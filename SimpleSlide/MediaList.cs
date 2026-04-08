using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.Search;

namespace SimpleSlide
{
    /// <summary>
    /// One of these is pushed on the Stack, for each folder being traversed
    /// </summary>
    internal class FolderState
    {
        public int LastFileNum { get; set; }
        public int LastFolderNum { get; set; }
        public IReadOnlyList<StorageFile> FileList { get; set; }        // Files in the folder
        public IReadOnlyList<StorageFolder> FolderList { get; set; }    // Folders in te folder
        public StorageFolder StorageFolder { get; set; }                // This folder
        public FolderState(StorageFolder storageFolder, IReadOnlyList<StorageFile> fileList, IReadOnlyList<StorageFolder> folderList)
        {
            StorageFolder = storageFolder;
            FileList = fileList;
            FolderList = folderList;
            LastFileNum = LastFolderNum = -1;
        }
    }

    /// <summary>
    /// Manages traversal of the media to be played
    /// </summary>
    internal class MediaList
    {
        public String? PickedFolderToken { get; set; }
        private Stack<FolderState> FoldersStack = new();
        private QueryOptions QueryOptions { get; set; }

        public MediaList()
        {

            // There is a bug in QueryOptios class that causes a cast exception when a List is passed
            // for the fie type filter list. There is mention of it in various forums. Only work-around
            // found is what is impemented here...
            var fileTypeFilterList = new List<String>() { ".jpeg", ".jpg", ".png", ".bmp", ".gif", ".tiff", ".ico", ".svg" };
            //var queryOptions = new QueryOptions(CommonFileQuery.OrderByName, fileTypeFilterList);
            QueryOptions = new QueryOptions();
            foreach (String fileType in fileTypeFilterList)
                QueryOptions.FileTypeFilter.Add(fileType);
        }

        public String CurrentFolderName()
        {
            if (0 == FoldersStack.Count)
                return new("");

            FolderState? fS = FoldersStack.Peek();
            StorageFolder sF = fS.StorageFolder;

            return (1+fS.LastFileNum).ToString() + ":" + fS.FileList.Count.ToString() + " " + sF.Name + "\\";
        }

        /// <summary>
        /// Get next media from list
        /// </summary>
        /// <returns></returns>
        public async Task<StorageFile?> GetNextMedia()
        {
            StorageFile? sF = await StackGetNext();
            return sF;
        }

        /// <summary>
        /// Get previous media from list
        /// </summary>
        /// <returns></returns>
        public async Task<StorageFile?> GetPreviousMedia()
        {
            StorageFile? sF = await StackGetPrevious();
            return sF;
        }

        /// <summary>
        /// Work the Stack/folde-tree to get next media
        /// </summary>
        /// <returns></returns>
        /// <remarks>
        /// This begs to be impemented via recursion, but does not fit well with
        /// other needs. So a loop and Stack are used.
        /// </remarks>
        private async Task<StorageFile?> StackGetNext()
        {
            if (0 == FoldersStack.Count) // JIC
                return null;

            FolderState? fS = FoldersStack.Peek();

            while (true)
            {
                // Nothing in the FolderStack. This case can also happen if the initial
                // folder has no media files and no subfolders.
                if (null == fS)
                    return null;

                // Have all files in folder hierarchy been processed?
                if (FoldersStack.Count == 1)
                    if (1+fS.LastFileNum >= fS.FileList.Count || 0 == fS.FileList.Count)
                        if (1+fS.LastFolderNum >= fS.FolderList.Count | 0 == fS.FolderList.Count)
                            fS.LastFileNum = fS.LastFolderNum = -1; // Go back to beginning

                // Any next files remaining in this folder?
                if (1 + fS.LastFileNum < fS.FileList.Count)
                    return fS.FileList[++fS.LastFileNum];

                // No more next files this folder, start working the folder list
                if (1 + fS.LastFolderNum < fS.FolderList.Count)
                {
                    // Onto next folder
                    await PrepForFolder(fS.FolderList[++fS.LastFolderNum]);
                    fS = FoldersStack.Peek();
                }
                else
                {
                    // No more folders, pop the stack
                    FoldersStack.Pop();
                    fS = (FoldersStack.Count > 0) ? FoldersStack.Peek() : null;
                }
            }
        }

        /// <summary>
        /// Work the Stack to get previous media
        /// </summary>
        /// <returns></returns>
        private async Task<StorageFile?> StackGetPrevious()
        {
            if (0 == FoldersStack.Count)  // JIC
                return null;

            FolderState? fS = FoldersStack.Peek();

            // Boundary case: at the beginning?
            if (1 == FoldersStack.Count && fS.LastFileNum <= 0)
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
                    fS = FoldersStack.Peek();
                }
                else
                {
                    // No more folders, pop the stack
                    FoldersStack.Pop();
                    fS = (FoldersStack.Count > 0) ? FoldersStack.Peek() : null;
                }
            }
        }

        /// <summary>
        /// Prepare to begin playing from a folder
        /// </summary>
        /// <param name="storageFolder">Pass in a StorageFolder is available. Otherwie will get it
        /// from the PickedFolderToken of the FutureAccessList.</param>
        /// <remarks>
        /// These types
        /// .jpeg, .jpg, .png, .bmp, gif, .tiff, .ico, .svg
        /// </remarks>
        public async Task PrepForFolder(Windows.Storage.StorageFolder? storageFolder = null)
        {
            if (null == storageFolder)
                storageFolder = (Windows.Storage.StorageFolder)await Windows.Storage.AccessCache.StorageApplicationPermissions.
                    FutureAccessList.GetItemAsync(PickedFolderToken);

            var query = storageFolder.CreateFileQueryWithOptions(QueryOptions);

            // Retrieve list of any media files
            IReadOnlyList<StorageFile>? fileList = await query.GetFilesAsync();

            // Get list of all the folders in this folder.
            IReadOnlyList<StorageFolder>? folderList = await storageFolder.GetFoldersAsync();

            // Make a FolderState to push onto FoldersStack
            FolderState folderState = new(storageFolder, fileList, folderList);
            FoldersStack.Push(folderState);
        }
    }
}
