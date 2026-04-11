using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.Search;

namespace SimpleSlide
{
    /// <summary>
    /// One of these is pushed on the Stack, for each folder being traversed
    /// </summary>
    /// 
    [DataContract]
    internal class FolderState
    {
        [DataMember] public int LastFileNum { get; set; }
        [DataMember] public int LastFolderNum { get; set; }
        [DataMember] public String? Path { get; set; }                  // Full path to this folder
        public IReadOnlyList<StorageFile> FileList { get; set; }        // Files in the folder
        public IReadOnlyList<StorageFolder> FolderList { get; set; }    // Folders in te folder
        public StorageFolder? ThisStorageFolder { get; set; }           // This folder
        public FolderState(StorageFolder storageFolder, IReadOnlyList<StorageFile> fileList, IReadOnlyList<StorageFolder> folderList)
        {
            ThisStorageFolder = storageFolder;
            Path = storageFolder.Path; // Hang on to this for deserialization reconstruction
            FileList = fileList;
            FolderList = folderList;
            LastFileNum = LastFolderNum = -1;
        }
        /// <summary>
        /// Construtor for deserializier
        /// </summary>
        public FolderState()
        {

            // Coming in from deserialization
            // Need to construct FileList and FolderList

        }
    }

    [DataContract]
    internal class FolderStateStack
    {
        [DataMember] public Stack<FolderState> FoldersStack { get; set; } = new();
        public FolderStateStack() { }
    }

    /// <summary>
    /// Manages traversal of the media to be played
    /// </summary>
    internal class MediaList
    {
        public String? PickedFolderToken { get; set; }

        private readonly FolderStateStack FolderStateStack = new();
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
            if (0 == FolderStateStack.FoldersStack.Count)
                return new("");

            FolderState? fS = FolderStateStack.FoldersStack.Peek();
            StorageFolder sF = fS.ThisStorageFolder;

            return (1 + fS.LastFileNum).ToString() + ":" + fS.FileList.Count.ToString() + " " + sF.Name + "\\";
        }

        /// <summary>
        /// Get next media from list
        /// </summary>
        /// <returns></returns>
        public async Task<StorageFile?> GetNextMedia()
        {
            StorageFile? sF = await StackGetNext();

            await PersistState(); // place in folder hierachy has changed, remember.

            return sF;
        }

        /// <summary>
        /// Get previous media from list
        /// </summary>
        /// <returns></returns>
        public async Task<StorageFile?> GetPreviousMedia()
        {
            StorageFile? sF = await StackGetPrevious();

            await PersistState(); // place in folder hierachy has changed, remember.

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
            if (0 == FolderStateStack.FoldersStack.Count) // JIC
                return null;

            FolderState? fS = FolderStateStack.FoldersStack.Peek();

            while (true)
            {
                // Nothing in the FolderStack. This case can also happen if the initial
                // folder has no media files and no subfolders.
                if (null == fS)
                    return null;

                // Have all files in folder hierarchy been processed?
                if (FolderStateStack.FoldersStack.Count == 1)
                    if (1 + fS.LastFileNum >= fS.FileList.Count || 0 == fS.FileList.Count)
                        if (1 + fS.LastFolderNum >= fS.FolderList.Count | 0 == fS.FolderList.Count)
                            fS.LastFileNum = fS.LastFolderNum = -1; // Go back to beginning

                // Any next files remaining in this folder?
                if (1 + fS.LastFileNum < fS.FileList.Count)
                    return fS.FileList[++fS.LastFileNum];

                // No more next files this folder, start working the folder list
                if (1 + fS.LastFolderNum < fS.FolderList.Count)
                {
                    // Onto next folder
                    await PrepForFolder(fS.FolderList[++fS.LastFolderNum]);
                    fS = FolderStateStack.FoldersStack.Peek();
                }
                else
                {
                    // No more folders, pop the stack
                    FolderStateStack.FoldersStack.Pop();
                    fS = (FolderStateStack.FoldersStack.Count > 0) ? FolderStateStack.FoldersStack.Peek() : null;
                }
            }
        }

