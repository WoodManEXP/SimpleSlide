using System;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.System.Threading;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media.Animation;
using Windows.UI.Xaml.Media.Imaging;

namespace SimpleSlide
{
    /// <summary>
    /// Place one of these on Queue to get commmad to the Player 
    /// </summary>
    internal class PlayerCommand
    {
        public enum PlayerCommands
        {
            Stop,               // Stop the player
            NewFolderStart,     // Start new from StartFolder
            Pause,              // Pause play
            Continue,           // Contiue play
            PrevMedia,          // Back to previous media
            NextMedia,          // Move on to next media
            ChangeSpeed,        // Change speed to this many milliseconds between images (Value)
            PrevFolder,         // Back to previous folder
            NextFolder          // Move on to next folde
        }
        public PlayerCommands Command { get; set; }
        public int Value { get; set; } = 0;
        public PlayerCommand() { }
    }

    /// <summary>
    /// Background task doing the media playing
    /// </summary>
    internal class Player
    {
        public enum PlayerState // What is player doing
        {
            DoingNothing, Playing, Paused
        }
        public PlayerState CurrentPlayerState { get; set; }
        public enum MediaType
        {
            None, Image, Video
        }
        public MediaType ThisMediaType { get; private set; } = MediaType.None;
        private MediaType OtherMediaType { get; set; } = MediaType.None;
        private MediaList MediaList { get; set; }
        public String? PickedFolderToken { get; set; }
        private IProgress<String> FNameProgress { get; set; }
        private IProgress<CommandSignals> CommandProgress { get; set; }
        private Boolean OnXBox { get; set; }

        // For passing commands from UI to Player
        public ConcurrentQueue<PlayerCommand> CommandQueue { get; private set; }
        public int DelayBetweenImges { get; set; } // MS
        public Boolean MediaListLoaded { get; set; }
        public Boolean AcceptingCommands { get; set; } = true; // Commands set while this is false will be ignored
        ThreadPoolTimer? NextImageTimer { get; set; } = null;
        public Image[] Image { get; set; } = new Image[2];
        public MediaPlayerElement MediaPlayerElement { get; set; }
        public Storyboard[] MediaStoryBoard { get; set; } = new Storyboard[2];
        public DoubleAnimation[] MediaAnimation { get; set; } = new DoubleAnimation[2];
        public MediaTransportControls TransportControl { get; set; }
        public ProgressRing? WorkingThing { get; set; }
        private Boolean PlayPrevious { get; set; } = false;

        /// <summary>
        /// Contstructor
        /// </summary>
        /// <param name="fNameProgress"></param>
        /// <param name="progressBarProgress"></param>
        [RequiresUnreferencedCode("Calls SimpleSlide.MediaList.MediaList(Progress<String>)")]
        public Player(String pickedFolderToken, Progress<String> fNameProgress, Progress<CommandSignals> commandProgress)
        {

            MediaList = new(fNameProgress);
            PickedFolderToken = pickedFolderToken;
            FNameProgress = fNameProgress;
            CommandProgress = commandProgress;
            CommandQueue = new();
            CurrentPlayerState = PlayerState.DoingNothing; // Doing Nothing;
            MediaListLoaded = false;

            // Running on XBox ?
            OnXBox = Windows.System.Profile.AnalyticsInfo.VersionInfo.DeviceFamily == "Windows.Xbox";
        }

