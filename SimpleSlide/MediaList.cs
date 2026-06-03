using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.Search;

namespace SimpleSlide
{
    [DataContract]
    internal class FolderState
    {
        /// <summary>
        /// One of these is pushed on the Stack, for each folder being traversed
        /// </summary>
        /// 
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
    }

    [DataContract]
    internal class FolderStateStack
    {
        [DataMember] public Stack<FolderState>? FoldersStack { get; set; }
        public FolderStateStack()
        {
            FoldersStack = new();
        }

        /// <summary>
        /// Back to initial state
        /// </summary>
        public void Clear()
        {
            FoldersStack.Clear();
        }

        /// <summary>
        /// Finish read from persistant store operation.
        /// Construct the FileList and FolderList for each FolderState on the stack.
        /// </summary>
        /// <returns></returns>
        /// <remarks>
        /// It'd be more ituitive to have accomplished this in the FolderState parameterless constructor 
        /// the deserializier would call. But constructors are unable to utilize async methods...
        /// When this is entered a FoldersStack of FolderState items has been created by the Serializer.
        /// However, Serializer cannot build FileList and FolderList members, so they are built here.
        /// </remarks>
        public async Task<Boolean> FinishDeserialization(QueryOptions QueryOptions)
        {
            Boolean allOK = false;

            if (null == FoldersStack) // Just to be sure
            {
                FoldersStack = new();
                allOK = true;
            }
            else
                foreach (var folderState in FoldersStack)
                {
                    allOK = false;
                    try
                    {
                        folderState.ThisStorageFolder = await StorageFolder.GetFolderFromPathAsync(folderState.Path);
                        var query = folderState.ThisStorageFolder.CreateFileQueryWithOptions(QueryOptions);

                        // Retrieve list of any media files
                        folderState.FileList = await query.GetFilesAsync();

                        // Get list of all the folders in this folder.
                        folderState.FolderList = await folderState.ThisStorageFolder.GetFoldersAsync();
                        allOK = true;
                    }
                    catch (Exception e) // FileNotFoundException, UnauthorizedAccessException
                    {
                        Debug.WriteLine("FolderStateStack:CompleteTheRead exception " + e.Message);
                        allOK = false;
                    }

                    if (!allOK)
                        break;
                }

            if (!allOK)                 // Something during deserialization has failed. Clear FolderStack
                FoldersStack.Clear();

            return allOK;
        }
    }


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
        private DataContractJsonSerializer DataContractJsonSerializer { get; set; }
        private Boolean EncounteredA_MediaFile { get; set; } = false;

        [RequiresUnreferencedCode("Calls System.Runtime.Serialization.Json.DataContractJsonSerializer.DataContractJsonSerializer(Type)")]
        [UnconditionalSuppressMessage("AOT", "IL3050:Calling members annotated with 'RequiresDynamicCodeAttribute' may break functionality when AOT compiling.", Justification = "<Pending>")]
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

            DataContractJsonSerializer = new(typeof(FolderStateStack)/*, typeList*/);
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
            if (0 == FolderStateStack.FoldersStack.Count)
                return new("");

            String aStr;

