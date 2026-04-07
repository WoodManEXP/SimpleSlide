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
        public int LastImageNum { get; set; } = -1;
        public int LastFolderNum { get; set; } = 0;
        public IReadOnlyList<StorageFile> FileList { get; set; }        // Files in the folder
        public IReadOnlyList<StorageFolder> FolderList { get; set; }    // Folders in te folder
        public FolderState(IReadOnlyList<StorageFile> fileList, IReadOnlyList<StorageFolder> folderList)
        {
            FileList = fileList;
            FolderList = folderList;
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
        /// <summary>
        /// Get next media from list
        /// </summary>
        /// <returns></returns>
        public StorageFile? GetNextMedia()
        {
            FolderState fS = FoldersStack.Peek();

            if (null == fS)
                return null; // Nothing in the FolderStack

            // List is ready/available
            fS.LastImageNum++;

            if (fS.LastImageNum >= fS.FileList.Count) // Round and round
                fS.LastImageNum = 0;

            return fS.FileList[fS.LastImageNum];
        }
        public StorageFile? GetPreviousMedia()
        {
            FolderState fS = FoldersStack.Peek();

            if (null == fS)
                return null; // Nothing in the FolderStack

            // List is ready/available

            if (--fS.LastImageNum < 0) // Round and round
                fS.LastImageNum = 0;
            return fS.FileList[fS.LastImageNum];
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
            else
            {
            
            }

            var query = storageFolder.CreateFileQueryWithOptions(QueryOptions);

            // Retrieve list of any media files
            IReadOnlyList<StorageFile>? fileList = await query.GetFilesAsync();

            // Get list of all the folders in this folder.
            IReadOnlyList<StorageFolder>? folderList = await storageFolder.GetFoldersAsync();

            // Make a FolderState to push onto FoldersStack
            FolderState folderState = new(fileList, folderList);
            FoldersStack.Push(folderState);
        }
    }
}