        /// <summary>
        /// Play media and respond to commands
        /// </summary>
        /// <returns></returns>
        /// <remarks>
        /// Long running background task
        /// Responds to commands coming in from CommandQueue.
        /// Plays each media file in a folder then begis to descend into the subfolders.
        /// Recursive algorithm, implemeted via Stack class.
        /// Images are played on yet another thread referenced by ThreadPoolTimer.
        /// </remarks>
        [RequiresUnreferencedCode("Calls SimpleSlide.MediaList.CtorAsyc()")]
        [RequiresDynamicCode("Calls SimpleSlide.MediaList.CtorAsyc()")]
        public async Task Play()
        {
            // Check if there is any persistant state available. If so, start the player -> this
            // has the effect of automatic playing starting from where it left off in previous run.
            SetWorkingThing(true);
            if (OnXBox)
                FNameProgress.Report(SimpleSlide.Strings.FromLastTimeXBox); // 
            else
                FNameProgress.Report(SimpleSlide.Strings.FromLastTime); // 
            if (await MediaList.DeserializeState())
            {
                MediaListLoaded = true;
                ContinuePlaying();
            }
            else
                FNameProgress.Report(SimpleSlide.Strings.SelectFolder);
            SetWorkingThing(false);

            while (true)
            {
                // Anything on the command queue?
                if (CommandQueue.TryDequeue(out PlayerCommand? playerCommand))
                {
                    switch (playerCommand.Command)
                    {
                        case PlayerCommand.PlayerCommands.Stop:
                            CurrentPlayerState = PlayerState.DoingNothing;
                            break;
                        case PlayerCommand.PlayerCommands.Pause:
                            NextImageTimer?.Cancel(); NextImageTimer = null;
                            CurrentPlayerState = PlayerState.Paused;
                            break;
                        case PlayerCommand.PlayerCommands.Continue:
                            ContinuePlaying();
                            break;
                        case PlayerCommand.PlayerCommands.NewFolderStart:
                            SetWorkingThing(true);
                            NoImages();
                            MediaList.FreshStart();
                            StorageFolder sF = (Windows.Storage.StorageFolder)await Windows.Storage.AccessCache.StorageApplicationPermissions.
                                        FutureAccessList.GetItemAsync(PickedFolderToken);
                            await MediaList.PrepForFolder(sF);
                            CurrentPlayerState = PlayerState.Playing;
                            // Thread to play the images
                            NextImageTimer?.Cancel(); NextImageTimer = null;
                            NextImageTimer = ThreadPoolTimer.CreateTimer(NextMediaHandler
                                                    , TimeSpan.FromMilliseconds(DelayBetweenImges));
                            MediaListLoaded = true;
                            SetWorkingThing(false);
                            break;
                        case PlayerCommand.PlayerCommands.ChangeSpeed:
                            DelayBetweenImges = playerCommand.Value; // Miliseconds
                            break;
                        case PlayerCommand.PlayerCommands.PrevMedia:
                        case PlayerCommand.PlayerCommands.NextMedia:
                            if (MediaListLoaded)
                            {
                                // Kill timer, immediately move to next/prev image
                                NextImageTimer?.Cancel(); NextImageTimer = null;
                                PlayPrevious = (PlayerCommand.PlayerCommands.PrevMedia == playerCommand.Command) ? true : false;
                                NextMediaHandler(null);
                                CommandProgress.Report(CommandSignals.MovementNotUnderway);
                            }
                            break;
                        case PlayerCommand.PlayerCommands.PrevFolder:
                        case PlayerCommand.PlayerCommands.NextFolder:
                            if (MediaListLoaded)
                            {
                                SetWorkingThing(true);
                                // Kill timer, immediately move to next/prev folder
                                NextImageTimer?.Cancel(); NextImageTimer = null;
                                PlayPrevious = false; // to be sure
                                if (PlayerCommand.PlayerCommands.PrevFolder == playerCommand.Command)
                                {
                                    FNameProgress.Report(SimpleSlide.Strings.FolderPrev);
                                    await MediaList.StackPrevFolder();
                                }
                                else
                                {
                                    FNameProgress.Report(SimpleSlide.Strings.FolderNext);
                                    await MediaList.StackNextFolder();
                                }
                                NextMediaHandler(null); // At differet folder, show image
                                SetWorkingThing(false);
                                CommandProgress.Report(CommandSignals.MovementNotUnderway);
                            }
                            break;
                        default:
                            break;
                    }
                }
                // As this is an infinite loop, yield a bit to give the rest of the system a chance
                await Task.Delay(50);
            }
        }

        /// <summary>
        /// Remove any current images
        /// </summary>
        private async void NoImages()
        {
            // No need to wait on this
            _ = Windows.ApplicationModel.Core.CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(
                Windows.UI.Core.CoreDispatcherPriority.Normal,
                (Windows.UI.Core.DispatchedHandler)(async () =>
                {
                    // Set no media
                    Image[0].Source = Image[1].Source = null;
                    MediaPlayerElement.Source = null;
                })
            );
        }

        /// <summary>
        /// Start/Stop the working thing indicator
        /// </summary>
        /// <param name="set"></param>
        private async void SetWorkingThing(Boolean set)
        {
            // No need to wait on this
            _ = Windows.ApplicationModel.Core.CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(
                Windows.UI.Core.CoreDispatcherPriority.Normal,
                (Windows.UI.Core.DispatchedHandler)(async () =>
                {
                    if (set)
                    {
                        WorkingThing?.IsActive = true;
                        WorkingThing?.Visibility = Visibility.Visible;
                    }
                    else
                    {
                        WorkingThing?.IsActive = false;
                        WorkingThing?.Visibility = Visibility.Collapsed;
                    }
                })
            );
        }

