using System;
using System.Collections.Concurrent;
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

        private MediaList? MediaList;
        public String? PickedFolderToken { get; set; }
        public IProgress<String> FNameProgress;

        // For passing commands from UI to Player
        public ConcurrentQueue<PlayerCommand> CommandQueue { get; private set; }
        public int DelayBetweenImges { get; set; } // MS
        public Boolean Ready { get; set; }
        public Boolean AcceptingCommands { get; set; } = true; // Commands set while this is false will be ignored
        ThreadPoolTimer? ThreadPoolTimer { get; set; } = null;
        public Image?[] ImagePane = new Image[3];
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
            Ready = false;
        }

        /// <summary>
        /// Play media and respond to commands
        /// </summary>
        /// <returns></returns>
        /// <remarks>
        /// Log running background task
        /// Responds to commands comin i from CommandQueue.
        /// Plays each media file in a folder then begis to descend into the subfolders.
        /// Recursive algorithm, implemeted via Stack class.
        /// Images are played on yet another thread referenced by ThreadPoolTimer.
        /// </remarks>
        public async Task Play()
        {
            PlayerCommand? playerCommand;

            while (true)
            {
                // Anything on the command queue?
                if (CommandQueue.TryDequeue(out playerCommand))
                {
                    switch (playerCommand.Command)
                    {
                        case PlayerCommand.PlayerCommands.Stop:
                            CurrentPlayerState = PlayerState.DoingNothing;
                            break;
                        case PlayerCommand.PlayerCommands.Pause:
                            CurrentPlayerState = PlayerState.Paused;
                            break;
                        case PlayerCommand.PlayerCommands.Continue:
                            CurrentPlayerState = PlayerState.Playing;
                            TimerElapsedHandler(null);
                            break;
                        case PlayerCommand.PlayerCommands.NewFolderStart:
                            //ProgressBarProgress.Report(true);
                            await MediaList.PrepForFolder();
                            //ProgressBarProgress.Report(false);
                            CurrentPlayerState = PlayerState.Playing;
                            // Thread to play the images
                            ThreadPoolTimer = ThreadPoolTimer.CreateTimer(TimerElapsedHandler
                                                    , TimeSpan.FromMilliseconds(DelayBetweenImges));
                            Ready = true;
                            break;
                        case PlayerCommand.PlayerCommands.ChangeSpeed:
                            DelayBetweenImges = playerCommand.Value; // Miliseconds
                            break;
                        case PlayerCommand.PlayerCommands.Next:
                            // Kill timer, immediately move to next image
                            ThreadPoolTimer?.Cancel();
                            PlayPrevious = false; // to be sure
                            TimerElapsedHandler(null);
                            break;
                        case PlayerCommand.PlayerCommands.Previous:
                            // Kill timer, immediately move to previous image
                            ThreadPoolTimer?.Cancel();
                            PlayPrevious = true;
                            TimerElapsedHandler(null);
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
        /// Respod to timer event and play next image
        /// </summary>
        /// <param name="timer"></param>
        private async void TimerElapsedHandler(ThreadPoolTimer? timer)
        {
            if (Ready)
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
        }
        private async Task ShowNextImage()
        {
            StorageFile? sF = MediaList?.GetNextMedia();
            await ShowImage(sF);
        }
        private async Task ShowPrevImage()
        {
            StorageFile? sF = MediaList?.GetPreviousMedia();
            await ShowImage(sF);
        }
        private async Task ShowImage(StorageFile? sF)
        {
            if (null == sF)
                return;

            Image? imagePane = ImagePane[1];

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
                FNameProgress.Report(sF.Name);
            }
        }
    }
}
