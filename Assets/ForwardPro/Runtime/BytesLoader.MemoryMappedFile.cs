using System.Collections.Generic;
using System.IO;
using System.IO.MemoryMappedFiles;
using UnityEngine.Profiling;

namespace UnityEngine.Rendering.EasyProbeVolume
{
    public static partial class BytesLoader
    {
        private static Dictionary<string, MemoryMappedFileHandle> s_MemoryMappedFiles = new();

        struct MemoryMappedFileHandle
        {
            public MemoryMappedFile mmf;
            public MemoryMappedViewAccessor accessor;
        }
        static MemoryMappedFileHandle GetMemoryMappedFile(string path)
        {
            if (!s_MemoryMappedFiles.TryGetValue(path, out var mmfHandle))
            {
                var fileInfo = new FileInfo(path);
                var mmf = MemoryMappedFile.CreateFromFile(path);
                var accessor = mmf.CreateViewAccessor(0, fileInfo.Length, MemoryMappedFileAccess.Read);
                mmfHandle = new MemoryMappedFileHandle()
                {
                    mmf = mmf,
                    accessor = accessor
                };
                s_MemoryMappedFiles.Add(path, mmfHandle);
            }
            
            mmfHandle.accessor.Flush();
            
            return mmfHandle;
        }
        
        static bool ReadBytesMemoryMappedFile(string filePath, ref byte[] buffer, int bufferOffset, long fileOffset,
            int length, ref int readBytes)
        {
            var mmfHandle = GetMemoryMappedFile(filePath);

#if UNITY_EDITOR || ENABLE_PROFILER
            Profiler.BeginSample(ProfilerSampler.s_MMFAccess);
#endif
            
            readBytes = mmfHandle.accessor.ReadArray(fileOffset, buffer, bufferOffset, length);
            
#if UNITY_EDITOR || ENABLE_PROFILER
            Profiler.EndSample();
#endif
            
            return true;
        }

        static void DisposeMemoryMappedFile()
        {
            foreach (var kv in s_MemoryMappedFiles)
            {
                kv.Value.accessor.Dispose();
                kv.Value.mmf.Dispose();
            }
            
            s_MemoryMappedFiles.Clear();
        }
    }
}