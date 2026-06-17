using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.Search;
using Windows.UI.WindowManagement;
using Windows.UI.Xaml.Media.Animation;

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
        private QueryOptions QueryOptions { get; set; }
        private QueryOptions QueryOptionsFiles { get; set; }
        private QueryOptions QueryOptionsFolders { get; set; }
        //private DataContractJsonSerializer DataContractJsonSerializer { get; set; }
        private Boolean EncounteredA_MediaFile { get; set; } = false;

        public MediaList(Progress<String> fNameProgress)
        {
            FNameProgress = fNameProgress;

            // Appears to be an issue in QueryOptions class causing a cast exception when a List is passed
            // for the fie type filter list. There is mention of it in various forums. Only work-around
            // found is what is impemented here...
            // var fileTypeFilterList = new List<String>() { ".jpeg", ".jpg", ".png", ".bmp", ".gif", ".tiff", ".ico", ".svg" };
            // var QueryOptions = new QueryOptions(CommonFileQuery.OrderByName, fileTypeFilterList);
            FileTypeFilterList = SimpleSlide.Strings.MediaTypes.Split(',').ToList();
            QueryOptions = new();
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
            {
                QueryOptions.FileTypeFilter.Add(fileType);
                QueryOptionsFiles.FileTypeFilter.Add(fileType);
            }
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

            FolderState fS = FolderStateStack.FolderStack.Peek();
            if (null != fS)
            {
                StorageFolder sF = fS.StorageFolder;
                if (null != sF)
                {
                    if (prependNumber)
                        aStr = (1 + fS.LastFileNum).ToString() + ":" + fS.FileCount.ToString() + " " + sF.Name + "\\";
                    else
                        aStr = sF.Name;
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
                sF = await StackGetNext();

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
            StorageFile? sF = await StackGetPrevious();

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
        /// other needs. So a loop and Stack are used.
        /// </remarks>
        private async Task<StorageFile?> StackGetNext()
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
                        if (1 + folderState.LastFolderNum >= folderState.FolderCount | 0 == folderState.FolderCount)
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
        /// <returns></returns>
        private async Task<StorageFile?> StackGetPrevious()
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
        /// Get a count of files in a folder
        /// </summary>
        /// <param name="storageFolder"></param>
        /// <returns></returns>
        /// <remarks>
        /// File system on XBox seems particularly slow at this...
        /// </remarks>
        private async Task<int> FilesThisFolder(Windows.Storage.StorageFolder sF)
        {
            var query = sF.CreateFileQueryWithOptions(QueryOptionsFiles);
            uint fileCount = await query.GetItemCountAsync();
            return (int)fileCount;
        }

        /// <summary>
        /// Retrieves the StorgeFile at position n
        /// </summary>
        /// <param name="n"></param>
        /// <returns></returns>
        private async Task<StorageFile> GetStorageFile(int n)
        {
            FolderState fS = FolderStateStack.FolderStack.Peek();
            var storageFolder = fS.StorageFolder;
            var query = storageFolder.CreateFileQueryWithOptions(QueryOptions);
            var singleFileList = await query.GetFilesAsync((uint)n, 1);
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
        /// Get a "quick" count of folders in a folder
        /// </summary>
        /// <param name="storageFolder"></param>
        /// <returns></returns>
        /// <remarks>
        /// File system on XBox seems particularly slow at this...
        /// </remarks>
        private async Task<int> FoldersThisFolder(Windows.Storage.StorageFolder sF)
        {
            var query = sF.CreateFolderQueryWithOptions(QueryOptionsFolders);

            // GetFolderCountAsync is much faster than GetFoldersAsync()
            uint count = await query.GetItemCountAsync();
            return (int)count;
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
            var query = sF.CreateFileQueryWithOptions(QueryOptions);

            // Retrieve list of any media files
            //IReadOnlyList<StorageFile>? fileList = await query.GetFilesAsync();

            // Get list of all the folders in this folder.
            //IReadOnlyList<StorageFolder>? folderList = await sF.GetFoldersAsync();

            // Make a FolderState to push onto FoldersStack
            int numFiles = await FilesThisFolder(sF);      // This is slow on Xbox
            int numFolders = await FoldersThisFolder(sF);  // And so is this...
            FolderState folderState = new(sF, numFiles, numFolders);
            FolderStateStack.FolderStack.Push(folderState);

            Ready = true;
        }

        /// <summary>
        /// Attempt to brig in the saved state.
        /// </summary>
        /// <returns></returns>
        public async Task<Boolean> DeserializeState()
        {
            // Message that this, perhaps lengthly operation is underway
            if (OnXBox)
                FNameProgress.Report(SimpleSlide.Strings.FromLastTimeXBox);
            else 
                FNameProgress.Report(SimpleSlide.Strings.FromLastTime);

            return (Ready = await FolderStateStack.DeserializeState(QueryOptions));
                FNameProgress.Report(SimpleSlide.Strings.FromLastTime);

            return (Ready = await FolderStateStack.DeserializeState(QueryOptions));
        }
    }
}