using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
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
            DoingNothing,
            Playing,
            Paused
        }
        public PlayerState CurrentPlayerState { get; set; }
        private MediaList MediaList { get; set; }
        public String? PickedFolderToken { get; set; }
        private IProgress<String> FNameProgress { get; set; }
        private Boolean OnXBox { get; set; }

        // For passing commands from UI to Player
        public ConcurrentQueue<PlayerCommand> CommandQueue { get; private set; }
        public int DelayBetweenImges { get; set; } // MS
        public Boolean MediaListLoaded { get; set; }
        public Boolean AcceptingCommands { get; set; } = true; // Commands set while this is false will be ignored
        ThreadPoolTimer? NextImageTimer { get; set; } = null;
        public Image[] ImagePane { get; set; } = new Image[2];
        public MediaPlayerElement[] MediaPlayerPane { get; set; } = new MediaPlayerElement[2];
        public Storyboard[] ImageFadeStoryBoard { get; set; } = new Storyboard[2];
        public DoubleAnimation[] ImageFadeAnimation { get; set; } = new DoubleAnimation[2];
        public ProgressRing? WorkingThing { get; set; }
        private Boolean PlayPrevious { get; set; } = false;

        /// <summary>
        /// Contstructor
        /// </summary>
        /// <param name="fNameProgress"></param>
        /// <param name="progressBarProgress"></param>
        [RequiresUnreferencedCode("Calls SimpleSlide.MediaList.MediaList(Progress<String>)")]
        public Player(String pickedFolderToken, Progress<String> fNameProgress)
        {

            MediaList = new(fNameProgress);
            PickedFolderToken = pickedFolderToken;
            FNameProgress = fNameProgress;
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
                            NextImageTimer = ThreadPoolTimer.CreateTimer(NextImageHandler
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
                                NextImageHandler(null);
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
                                SetWorkingThing(false);
                                NextImageHandler(null); // At differet folder, show image
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
                    ImagePane[0].Source = ImagePane[1].Source = null;
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
            NextImageHandler(null); // Fire the timer delegate
        }

        /// <summary>
        /// Respod to timer event and play next or previous image
        /// </summary>
        /// <param name="timer"></param>
        [RequiresDynamicCode("Calls SimpleSlide.Player.NextOrPrevImage()")]
        [RequiresUnreferencedCode("Calls SimpleSlide.Player.NextOrPrevImage()")]
        private async void NextImageHandler(ThreadPoolTimer? timer)
        {
            if (MediaListLoaded)
            {
                AcceptingCommands = false;
                try
                {
                    await NextOrPrevImage();
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
                                Message1 = SimpleSlide.Strings.LookingFor + SimpleSlide.Strings.MediaTypes
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

            if (CurrentPlayerState == PlayerState.Playing)
                // Schedule to return in DelayBetweenImges milliseconds
                NextImageTimer?.Cancel(); NextImageTimer = null;
                NextImageTimer = ThreadPoolTimer.CreateTimer(NextImageHandler
                            , TimeSpan.FromMilliseconds(DelayBetweenImges));
        }

        [RequiresDynamicCode("Calls SimpleSlide.MediaList.GetNextMedia()")]
        [RequiresUnreferencedCode("Calls SimpleSlide.MediaList.GetNextMedia()")]
        private async Task NextOrPrevImage()
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

        private int FadeInImageNum { get; set; } = 0;

        [MethodImpl(MethodImplOptions.NoOptimization)]
        private async Task ShowMedia(StorageFile? sF)
        {
            if (null == sF)
                return;

            // Select image element and storyboard, moving back and forth between the two.
            Image thisImage = ImagePane[FadeInImageNum];
            Storyboard thisStoryboard = ImageFadeStoryBoard[FadeInImageNum];
            DoubleAnimation thisAnimation = ImageFadeAnimation[FadeInImageNum];
            FadeInImageNum = (0 == FadeInImageNum) ? 1 : 0;
            Image otherImage = ImagePane[FadeInImageNum]; // This'll be the currently displayed image
            Storyboard otherStoryboard = ImageFadeStoryBoard[FadeInImageNum];
            DoubleAnimation otherAnimation = ImageFadeAnimation[FadeInImageNum]; ;

            // The following activities must take place on the UI thread, so use the Dispatcher to toss them over,
            // via a lambda expression, to that thread.
            await Windows.ApplicationModel.Core.CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(
                    Windows.UI.Core.CoreDispatcherPriority.Normal,
                    async () =>
                {
                    // Set the image source to the selected bitmap 
                    var bitmapImage = new BitmapImage()
                    {
                        CreateOptions = BitmapCreateOptions.IgnoreImageCache // Necessary ??
                    };

                    bitmapImage.DecodePixelWidth = (int)thisImage.Width; //match the target Image.Width, not shown
                    bitmapImage.ImageOpened += (s, e) =>
                    {
                        try
                        {
                            // For making new image appear
                            //var thisDoubleAnimation = thisStoryboard.Children[0] as DoubleAnimation;
                            thisAnimation?.Duration = new Duration(TimeSpan.FromSeconds(1));
                            thisAnimation?.From = 0D;
                            thisAnimation?.To = 1D;

                            // For making existig image disappear
                            //var otherDoubleAnimation = otherStoryboard.Children[0] as DoubleAnimation;
                            otherAnimation?.Duration = new Duration(TimeSpan.FromSeconds(0));
                            otherAnimation?.From = 1D;
                            otherAnimation?.To = 0D;

                            otherStoryboard.Begin();
                            thisStoryboard.Begin();
                        }
                        catch (Exception ex)
                        {
                            String mStr = ex.Message;
                        }
                    };
                    IRandomAccessStream fileStream = await sF.OpenAsync(Windows.Storage.FileAccessMode.Read);
                    await bitmapImage.SetSourceAsync(fileStream);
                    thisImage?.Source = bitmapImage;
                }
                );
            FNameProgress.Report(MediaList.CurrentFolderName(true) + sF.Name);
        }
    }
}
