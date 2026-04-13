using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.System.Threading;
using Windows.UI.Xaml.Controls;
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
            Next,               // Move on to next item/pic
            Previous,           // Go to previous item/pic
            ChangeSpeed         // Change speed to this many milliseconds between images (Value)
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
        public IProgress<String> FNameProgress;

        // For passing commands from UI to Player
        public ConcurrentQueue<PlayerCommand> CommandQueue { get; private set; }
        public int DelayBetweenImges { get; set; } // MS
        public Boolean MediaListLoaded { get; set; }
        public Boolean AcceptingCommands { get; set; } = true; // Commands set while this is false will be ignored
        ThreadPoolTimer? ThreadPoolTimer { get; set; } = null;
        public Image[] ImagePane = new Image[3];
        private Boolean PlayPrevious { get; set; } = false;

        /// <summary>
        /// Contstructor
        /// </summary>
        /// <param name="fNameProgress"></param>
        /// <param name="progressBarProgress"></param>
        public Player(String pickedFolderToken, Progress<String> fNameProgress)
        {

            MediaList = new()
            {
                PickedFolderToken = pickedFolderToken
            };

            PickedFolderToken = pickedFolderToken;
            FNameProgress = fNameProgress;
            CommandQueue = new();
            CurrentPlayerState = PlayerState.DoingNothing; // Doing Nothing;
            MediaListLoaded = false;
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
            // has the effect of automatically playing starting from where it left off in previous run.
            if (await MediaList.DeserializeState())
            {
                MediaListLoaded = true;
                ContinuePlaying();
            }

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
                            ThreadPoolTimer?.Cancel();
                            CurrentPlayerState = PlayerState.Paused;
                            break;
                        case PlayerCommand.PlayerCommands.Continue:
                            ContinuePlaying();
                            break;
                        case PlayerCommand.PlayerCommands.NewFolderStart:
                            //ProgressBarProgress.Report(true);
                            await MediaList.PrepForFolder();
                            //ProgressBarProgress.Report(false);
                            CurrentPlayerState = PlayerState.Playing;
                            // Thread to play the images
                            ThreadPoolTimer = ThreadPoolTimer.CreateTimer(TimerElapsedHandler
                                                    , TimeSpan.FromMilliseconds(DelayBetweenImges));
                            MediaListLoaded = true;
                            break;
                        case PlayerCommand.PlayerCommands.ChangeSpeed:
                            DelayBetweenImges = playerCommand.Value; // Miliseconds
                            break;
                        case PlayerCommand.PlayerCommands.Next:
                            if (MediaListLoaded)
                            {
                                // Kill timer, immediately move to next image
                                ThreadPoolTimer?.Cancel();
                                PlayPrevious = false; // to be sure
                                TimerElapsedHandler(null);
                            }
                            break;
                        case PlayerCommand.PlayerCommands.Previous:
                            if (MediaListLoaded)
                            {
                                // Kill timer, immediately move to previous image
                                ThreadPoolTimer?.Cancel();
                                PlayPrevious = true;
                                TimerElapsedHandler(null);
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

        private void ContinuePlaying()
        {
            CurrentPlayerState = PlayerState.Playing;
            TimerElapsedHandler(null); // Fire the timer delegate
        }

        /// <summary>
        /// Respod to timer event and play next image
        /// </summary>
        /// <param name="timer"></param>
        private async void TimerElapsedHandler(ThreadPoolTimer? timer)
        {
            if (MediaListLoaded)
            {
                AcceptingCommands = false;
                if (PlayPrevious)
                    await ShowPrevImage();
                else
                    await ShowNextImage();
                AcceptingCommands = true;
            }

            PlayPrevious = false; // Default state is to progress forward

            if (CurrentPlayerState == PlayerState.Playing)
                // Schedule to return in DelayBetweenImges milliseconds
                ThreadPoolTimer = ThreadPoolTimer.CreateTimer(TimerElapsedHandler
                            , TimeSpan.FromMilliseconds(DelayBetweenImges));

            //Debug.WriteLine("DelayBetweenImges " + DelayBetweenImges.ToString());
        }
        private async Task ShowNextImage()
        {
            StorageFile? sF = await MediaList.GetNextMedia();
            await ShowImage(sF);
        }
        private async Task ShowPrevImage()
        {
            StorageFile? sF = await MediaList.GetPreviousMedia();
            await ShowImage(sF);
        }
        private async Task ShowImage(StorageFile? sF)
        {
            if (null == sF)
                return;

            Image imagePane = ImagePane[1];

            using (IRandomAccessStream fileStream = await sF.OpenAsync(Windows.Storage.FileAccessMode.Read))
            {
                // The following activities must take place on the UI thread, so use the Dispatcher to toss them over,
                // via a lambda expression, to that thread.
                await Windows.ApplicationModel.Core.CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(
                    Windows.UI.Core.CoreDispatcherPriority.Normal,
                    async () =>
                {
                    // Set the image source to the selected bitmap 
                    BitmapImage bitmapImage = new BitmapImage();
                    bitmapImage.DecodePixelWidth = (int)imagePane.Width; //match the target Image.Width, not shown
                    await bitmapImage.SetSourceAsync(fileStream);
                    imagePane?.Source = bitmapImage;
                }
                );
                FNameProgress.Report(MediaList.CurrentFolderName() + sF.Name);
            }
        }
    }
}
