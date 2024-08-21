using System.Collections.Generic;
using System.IO;

namespace UnityEngine.Rendering.EasyProbeVolume
{
    public static partial class BytesLoader
    {
                
#if UNITY_EDITOR || ENABLE_PROFILER
        static class ProfilerSampler
        {
            public static string s_BytesStreamRead = "BytesStreamRead";
            public static string s_FileStreamSeek = "FileStreamSeek";
            public static string s_FileStreamRead = "FileStreamRead";

            public static string s_MMFAccess = "MemoryMappedFileAccess";
        }
#endif
        
        public enum LoaderType
        {
            FileStream,
            MemoryMappedFile
        }

        private static LoaderType s_Type = LoaderType.FileStream;
        
        public static void SetLoaderType(LoaderType type)
        {
            if (s_Type == type)
            {
                return;
            }

            if (s_Type == LoaderType.FileStream)
            {
                DisposeFileStreams();
            }
            else
            {
                DisposeMemoryMappedFile();
            }

            s_Type = type;
        }
        
        
        public static bool LoadBytes(string filePath, ref byte[] buffer, int fileOffset, int bufferOffset, int length, out int bytes)
        {
            bytes = 0;
            if (!File.Exists(filePath))
            {
                return false;
            }
            
            if (s_Type == LoaderType.FileStream)
            {
                return ReadBytesFileStream(
                    filePath,
                    ref buffer,
                    fileOffset,
                    bufferOffset,
                    length,
                    ref bytes
                );
            }
            else
            {
                return ReadBytesMemoryMappedFile(
                    filePath,
                    ref buffer,
                    fileOffset,
                    bufferOffset,
                    length,
                    ref bytes
                );
            }
            
        }
        
        
        public static void Dispose()
        {
            DisposeFileStreams();
            DisposeMemoryMappedFile();
        }
    }
}