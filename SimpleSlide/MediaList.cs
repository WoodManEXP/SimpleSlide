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
        private QueryOptions QueryOptions { get; set; }
        private QueryOptions QueryOptionsFileCout { get; set; }
        private QueryOptions QueryOptionsFolderCout { get; set; }
        //private DataContractJsonSerializer DataContractJsonSerializer { get; set; }
        private Boolean EncounteredA_MediaFile { get; set; } = false;

        public MediaList(Progress<String> fNameProgress)
        {
            FNameProgress = fNameProgress;

            // Appears to be an issue in QueryOptios class causing a cast exception when a List is passed
            // for the fie type filter list. There is mention of it in various forums. Only work-around
            // found is what is impemented here...
            // var fileTypeFilterList = new List<String>() { ".jpeg", ".jpg", ".png", ".bmp", ".gif", ".tiff", ".ico", ".svg" };
            //var QueryOptions = new QueryOptions(CommonFileQuery.OrderByName, fileTypeFilterList);
            FileTypeFilterList = SimpleSlide.Strings.MediaTypes.Split(',').ToList();
            QueryOptions = new();
            QueryOptionsFileCout = new()
            {
                FolderDepth = FolderDepth.Shallow, // Just this folder
                IndexerOption = IndexerOption.UseIndexerWhenAvailable
            };
            QueryOptionsFolderCout = new(CommonFolderQuery.DefaultQuery)
            {
                FolderDepth = FolderDepth.Shallow, // Just this folder
                IndexerOption = IndexerOption.UseIndexerWhenAvailable
            };
            foreach (String fileType in FileTypeFilterList)
            {
                QueryOptions.FileTypeFilter.Add(fileType);
                QueryOptionsFileCout.FileTypeFilter.Add(fileType);
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
                StorageFolder? sF = fS.ThisStorageFolder;
                if (null != sF)
                {
                    if (prependNumber)
                        aStr = (1 + fS.LastFileNum).ToString() + ":" + fS.FileList.Count.ToString() + " " + sF.Name + "\\";
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

            FolderState? fS = FolderStateStack.FolderStack.Peek();

            while (true)
            {
                // Nothing in the FolderStack. This case can also happen if the initial
                // folder has no media files and no subfolders.
                if (null == fS)
                    return null;

                // Have all files in folder hierarchy been processed?
                // This is checking for having reached the end of the top-most folder. Having
                // done this means the op folder + all elow it have been traversed.
                if (FolderStateStack.FolderStack.Count == 1)
                    if (1 + fS.LastFileNum >= fS.FileList.Count || 0 == fS.FileList.Count)
                        if (1 + fS.LastFolderNum >= fS.FolderList.Count | 0 == fS.FolderList.Count)
                        {
                            fS.LastFileNum = fS.LastFolderNum = -1; // Go back to beginning

                            // Folder hierarchy and contents have been completel traversed with
                            // no playable media encoutered.
                            if (!EncounteredA_MediaFile)
                                throw new NoMediaException();
                        }

                // Any next files remaining in this folder?
                if (1 + fS.LastFileNum < fS.FileList.Count)
                    return fS.FileList[++fS.LastFileNum];

                // No more next files this folder, start working the folder list
                if (1 + fS.LastFolderNum < fS.FolderList.Count)
                {
                    // Onto next folder
                    await PrepForFolder(fS.FolderList[++fS.LastFolderNum]);
                    fS = FolderStateStack.FolderStack.Peek();
                }
                else
                {
                    // No more folders, pop the stack
                    FolderStateStack.FolderStack.Pop();
                    fS = (FolderStateStack.FolderStack.Count > 0) ? FolderStateStack.FolderStack.Peek() : null;
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

            FolderState? fS = FolderStateStack.FolderStack.Peek();

            // Boundary case: at the beginning?
            if (1 == FolderStateStack.FolderStack.Count && fS.LastFileNum <= 0)
                return null;

            while (true)
            {
                if (null == fS)
                    return null; // Nothing in the FolderStack

                // Any previous files remaining in this folder?
                if (fS.FileList.Count != 0 && fS.LastFileNum - 1 >= 0)
                    return fS.FileList[--fS.LastFileNum];

                // No more previous files this folder, start working the folder list
                if (fS.LastFolderNum - 1 > fS.FolderList.Count)
                {
                    // Onto next folder
                    await PrepForFolder(fS.FolderList[fS.LastFolderNum]);
                    fS = FolderStateStack.FolderStack.Peek();
                }
                else
                {
                    // No more folders, pop the stack
                    FolderStateStack.FolderStack.Pop();
                    fS = (FolderStateStack.FolderStack.Count > 0) ? FolderStateStack.FolderStack.Peek() : null;
                    if (null != fS)
                        fS.LastFolderNum = -1;  // Popping back to a parent folder. Prep to possibly start again
                                                // with its first child folder. 
                }
            }
        }

        /// <summary>
        /// Get a "quick" count of files in a folder
        /// </summary>
        /// <param name="storageFolder"></param>
        /// <returns></returns>
        /// <remarks>
        /// File system on XBox seems particularly slow at this...
        /// </remarks>
        private async Task<uint> FilesThisFolder(Windows.Storage.StorageFolder? sF)
        {
            var query = sF.CreateFileQueryWithOptions(QueryOptionsFileCout);
            uint fileCount = await query.GetItemCountAsync();
            return fileCount;
        }

        /// <summary>
        /// Get a "quick" count of folders in a folder
        /// </summary>
        /// <param name="storageFolder"></param>
        /// <returns></returns>
        /// <remarks>
        /// File system on XBox seems particularly slow at this...
        /// </remarks>
        private async Task<uint> FoldersThisFolder(Windows.Storage.StorageFolder? sF)
        {
            var query = sF.CreateFolderQueryWithOptions(QueryOptionsFolderCout);

            // GetFolderCountAsync is much faster than GetFoldersAsync()
            uint count = await query.GetItemCountAsync();
            return count;
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
            Boolean onXbox = Windows.System.Profile.AnalyticsInfo.VersionInfo.DeviceFamily == "Windows.Xbox";

            // XBox has especially slow file system. If a large number of files/folers needs to be
            // gathered for this sF place a Hang on... message in the status area.
            if (onXbox)
            {
                uint numFiles = await FilesThisFolder(sF);
                uint numFolders = await FoldersThisFolder(sF);
                uint tN;
                if ((tN = numFiles + numFolders) > 100)
                    FNameProgress.Report(SimpleSlide.Strings.FolderPrepXBox.Replace("_N", tN.ToString()));
            }

            var query = sF.CreateFileQueryWithOptions(QueryOptions);

            // Retrieve list of any media files
            IReadOnlyList<StorageFile>? fileList = await query.GetFilesAsync();

            // Get list of all the folders in this folder.
            IReadOnlyList<StorageFolder>? folderList = await sF.GetFoldersAsync();

            // Make a FolderState to push onto FoldersStack
            FolderState folderState = new(sF, fileList, folderList);
            FolderStateStack.FolderStack.Push(folderState);

            Ready = true;
        }

        /// <summary>
        /// Attemot to brig in the saved state.
        /// </summary>
        /// <returns></returns>
        public async Task<Boolean> DeserializeState()
        {
            return (Ready = await FolderStateStack.DeserializeState(QueryOptions));
        }
    }
}