            FolderState fS = FolderStateStack.FoldersStack.Peek();
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
        [RequiresUnreferencedCode("Calls SimpleSlide.MediaList.WritePersistentState()")]
        [RequiresDynamicCode("Calls SimpleSlide.MediaList.WritePersistentState()")]
        public async Task<StorageFile?> GetNextMedia()
        {
            StorageFile? sF;
            try
            {
                sF = await StackGetNext();

                await SerializeState(); // place in folder hierachy has changed, remember.

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
        [RequiresUnreferencedCode("Calls SimpleSlide.MediaList.SerializeState()")]
        [RequiresDynamicCode("Calls SimpleSlide.MediaList.SerializeState()")]
        public async Task<StorageFile?> GetPreviousMedia()
        {
            StorageFile? sF = await StackGetPrevious();

            await SerializeState(); // place in folder hierachy has changed, remember.

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
                // This is checking for having reached the end of the top-most folder. Having
                // done this means the op folder + all elow it have been traversed.
                if (FolderStateStack.FoldersStack.Count == 1)
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

            FolderState? fS = FolderStateStack.FoldersStack.Peek();

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
        /// Get a "quick" count of files in a folder
        /// </summary>
        /// <param name="storageFolder"></param>
        /// <returns></returns>
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
        // Unsure what these mean, but C# compiler recommends...
        //[RequiresDynamicCode("Calls SimpleSlide.MediaList.PersistState()")]
        //[RequiresUnreferencedCode("Calls SimpleSlide.MediaList.PersistState()")]
        public async Task PrepForFolder(Windows.Storage.StorageFolder? sF)
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
            FolderStateStack.FoldersStack.Push(folderState);

            Ready = true;
        }

        /// <summary>
        /// Persist current state to a file, via JSON serialization
        /// </summary>
        /// <returns></returns>
        /// <remarks>
        /// This operation enables the system to contiue where it left off from the previous run.
        /// </remarks>
        private Boolean PersistingState { get; set; } = false;
        private MemoryStream PersistMemoryStream { get; set; } = new();

        private readonly String PersistentFname = new("SimpleSlide.json");

        // Unsure what these mean, but C# compiler recommends...
        [RequiresUnreferencedCode("Calls System.Runtime.Serialization.Json.DataContractJsonSerializer.DataContractJsonSerializer(Type)")]
        [RequiresDynamicCode("Calls System.Runtime.Serialization.Json.DataContractJsonSerializer.DataContractJsonSerializer(Type)")]
        private async Task SerializeState()
        {

            // Do not want to re-enter this if it might stil be running from a previous invocation.
            if (PersistingState)
                return;
            PersistingState = true;

            var storageFolder = Windows.Storage.ApplicationData.Current.LocalFolder;
            var file = await storageFolder.CreateFileAsync(PersistentFname,
                CreationCollisionOption.ReplaceExisting);

            try
            {
                PersistMemoryStream.Position = 0; // Reset position to start
                PersistMemoryStream.SetLength(0); // Clear existing content/length
                DataContractJsonSerializer.WriteObject(PersistMemoryStream, FolderStateStack);

                var fileStream = await file.OpenStreamForWriteAsync();

                // Asynchronously copy data from MemoryStream to the StorageFile stream
                PersistMemoryStream.Position = 0; // Reset position to the start for the write
                await PersistMemoryStream.CopyToAsync(fileStream);
                // Ensure all data is written to the disk
                await fileStream.FlushAsync();
                fileStream.Close();
            }
            catch (Exception e)
            {
                Debug.WriteLine("WritePersistentState exception " + e.Message);
            }
            PersistingState = false;
        }

        /// <summary>
        /// Check for availability of persistent state, if available use it to
        /// instantiate the FolderStateStack. 
        /// </summary>
        /// <returns>true is success, false otherwise/returns>
        /// <remarks>
        /// It'd be desirable to calll this from the MediaList class constructor, but making async
        /// calls from a constructor is difficult, at best.
        /// https://hackernoon.com/asynchronous-initialization-in-c-overcoming-constructor-limitations
        /// </remarks>
        // Unsure what these mean, but C# compiler recommends...
        [RequiresUnreferencedCode("Calls System.Runtime.Serialization.Json.DataContractJsonSerializer.DataContractJsonSerializer(Type)")]
        [RequiresDynamicCode("Calls System.Runtime.Serialization.Json.DataContractJsonSerializer.DataContractJsonSerializer(Type)")]
        public async Task<Boolean> DeserializeState()
        {
            try
            {
                // Path = "C:\Users\Robert\AppData\Local\Packages\WoodManEXP.SimpleSlide_8nk81dv7p0dfm\LocalState"
                var storageFolder = Windows.Storage.ApplicationData.Current.LocalFolder;
                var file = await storageFolder.GetFileAsync(PersistentFname);

                var fileStream = await file.OpenStreamForReadAsync();
                fileStream.Position = 0;

                FolderStateStack = (FolderStateStack)DataContractJsonSerializer.ReadObject(fileStream);

                // This can fail if the last run's files are no longer available.
                // Eg. a USB device no loner connecteed or a folder renamed.
                return (Ready = await FolderStateStack.FinishDeserialization(QueryOptions));
            }
            catch (Exception e) // FileNotFoundException, UnauthorizedAccessException
            {
                Debug.WriteLine("ReadPersistentState exception " + e.Message);
            }
            return (Ready = false);
        }
    }
}