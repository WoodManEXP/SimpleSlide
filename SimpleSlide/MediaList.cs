using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.Search;

namespace SimpleSlide
{
    /// <summary>
    /// Manages traversal of the media to be played
    /// </summary>
    internal class MediaList
    {
        //public String? PickedFolderToken { get; set; }
        private IProgress<String> FNameProgress { get; set; }
        public Boolean Ready { get; set; } = false; // Ready to support playing.
        private FolderStateStack FolderStateStack { get; set; } = new();
        private List<String> FileTypeFilterList { get; set; }
        private QueryOptions QueryOptionsFiles { get; set; }
        private QueryOptions QueryOptionsFolders { get; set; }
        //private DataContractJsonSerializer DataContractJsonSerializer { get; set; }
        private Boolean EncounteredA_MediaFile { get; set; } = false;
        private Boolean OnXBox { get; set; }

        public MediaList(Progress<String> fNameProgress)
        {
            FNameProgress = fNameProgress;

            // Running on XBox ?
            OnXBox = Windows.System.Profile.AnalyticsInfo.VersionInfo.DeviceFamily == "Windows.Xbox";

            // Appears to be an issue in QueryOptions class causing a cast exception when a List is passed
            // for the fie type filter list. There is mention of it in various forums. Only work-around
            // found is what is impemented here...
            // var fileTypeFilterList = new List<String>() { ".jpeg", ".jpg", ".png", ".bmp", ".gif", ".tiff", ".ico", ".svg" };
            // var QueryOptions = new QueryOptions(CommonFileQuery.OrderByName, fileTypeFilterList);
            FileTypeFilterList = SimpleSlide.Strings.MediaTypes.Split(',').ToList();
            QueryOptionsFiles = new()
            {
                FolderDepth = FolderDepth.Shallow, // Just this folder
                IndexerOption = IndexerOption.UseIndexerWhenAvailable
            };
            QueryOptionsFolders = new(CommonFolderQuery.DefaultQuery)
            {
                FolderDepth = FolderDepth.Shallow, // Just this folder
                IndexerOption = IndexerOption.UseIndexerWhenAvailable
            };
            foreach (String fileType in FileTypeFilterList)
                QueryOptionsFiles.FileTypeFilter.Add(fileType);
        }

        /// <summary>
        /// Call this to let MediaList know a new/different set of folders has been selected
        /// </summary>
        public void FreshStart()
        {
            EncounteredA_MediaFile = false;
            FolderStateStack.Clear(); // Back to empty state
        }

        public String CurrentFolderName(Boolean prependNumber = false)
        {
            if (0 == FolderStateStack.FolderStack.Count)
                return new("");

            String aStr;

            FolderState folderState = FolderStateStack.FolderStack.Peek();
            if (null != folderState)
            {
                StorageFolder storageFolder = folderState.StorageFolder;
                if (null != storageFolder)
                {
                    if (prependNumber)
                        aStr = (1 + folderState.LastFileNum).ToString() + ":" + folderState.FileCount.ToString() + " " + storageFolder.Name + "\\";
                    else
                        aStr = storageFolder.Name;
                }
                else
                    aStr = SimpleSlide.Strings.HashSigns;
            }
            else
                aStr = SimpleSlide.Strings.HashSigns;

            return aStr;
        }

        /// <summary>
        /// Get next media from list
        /// </summary>
        /// <returns></returns>
        public async Task<StorageFile?> GetNextMedia()
        {
            StorageFile? sF;
            try
            {
                sF = await StackNextMedia();

                await FolderStateStack.SerializeState(); // place in folder hierachy has changed, remember.

                if (null != sF) // Have encountered at least one media file 
                    EncounteredA_MediaFile = true;
            }
            catch (NoMediaException)
            {
                throw new NoMediaException();
            }
            return sF;
        }

        /// <summary>
        /// Get previous media from list
        /// </summary>
        /// <returns></returns>
        public async Task<StorageFile?> GetPreviousMedia()
        {
            StorageFile? sF = await StackPreviousMedia();

            await FolderStateStack.SerializeState(); // place in folder hierachy has changed, remember.

            if (null != sF) // Have encountered at least one media file 
                EncounteredA_MediaFile = true;

            return sF;
        }

