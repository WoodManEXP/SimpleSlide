using System;
using System.Collections.Generic;
using System.Text;
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
        public int CurrentFolderNum { get; set; } = 0;
        public IReadOnlyList<StorageFolder>? FolderList { get; set; }
        public FolderState() { }
    }

    /// <summary>
    /// Manages traversal of the media to be played
    /// </summary>
    internal class MediaList
    {

        public String? PickedFolderToken { get; set; }

        private Stack<FolderState> FoldersStack = new();
        private IReadOnlyList<StorageFile>? CurrentFolderFileList { get; set; } = null; // All the files in a folder
        private int NextFileNum;

        public MediaList() { }

        private int LastImageNumThisFolder { get; set; } = -1;

        /// <summary>
        /// Get next media from list
        /// </summary>
        /// <returns></returns>
        public StorageFile? GetNextMedia()
        {
            if (null == CurrentFolderFileList)
                return null;

            // List is ready/available
            LastImageNumThisFolder++;

            if (LastImageNumThisFolder >= CurrentFolderFileList.Count) // Round and round
                LastImageNumThisFolder = 0;

            return CurrentFolderFileList[LastImageNumThisFolder];
        }

        public StorageFile? GetPreviousMedia()
        {
            if (null == CurrentFolderFileList)
                return null;

            // List is ready/available

            if (--LastImageNumThisFolder < 0) // Round and round
                LastImageNumThisFolder = 0;
            return CurrentFolderFileList[LastImageNumThisFolder];
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
        public async Task PrepForFolder(Windows.Storage.StorageFolder? storageFolder = null)
        {
            var folder = (Windows.Storage.StorageFolder)await Windows.Storage.AccessCache.StorageApplicationPermissions.
                FutureAccessList.GetItemAsync(PickedFolderToken);

            // Retrieve list of any media files
            // There is a bug in QueryOptios class that causes a cast exception when a List is passed
            // for the fie type filter list. There is mention of it in various forums. Only work-around
            // found is what is impemented here...
            var fileTypeFilterList = new List<String>() { ".jpeg", ".jpg", ".png", ".bmp", ".gif", ".tiff", ".ico", ".svg" };
            //var queryOptions = new QueryOptions(CommonFileQuery.OrderByName, fileTypeFilterList);
            var queryOptions = new QueryOptions();
            foreach (String fileType in fileTypeFilterList)
                queryOptions.FileTypeFilter.Add(fileType);
            var query = folder.CreateFileQueryWithOptions(queryOptions);
            CurrentFolderFileList = null;
            CurrentFolderFileList = await query.GetFilesAsync();   // Files in current folder

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

            LastImageNumThisFolder = 0;
        }
    }
}
