using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.Search;
using Windows.Storage.Streams;
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
            Stop,       // Stop the player
            Play,       // Start new from StartFolder
            Pause,      // Pause play
            Continue,   // Contiue play
            Forward,    // Move on to next item/pic
            Backward,   // Go to previous item/pic
            ChangeSpeed // Change speed to this many milliseconds between images (Value)
        }
        public PlayerCommands Command { get; set; }
        public int Value { get; set; } = 0;
        public PlayerCommand() { }
    }

    /// <summary>
    /// One of these is pushed on the Stack, for each folder being traversed
    /// </summary>
    internal class FolderState
    {
        public int CurrentFolderNum { get; set; } = 0;
        public IReadOnlyList<StorageFolder>? FolderList { get; set; }

        public FolderState() { }
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
        public String? PickedFolderToken { get; set; }
        public IProgress<String> FNameProgress;

        // For passing commands from UI to Player
        public ConcurrentQueue<PlayerCommand> CommandQueue { get; private set; }
        private IReadOnlyList<StorageFile>? CurrentFolderFileList { get; set; } // All the files in a folder
        private int NextFileNum;
        private Stack<FolderState> FoldersStack = new();
        public int DelayBetweenImges { get; set; }

        public Image?[] ImagePane = new Image[3];

        /// <summary>
        /// Contstructor
        /// </summary>
        /// <param name="fNameProgress"></param>
        public Player(String pickedFolderToken, Progress<String> fNameProgress)
        {
            PickedFolderToken = pickedFolderToken;
            FNameProgress = fNameProgress;
            CommandQueue = new();
            CurrentPlayerState = PlayerState.DoingNothing; // Doing Nothing;
        }

        /// <summary>
        /// Play media and respond to commands
        /// </summary>
        /// <returns></returns>
        /// <remarks>
        /// Log running background task
        /// Plays each media file in a folder then begis to descend into the subfolders.
        /// Recursive algorithm, but it is implemeted with a stack.
        /// </remarks>
        public async Task Play()
        {
            PlayerCommand? playerCommand;

            int i = 0;
            while (true)
            {
                // Anythig on the command queue?
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
                            break;
                        case PlayerCommand.PlayerCommands.Play:
                            CurrentPlayerState = PlayerState.Playing;
                            PrepForFolder();
                            i = 0; // Start new
                            break;
                        case PlayerCommand.PlayerCommands.ChangeSpeed:
                            DelayBetweenImges = playerCommand.Value; // Miliseconds
                            break;
                        default:
                            break;
                    }
                }

                if (PlayerState.Playing == CurrentPlayerState)
                {
                    /*
                     * if (there is a next media file in CurrentFolderFileList)
                     *  play it
                     * else
                     *  descend into next folder in this folder
                     *          PrepFolder();
                     * 
                     */
                    if (null != CurrentFolderFileList) // List is ready/available
                    {
                        // Round and round
                        if (i >= CurrentFolderFileList.Count)
                            i = 0;

                        Image? imagePane = ImagePane[1];

                        StorageFile sF = CurrentFolderFileList[i];

                        using (IRandomAccessStream fileStream = await sF.OpenAsync(Windows.Storage.FileAccessMode.Read))
                        {
                            // Set the image source to the selected bitmap 
                            BitmapImage bitmapImage = new BitmapImage();
                            bitmapImage.DecodePixelWidth = (int)imagePane.Width; //match the target Image.Width, not shown
                            await bitmapImage.SetSourceAsync(fileStream);
                            imagePane?.Source = bitmapImage;
                        }

                        FNameProgress.Report(i.ToString() + " " + sF.Name);
                        i++;
                    }
                }

                await Task.Delay(DelayBetweenImges);
            }
        }

        /// <summary>
        /// Prepare to begin playig from the taget directory named in commandString
        /// </summary>
        /// <param name="storageFolder">Pass in a StorageFolder is available. Otherwie will get it
        /// from the PickedFolderToken of the FutureAccessList.</param>
        /// <remarks>
        /// These types
        /// .jpeg, .jpg, .png, .bmp, gif, .tiff, .ico, .svg
        /// </remarks>
        private async void PrepForFolder(Windows.Storage.StorageFolder? storageFolder=null)
        {
            var folder = (Windows.Storage.StorageFolder) await Windows.Storage.AccessCache.StorageApplicationPermissions.
                FutureAccessList.GetItemAsync(PickedFolderToken);

            // Retrieve list of any media files
            // There is a bug in QueryOptios class that causes a cast exception when a List is passed
            // for the fie type filter list. There is mention of it in various forums. Only work-around
            // found is what is impemented here...
            var fileTypeFilterList = new List<String>() { ".jpeg", ".jpg", ".png", ".bmp", ".gif", ".tiff", ".ico", ".svg" };
            //var queryOptions = new QueryOptions(CommonFileQuery.OrderByName, fileTypeFilterList);
            var queryOptions = new QueryOptions();
            foreach(String fileType in fileTypeFilterList)
                queryOptions.FileTypeFilter.Add(fileType);
            var query = folder.CreateFileQueryWithOptions(queryOptions);
            CurrentFolderFileList = null;
            CurrentFolderFileList = await query.GetFilesAsync();   // Files in current folder

            //            foreach (StorageFile file in fileList)
            //            {
            //                outputText.Append(file.Name + "\n");
            //            }

            // Get list of all the folders in this folder. Push that list
            // onto the folder stack. Gonna do it this way instead of using true recursion.
            IReadOnlyList<StorageFolder>? currentFolderFolderList;      // All the folders in a folder
            currentFolderFolderList = await folder.GetFoldersAsync();   // Folders in current folder

            // Make a FolderState to push onto FoldersStack
            FolderState folderState = new()
            {
                FolderList = currentFolderFolderList
            };
            FoldersStack.Push(folderState);
        }
    }
}
