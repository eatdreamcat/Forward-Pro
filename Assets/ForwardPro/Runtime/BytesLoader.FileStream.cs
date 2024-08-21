using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine.Profiling;

namespace UnityEngine.Rendering.EasyProbeVolume
{
    public static partial class BytesLoader
    {
        struct FileStreamKey
        {
            public FileAccess access;
            public FileMode mode;
            public string path;
        }
        
        private static Dictionary<FileStreamKey, FileStream> s_FileStreamMap = new();
        
        static FileStream GetFileStream(string path, FileMode mode, FileAccess access)
        {
            var key = new FileStreamKey()
            {
                path = path,
                mode = mode,
                access = access
            };

            if (!s_FileStreamMap.TryGetValue(key, out var fileStream))
            {
                fileStream = new FileStream(path, mode, access);
                s_FileStreamMap.Add(key, fileStream);
            }
            
            fileStream.Flush();
            return fileStream;
        }
        
        static bool ReadBytesFileStream(string filePath, ref byte[] buffer, int bufferOffset, long fileOffset,
            int length, ref int readBytes)
        {
            var fileStream = GetFileStream(filePath, FileMode.Open, FileAccess.Read);
            return ReadBytesFromFileStream(
                fileStream,
                ref buffer,
                bufferOffset,
                fileOffset,
                length,
                ref readBytes
            );;
        }

        static bool ReadBytesFromFileStream(FileStream fs, ref byte[] buffer, int bufferOffset, 
            long fileOffset, int length, ref int  readBytes)
        {
            readBytes = 0;
            
            if (length <= 0)
            {
                Debug.LogError("[EasyProbeStreaming](ReadBytesFromRelativePath): bytes to read is zero.");
                return false;
            }
            
           
            if (buffer.Length - bufferOffset < length)
            {
                Debug.LogError("[EasyProbeStreaming](ReadBytesFromRelativePath): buffer is lack of size.");
                return false;
            }
            
            try
            {
#if UNITY_EDITOR || ENABLE_PROFILER
                Profiler.BeginSample(ProfilerSampler.s_BytesStreamRead);
#endif
                
#if UNITY_EDITOR || ENABLE_PROFILER
                Profiler.BeginSample(ProfilerSampler.s_FileStreamSeek);
#endif
                fs.Seek(fileOffset, SeekOrigin.Begin);
                
#if UNITY_EDITOR || ENABLE_PROFILER
                Profiler.EndSample();
#endif
                
#if UNITY_EDITOR || ENABLE_PROFILER
                Profiler.BeginSample(ProfilerSampler.s_FileStreamRead);
#endif
                readBytes = fs.Read(buffer, bufferOffset, length);
                
#if UNITY_EDITOR || ENABLE_PROFILER
                Profiler.EndSample();
#endif
#if UNITY_EDITOR || ENABLE_PROFILER
                Profiler.EndSample();
#endif
                
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError("[EasyProbeStreaming](ReadByteFromRelativePath):" + e.Message);
            }

            return false;
        }

        static void DisposeFileStreams()
        {
            foreach (var keyValue in s_FileStreamMap)
            {
                if (keyValue.Value != null)
                {
                    keyValue.Value.Dispose();
                }
            }
            
            s_FileStreamMap.Clear();
        }
    }
}