        [RequiresUnreferencedCode("Calls SimpleSlide.Player.NextImageHandler(ThreadPoolTimer)")]
        [RequiresDynamicCode("Calls SimpleSlide.Player.NextImageHandler(ThreadPoolTimer)")]
        private void ContinuePlaying()
        {
            CurrentPlayerState = PlayerState.Playing;
            NextMediaHandler(null); // Fire the timer delegate
        }

        /// <summary>
        /// Respod to timer event and play next or previous image
        /// </summary>
        /// <param name="timer"></param>
        [RequiresDynamicCode("Calls SimpleSlide.Player.NextOrPrevImage()")]
        [RequiresUnreferencedCode("Calls SimpleSlide.Player.NextOrPrevImage()")]
        private async void NextMediaHandler(ThreadPoolTimer? timer)
        {
            if (MediaListLoaded)
            {
                AcceptingCommands = false;

                // Ensure imer stopped
                NextImageTimer?.Cancel();
                NextImageTimer = null;

                try
                {
                    await NextOrPrevMedia();
                }
                catch (NoMediaException)
                {
                    // The following activities must take place on the UI thread, so use the Dispatcher to toss them over,
                    // via a lambda expression, to that thread.
                    await Windows.ApplicationModel.Core.CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(
                        Windows.UI.Core.CoreDispatcherPriority.Normal,
                        (Windows.UI.Core.DispatchedHandler)(async () =>
                        {
                            // Display dialog
                            MessageDialog messageDialog = new()
                            {
                                MessageTitle = SimpleSlide.Strings.NoMediaTitle,
                                Message0 = SimpleSlide.Strings.NoMediaMessage.Replace("_0", MediaList.CurrentFolderName(false)),
                                Message1 = SimpleSlide.Strings.LookingFor + SimpleSlide.Strings.ImageFileTypes
                                           + " "
                                           + SimpleSlide.Strings.VideoFileTypes
                            };
                            await messageDialog.ShowAsync();
                        })
                    );
                    FNameProgress.Report(SimpleSlide.Strings.SelectFolder);

                    // No media was located
                    CurrentPlayerState = PlayerState.DoingNothing;
                    MediaListLoaded = false;
                    MediaList.FreshStart();
                }
                finally
                {
                    AcceptingCommands = true;
                }
            }

            PlayPrevious = false; // Default state is to progress forward

            // If current media is not a Vid and playing is not Paused then schedule the timer.
            // Schedule to return in DelayBetweenImges milliseconds
            // (When a video finishes or next/prev command comes through things will progress)
            if (MediaType.Video != ThisMediaType && PlayerState.Paused != CurrentPlayerState)
                NextImageTimer = ThreadPoolTimer.CreateTimer(NextMediaHandler
                            , TimeSpan.FromMilliseconds(DelayBetweenImges));
        }

        [RequiresDynamicCode("Calls SimpleSlide.MediaList.GetNextMedia()")]
        [RequiresUnreferencedCode("Calls SimpleSlide.MediaList.GetNextMedia()")]
        private async Task NextOrPrevMedia()
        {
            StorageFile? sF;

            try
            {
                sF = PlayPrevious ? await MediaList.GetPreviousMedia() : await MediaList.GetNextMedia();
                await ShowMedia(sF);
            }
            catch (NoMediaException)
            {
                throw new NoMediaException();
            }
        }

        private int FadeInNum { get; set; } = 0;
        private Duration OtherDuration { get; set; } = new Duration(TimeSpan.FromSeconds(0.5));
        private Duration ThisDuration { get; set; } = new Duration(TimeSpan.FromSeconds(1));