        /// <summary>
        /// Work the Stack to get previous media
        /// </summary>
        /// <returns></returns>
        private async Task<StorageFile?> StackGetPrevious()
        {
            if (0 == FolderStateStack.FoldersStack.Count)  // JIC
                return null;

            FolderState fS = FolderStateStack.FoldersStack.Peek();

            // Boundary case: at the beginning?
            if (1 == FolderStateStack.FoldersStack.Count && fS.LastFileNum <= 0)
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
                    fS = FolderStateStack.FoldersStack.Peek();
                }
                else
                {
                    // No more folders, pop the stack
                    FolderStateStack.FoldersStack.Pop();
                    fS = (FolderStateStack.FoldersStack.Count > 0) ? FolderStateStack.FoldersStack.Peek() : null;
                    if (null != fS)
                        fS.LastFolderNum = -1;  // Popping back to a parent folder. Prep to possibly start again
                                                // with its first child folder. 
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
        // Unsure what these mean, but C# compiler recommends...
        //[RequiresDynamicCode("Calls SimpleSlide.MediaList.PersistState()")]
        //[RequiresUnreferencedCode("Calls SimpleSlide.MediaList.PersistState()")]
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
            FolderStateStack.FoldersStack.Push(folderState);

            //await PersistState(); // place in folder hierachy has changed, remember.
        }

        /// <summary>
        /// Persist current state to a file, via JSON serialization
        /// </summary>
        /// <returns></returns>
        /// <remarks>
        /// This operation enables the system to contiue where it left off from the previous run.
        /// </remarks>
        private Boolean PersistingState { get; set; } = false;
        private DataContractJsonSerializer? DataContractJsonSerializer { get; set; } = null;
        private MemoryStream PersistMemoryStream { get; set; } = new();

        // Unsure what these mean, but C# compiler recommends...
        [RequiresUnreferencedCode("Calls System.Runtime.Serialization.Json.DataContractJsonSerializer.DataContractJsonSerializer(Type)")]
        [RequiresDynamicCode("Calls System.Runtime.Serialization.Json.DataContractJsonSerializer.DataContractJsonSerializer(Type)")]
        private async Task PersistState()
        {

            if (null == DataContractJsonSerializer)
            {
                //var typeList = new List<Type>();
                //typeList.Add(typeof(StorageFile));     // StorageFile difficult to serialize
                //typeList.Add(typeof(StorageFolder));   // StorageFolder difficult to serialize
                DataContractJsonSerializer = new(typeof(FolderStateStack)/*, typeList*/);
            }

            // Do not want to re-enter this if it might stil be running from a previous invocation.
            if (PersistingState)
                return;
            PersistingState = true;
            
            var storageFolder = Windows.Storage.ApplicationData.Current.LocalFolder;
            var file = await storageFolder.CreateFileAsync("SimpleSlide.json",
                CreationCollisionOption.ReplaceExisting);

            try
            {
                PersistMemoryStream.Position = 0; // Reset position to start
                PersistMemoryStream.SetLength(0); // Clear existing content/length
                //PersistMemoryStream.Seek(0, SeekOrigin.Begin); 
                DataContractJsonSerializer.WriteObject(PersistMemoryStream, FolderStateStack);

                PersistMemoryStream.Position = 0; // Reset position to the start
//                using (var reader = new StreamReader(PersistMemoryStream))
//                {
//                    string text = reader.ReadToEnd();
//                }

                var fileStream = await file.OpenStreamForWriteAsync();

                // Asynchronously copy data from MemoryStream to the StorageFile stream
                PersistMemoryStream.Position = 0;
                await PersistMemoryStream.CopyToAsync(fileStream);
                // Ensure all data is written to the disk
                await fileStream.FlushAsync();
                fileStream.Close();
            }
            catch (Exception e)
            {
                Debug.WriteLine("PersistState exception " + e.Message);
            }
            PersistingState = false;
        }
    }
}