        /// <summary>
        /// Work the Stack/folde-tree to get next media file
        /// </summary>
        /// <returns></returns>
        /// <remarks>
        /// This begs to be impemented via recursion, but recursion does not fit well with
        /// other needs. So a loop and stack are used.
        /// </remarks>
        private async Task<StorageFile?> StackNextMedia()
        {
            if (0 == FolderStateStack.FolderStack.Count) // JIC
                return null;

            FolderState? folderState = FolderStateStack.FolderStack.Peek();

            while (true)
            {
                // Nothing in the FolderStack. This case can also happen if the initial
                // folder has no media files and no subfolders.
                if (null == folderState)
                    return null;

                // Have all files in folder hierarchy been processed?
                // This is checking for having reached the end of the top-most folder. Having
                // done this means the top folder and all below it have been traversed.
                if (FolderStateStack.FolderStack.Count == 1)
                    if (1 + folderState.LastFileNum >= folderState.FileCount || 0 == folderState.FileCount)
                        if (1 + folderState.LastFolderNum >= folderState.FolderCount || 0 == folderState.FolderCount)
                        {
                            folderState.LastFileNum = folderState.LastFolderNum = -1; // Go back to beginning

                            // Folder hierarchy and contents have been fully traversed with
                            // no playable media encoutered.
                            if (!EncounteredA_MediaFile)
                                throw new NoMediaException();
                        }

                // Any next files remaining in this folder?
                if (1 + folderState.LastFileNum < folderState.FileCount)
                    return await GetStorageFile(++folderState.LastFileNum);

                // No more next files this folder, start working the folder list
                if (1 + folderState.LastFolderNum < folderState.FolderCount)
                {
                    // Onto next folder
                    await PrepForFolder(await GetStorageFolder(++folderState.LastFolderNum));
                    folderState = FolderStateStack.FolderStack.Peek();
                }
                else
                {
                    // No more folders, pop the stack
                    FolderStateStack.FolderStack.Pop();
                    folderState = (FolderStateStack.FolderStack.Count > 0) ? FolderStateStack.FolderStack.Peek() : null;
                }
            }
        }

        /// <summary>
        /// Work the Stack to get previous media
        /// </summary>
        /// <remarks>
        /// Prep te next folder to start at the beining
        /// </remarks>
        /// <returns></returns>
        private async Task<StorageFile?> StackPreviousMedia()
        {
            if (0 == FolderStateStack.FolderStack.Count)  // JIC
                return null;

            FolderState? folderState = FolderStateStack.FolderStack.Peek();

            // Boundary case: at the beginning?
            if (1 == FolderStateStack.FolderStack.Count && folderState.LastFileNum <= 0)
                return null;

            while (true)
            {
                if (null == folderState)
                    return null; // Nothing in the FolderStack

                // Any previous files remaining in this folder?
                if (folderState.FileCount != 0 && folderState.LastFileNum > 0)
                    return await GetStorageFile(--folderState.LastFileNum);

                // *************************************************
                // What is this ????
                // No more previous files this folder, start working the folder list
                if (folderState.LastFolderNum - 1 > folderState.FolderCount)
                {
                    // Onto next folder
                    await PrepForFolder(await GetStorageFolder(folderState.LastFolderNum));
                    folderState = FolderStateStack.FolderStack.Peek();
                }
                else
                {
                    // No more folders, pop the stack
                    FolderStateStack.FolderStack.Pop();
                    folderState = (FolderStateStack.FolderStack.Count > 0) ? FolderStateStack.FolderStack.Peek() : null;
                    if (null != folderState)
                    {
                        folderState.LastFolderNum = -1; // Popping back to a parent folder. Prep to possibly start again
                                                        // with its first child folder.
                        if (folderState.FileCount > 0)  // So the predecrement above will start at the last pic
                            folderState.LastFileNum++;  // in the folder.
                    }
                }
            }
        }

