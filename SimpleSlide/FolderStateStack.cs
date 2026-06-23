using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.Search;

namespace SimpleSlide
{
    internal class FolderState
    {
        /// <summary>
        /// One of these is pushed on the Stack, for each folder being traversed
        /// </summary>
        /// 
        [JsonInclude] public int LastFileNum { get; set; } = -1;
        [JsonInclude] public int LastFolderNum { get; set; } = -1;
        [JsonInclude] public String? Path { get; set; }                             // Full path to this folder
        [JsonIgnore] public StorageFolder? StorageFolder { get; set; }          // This folder
        [JsonIgnore] public StorageFileQueryResult StorageFileQuery { get; internal set; }
        [JsonIgnore] public StorageFolderQueryResult StorageFolderQuery { get; internal set; }
        [JsonIgnore] public int FileCount { get; set; } = 0;
        [JsonIgnore] public int FolderCount { get; set; } = 0;

        /// <summary>
        /// For deserialization
        /// </summary>
        public FolderState()
        {

        }
        public FolderState(StorageFolder storageFolder, QueryOptions queryOptionsFiles, QueryOptions queryOptionsFolders)
        {
            StorageFolder = storageFolder;
            StorageFileQuery = storageFolder.CreateFileQueryWithOptions(queryOptionsFiles);
            StorageFolderQuery = storageFolder.CreateFolderQueryWithOptions(queryOptionsFolders);
            Path = storageFolder.Path; // Hang on to this for deserialization reconstruction
        }

        public async Task PostCtor()
        {
            FileCount = (int)await StorageFileQuery.GetItemCountAsync();
            FolderCount = (int)await StorageFolderQuery.GetItemCountAsync();
        }
    }

    /// <summary>
    /// Make a sub-class of Stack, to aid in JSON (De)Serialization
    /// </summary>
    /// <typeparam name="T"></typeparam>
    internal class FolderStack<T> : Stack<T>
    {
        public FolderStack()
        {
        }

        /// <summary>
        /// Construct one of these from another
        /// </summary>
        /// <param name="st"></param>
        public FolderStack(FolderStack<T> st) : base(st)
        {

        }
    }

    // For non-reflection, JSON (De)Serialization
    [JsonSourceGenerationOptions(WriteIndented = true)]
    [JsonSerializable(typeof(FolderStack<FolderState>))]
    [JsonSerializable(typeof(FolderState))]
    internal partial class SourceGenerationContext : JsonSerializerContext { }

    internal class FolderStateStack
    {
        public FolderStack<FolderState> FolderStack { get; set; }
        private Boolean PersistingState { get; set; } = false;
        public FolderStateStack()
        {
            FolderStack = new();
        }

        /// <summary>
        /// Back to initial state
        /// </summary>
        public void Clear()
        {
            FolderStack.Clear();
        }

        /// <summary>
        /// Persist current state to a file, via JSON serialization
        /// </summary>
        /// <returns></returns>
        /// <remarks>
        /// This operation enables the system to contiue where it left off from the previous run.
        /// </remarks>
        public async Task SerializeState()
        {

            // Do not want to re-enter this if it might stil be running from a previous invocation.
            if (PersistingState)
                return;
            PersistingState = true;

            var storageFolder = Windows.Storage.ApplicationData.Current.LocalFolder;
            var file = await storageFolder.CreateFileAsync(SimpleSlide.Strings.PersistentFname,
                CreationCollisionOption.ReplaceExisting);

            try
            {
                String jsonStr = JsonSerializer.Serialize(FolderStack, SourceGenerationContext.Default.FolderStackFolderState);

                var fileStream = await file.OpenStreamForWriteAsync();

                // using flushes and releases the file resorces when writer is collected
                using var writer = new StreamWriter(fileStream);
                writer.Write(jsonStr);
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
        public async Task<Boolean> DeserializeState(QueryOptions queryOptionsFiles, QueryOptions queryOptionsFolders)
        {
            try
            {
                // Path = "C:\Users\Robert\AppData\Local\Packages\WoodManEXP.SimpleSlide_8nk81dv7p0dfm\LocalState"
                var storageFolder = Windows.Storage.ApplicationData.Current.LocalFolder;
                var file = await storageFolder.GetFileAsync(SimpleSlide.Strings.PersistentFname);

                var fileStream = await file.OpenStreamForReadAsync();
                fileStream.Position = 0;

                var readIn = FolderStack;

                readIn = JsonSerializer.Deserialize(fileStream, SourceGenerationContext.Default.FolderStackFolderState);

                // Stack comes in reversed from JSON. This construct reverses it
                FolderStack = new(readIn);

                // This can fail if the last run's files are no longer available.
                // Eg. a USB device no loner connecteed or a folder renamed.
                return await FinishDeserialization(queryOptionsFiles, queryOptionsFolders);
            }
            catch (Exception e) // FileNotFoundException, UnauthorizedAccessException
            {
                Debug.WriteLine("ReadPersistentState exception " + e.Message);
            }
            return false;
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
        public async Task<Boolean> FinishDeserialization(QueryOptions queryOptionsFiles, QueryOptions queryOptionsFolders)
        {
            Boolean allOK = false;

            if (null == FolderStack) // Just to be sure
            {
                FolderStack = new();
                allOK = true;
            }
            else
                // Traverse each FolderState element coming in from deserialization initilizing
                // elements that were/are not serialized.
                foreach (var folderState in FolderStack)
                {
                    allOK = false;
                    try
                    {
                        folderState.StorageFolder = await StorageFolder.GetFolderFromPathAsync(folderState.Path);
                        folderState.StorageFileQuery = folderState.StorageFolder.CreateFileQueryWithOptions(queryOptionsFiles);
                        folderState.StorageFolderQuery = folderState.StorageFolder.CreateFolderQueryWithOptions(queryOptionsFolders);

                        // Get count of files
                        folderState.FileCount = (int)await folderState.StorageFileQuery.GetItemCountAsync();

                        // Get count of folders
                        folderState.FolderCount = (int)await folderState.StorageFolderQuery.GetItemCountAsync();

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
                FolderStack.Clear();

            return allOK;
        }

    }
}