        //[MethodImpl(MethodImplOptions.NoOptimization)]
        /// <summary>
        /// 
        /// </summary>
        /// <param name="sF"></param>
        /// <remarks>
        /// this - the new media to be shown
        /// other - the media that was being shown
        /// </remarks>
        /// <returns></returns>
        private async Task ShowMedia(StorageFile? sF)
        {
            if (null == sF)
                return;

            // Is the incoming sF image or video?
            Boolean thisIsAnImage = (MediaList.ImageFileTypes.Contains(sF.FileType.ToUpperInvariant())) ? true : false;
            Boolean otherIsAnImage = (MediaType.Video == ThisMediaType) ? false : true;

            Image thisImage, otherImage;
            Storyboard thisStoryboard, otherStoryboard;
            DoubleAnimation thisAnimation, otherAnimation;

            // Select image/video element and storyboard, moving back and forth between the two.
            thisImage = Image[FadeInNum];
            thisStoryboard = MediaStoryBoard[FadeInNum];
            thisAnimation = MediaAnimation[FadeInNum];
            FadeInNum = (0 == FadeInNum) ? 1 : 0;
            otherImage = Image[FadeInNum]; // This'll be the currently displayed media
            otherStoryboard = MediaStoryBoard[FadeInNum];
            otherAnimation = MediaAnimation[FadeInNum];

            OtherMediaType = ThisMediaType;
            ThisMediaType = thisIsAnImage ? MediaType.Image : MediaType.Video;

            //if (!thisIsAnImage)
            //    System.Diagnostics.Debugger.Break();

            // The following activities must take place on the UI thread, so use the Dispatcher to toss them over,
            // via a lambda expression, to that thread.
            await Windows.ApplicationModel.Core.CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(
                    Windows.UI.Core.CoreDispatcherPriority.Normal,
                    async () =>
                {
                    thisStoryboard.Stop();
                    otherStoryboard.Stop();

                    // Prep/target Storyboards and Animations for fade in/out
                    Storyboard.SetTargetName(otherAnimation, otherImage.Name);
                    Storyboard.SetTargetName(thisAnimation, thisImage.Name);
                    otherAnimation.Duration = OtherDuration;    // For making existing media disappear
                    otherAnimation.From = 1D;
                    otherAnimation.To = 0D;
                    thisAnimation.Duration = ThisDuration;      // For making new media appear
                    thisAnimation.From = 0D;
                    thisAnimation.To = 1D;

                    if (thisIsAnImage)
                    {
                        try
                        {
                            // Set the image source to the selected bitmap 
                            var bitmapImage = new BitmapImage()
                            {
                                CreateOptions = BitmapCreateOptions.IgnoreImageCache // Necessary ??
                            };

                            bitmapImage.DecodePixelWidth = (int)thisImage.Width; //match the target Image.Width, not shown
                            bitmapImage.ImageOpened += (s, e) =>
                            {
                                thisStoryboard.Begin();         // Fade-in new image
                                if (otherIsAnImage)
                                    otherStoryboard.Begin();    // Fade-out previous image
                                else
                                {   // Hide previous video pae and controls
                                    MediaPlayerElement.Visibility = Visibility.Collapsed;
                                    TransportControl.Visibility = Visibility.Collapsed;
                                }
                                ReleaseMedia(MediaPlayerElement, otherImage);
                            };
                            IRandomAccessStream fileStream = await sF.OpenAsync(Windows.Storage.FileAccessMode.Read);
                            await bitmapImage.SetSourceAsync(fileStream);
                            thisImage?.Source = bitmapImage;
                        }
                        catch (Exception ex)
                        {
                            String mStr = ex.Message;
                        }
                    }
                    else // This is a video
                    {
                        try
                        {
                            ReleaseMedia(MediaPlayerElement, otherImage);

                            MediaPlayerElement.Source = MediaSource.CreateFromStorageFile(sF);

                            // (Re)set event handler(s)
                            // Necessary after MediaSource changes.
                            MediaPlayerElement.MediaPlayer.MediaEnded -= MediaEnded;
                            MediaPlayerElement.MediaPlayer.MediaEnded += MediaEnded;

                            MediaPlayerElement.Visibility = Visibility.Visible;
                            TransportControl.Visibility = Visibility.Visible;
                            MediaPlayerElement.MediaPlayer.Play();
                        }
                        catch (Exception ex)
                        {
                            String mStr = ex.Message;
                        }
                    }
                }
                );

            // Show file info, adding the Paused string, if Paused.
            String aStr = MediaList.CurrentFolderName(true) + sF.Name;
            if (PlayerState.Paused==CurrentPlayerState)
                aStr = SimpleSlide.Strings.Paused + " " + aStr;
            FNameProgress.Report(aStr);
        }

        /// <summary>
        /// Handle MediaEnded event
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="args"></param>
        private void MediaEnded(MediaPlayer sender, object args)
        {
            // Move to next media whe video is completed
            // Send Next image command to Player
            this.CommandQueue.Enqueue(new PlayerCommand()
            {
                Command = PlayerCommand.PlayerCommands.NextMedia
            });
        }

        /// <summary>
        /// Release resoures that were associated with a XAML MediaPlayerElement
        /// </summary>
        /// <param name="mpe">MediaPlayerElement</param>
        /// <param name="image">Image elemet</param>
        /// <remarks>
        /// probaby no need for this as garbage would eventually collect the o longer referenced resoures
        /// </remarks>
        private void ReleaseMedia(MediaPlayerElement mpe, Image image)
        {
            // Free resources from previous video, if there was one
            var mediaPlayer = mpe.MediaPlayer;
            if (null != mediaPlayer)
            {
                IMediaPlaybackSource source = mediaPlayer.Source;
                if (source is MediaSource mediaSource)
                    mediaSource.Dispose();
            }
            if (null != image)
                image.Source = null;
        }
    }
}