        /// <summary>
        /// Move player MediaList into beginning of next folder
        /// </summary>
        /// <remarks>
        /// Prep te next folder to start at the beining
        /// </remarks>
        /// <returns></returns>
        public async Task StackNextFolder()
        {
            if (0 == FolderStateStack.FolderStack.Count) // JIC
                return;

            FolderState folderState = FolderStateStack.FolderStack.Peek();

            while (true)
            {
                // Is a next folder available in the hierarchy?
                if (FolderStateStack.FolderStack.Count == 1)
                    if (1 + folderState.LastFolderNum >= folderState.FolderCount || 0 == folderState.FolderCount)
                    {
                        folderState.LastFileNum = folderState.LastFolderNum = -1; // Go back to beginning
                        break;
                    }

                if (folderState.FolderCount > 0 && 1 + folderState.LastFolderNum < folderState.FolderCount)
                {
                    // Skip any remaining media in this folder (for when/if it pops back here)
                    folderState.LastFileNum = folderState.FileCount;

                    // Onto next folder
                    await PrepForFolder(await GetStorageFolder(++folderState.LastFolderNum));
                    break;
                }

                // No more folders, pop the stack
                FolderStateStack.FolderStack.Pop();
                folderState = FolderStateStack.FolderStack.Peek();
            }
        }

        /// <summary>
        /// Move player MediaList into beginning of previous folder
        /// </summary>
        /// <returns></returns>
        public async Task StackPrevFolder()
        {
            if (0 == FolderStateStack.FolderStack.Count) // JIC
                return;

            FolderState folderState = FolderStateStack.FolderStack.Peek();

            while (true)
            {
                // Look for a previous folder available in the hierarchy?

                if (FolderStateStack.FolderStack.Count == 1) // At the top
                    if (0 == folderState.FolderCount                                // No folders
                        || 1 + folderState.LastFolderNum >= folderState.FolderCount // already at end of of folder
                        || -1 == folderState.LastFolderNum                          // Had not started traversing folders
                        || 0 == folderState.LastFolderNum)                          // Were in the first folder
                    {
                        // Go back to beginning of top folder
                        folderState.LastFileNum = folderState.LastFolderNum = -1; // Go back to beginning
                        break;
                    }

                // Back up in present folder?
                if (0 != folderState.FolderCount && -1 + folderState.LastFolderNum >= 0)
                {
                    await PrepForFolder(await GetStorageFolder(--folderState.LastFolderNum));
                    break;
                }

                // No more folders in preset folder, pop the stack
                FolderStateStack.FolderStack.Pop();
                folderState = FolderStateStack.FolderStack.Peek();
            }
        }

        /// <summary>
        /// Retrieves the StorgeFile at position n
        /// </summary>
        /// <param name="n"></param>
        /// <returns></returns>
        private async Task<StorageFile> GetStorageFile(int n)
        {
            FolderState folderState = FolderStateStack.FolderStack.Peek();
            var singleFileList = await folderState.StorageFileQuery.GetFilesAsync((uint)n, 1);
            return singleFileList[0];
        }

        /// <summary>
        /// Retrieves the StorgeFolder at position n
        /// </summary>
        /// <param name="n"></param>
        /// <returns></returns>
        private async Task<StorageFolder> GetStorageFolder(int n)
        {
            FolderState fS = FolderStateStack.FolderStack.Peek();
            var storageFolder = fS.StorageFolder;
            var singleFolderList = await storageFolder.GetFoldersAsync(CommonFolderQuery.DefaultQuery, (uint)n, 1);
            return singleFolderList[0];
        }

        /// <summary>
        /// Prepare to begin playing from a folder
        /// </summary>
        /// <param name="sF">Pass in a StorageFolder is available. Otherwie will get it
        /// from the PickedFolderToken of the FutureAccessList.</param>
        /// <remarks>
        /// These types
        /// .jpeg, .jpg, .png, .bmp, gif, .tiff, .ico, .svg
        /// </remarks>
        public async Task PrepForFolder(Windows.Storage.StorageFolder sF)
        {
            // Make a FolderState to push onto FoldersStack
            FolderState folderState = new(sF, QueryOptionsFiles, QueryOptionsFolders);
            await folderState.PostCtor(); // For async things that cannot happen in constructor
                                          // Be cool to not await here, becasue this can be long, long...
                                          // Tough to implemet with the side-effects
            FolderStateStack.FolderStack.Push(folderState);
            Ready = true;
        }

        /// <summary>
        /// Attempt to brig in the saved state.
        /// </summary>
        /// <returns></returns>
        public async Task<Boolean> DeserializeState()
        {
            // Message that this, perhaps lengthly operation, is underway
            if (OnXBox)
                FNameProgress.Report(SimpleSlide.Strings.FromLastTimeXBox);
            else
                FNameProgress.Report(SimpleSlide.Strings.FromLastTime);

            return (Ready = await FolderStateStack.DeserializeState(QueryOptionsFiles, QueryOptionsFolders));
        }
    }
}