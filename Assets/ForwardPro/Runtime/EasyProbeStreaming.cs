
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.IO.LowLevel.Unsafe;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Profiling;
using UnityEngine.Rendering.Universal;

namespace UnityEngine.Rendering.EasyProbeVolume
{
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct EasyProbeMetaData
    {
        public Vector3Int cellMin;
        public int probeSpacing;
        public Vector3Int cellMax;
        public int cellSize;
        public Vector3Int probeCountPerVolumeAxis;
        public int probeCountPerCellAxis;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct EasyCellData
    {
        // xyz: cell world position, w: probe count per slice
        public Vector4 position;
        // xyz: volume index, w: flatten index
        public Vector4 cellIndex;
        // // x:l0l1 x start, y: l0l1 y start, z: l0l1 x end, w: l0l1 y end,
        // public Vector4 probeDataPerSliceLayoutL0L1;
        // public Vector4 probeDataPerSliceLayoutL2;
        // // xy: L0L1 start,end; zw: L1 start,end
        // public Vector4 probeDataSliceLayout;
    }

    public static class EasyProbeStreaming 
    {
#if UNITY_EDITOR || ENABLE_PROFILER
        static class ProfilerSampler
        {
            public static string s_DoStreaming = "DoStreaming";
            public static string s_TextureUploadCPU = "TextureUploadCPU";
            public static string s_BytesStreamRead = "BytesStreamRead";
            public static string s_FileStreamRead = "FileStreamRead";
            public static string s_ProcessStreamingRequest = "ProcessStreamingRequest";
        }
#endif
        
        public static string s_OutputDir = "/EasyProbe";
        public static string s_MetadataPath = s_OutputDir + "/EasyProbeMetadata.byte";
        public static string s_L0L1DataPath = s_OutputDir + "/EasyProbeL0L1.byte";
        public static string s_L2DataPath = s_OutputDir + "/EasyProbeL2.byte";
        public static string s_CellDataPath = s_OutputDir + "/EasyProbeCell.byte";
        
        private static int _EasyProbeSHAr = Shader.PropertyToID("_EasyProbeSHAr");
        private static int _EasyProbeSHAg = Shader.PropertyToID("_EasyProbeSHAg");
        private static int _EasyProbeSHAb = Shader.PropertyToID("_EasyProbeSHAb");
        private static int _EasyProbeSHBr = Shader.PropertyToID("_EasyProbeSHBr");
        private static int _EasyProbeSHBg = Shader.PropertyToID("_EasyProbeSHBg");
        private static int _EasyProbeSHBb = Shader.PropertyToID("_EasyProbeSHBb");
        private static int _EasyProbeSHC = Shader.PropertyToID("_EasyProbeSHC");
        
        private static int _EasyProbeVolumeSize = Shader.PropertyToID("_EasyProbeVolumeSize");
        private static int _EasyProbeVolumeWorldOffset = Shader.PropertyToID("_EasyProbeVolumeWorldOffset");
        private static int _EasyProbeNoiseFrameIndex = Shader.PropertyToID("_EasyProbeNoiseFrameIndex");
        private static int _EasyPVSamplingNoise = Shader.PropertyToID("_EasyPVSamplingNoise");
       
        private static string s_CommandBufferName = "EasyProbeVolume";

        private static string s_EasyProbeSHArName = "_EasyProbeSHAr";
        private static string s_EasyProbeSHAgName = "_EasyProbeSHAg";
        private static string s_EasyProbeSHAbName = "_EasyProbeSHAb";
        private static string s_EasyProbeSHBrName = "_EasyProbeSHBr";
        private static string s_EasyProbeSHBgName = "_EasyProbeSHBg";
        private static string s_EasyProbeSHBbName = "_EasyProbeSHBb";
        private static string s_EasyProbeSHCName = "_EasyProbeSHC";
        
        static Texture s_EasyProbeSHAr = null;
        static Texture s_EasyProbeSHAg = null;
        static Texture s_EasyProbeSHAb = null;
        static Texture s_EasyProbeSHBr = null;
        static Texture s_EasyProbeSHBg = null;
        static Texture s_EasyProbeSHBb = null;
        static Texture s_EasyProbeSHC = null;
        
        static NativeArray<byte> s_SHAr;
        private static DiskStreamingRequest s_SHArStreamingRequest;
        static NativeArray<byte> s_SHAg;
        private static DiskStreamingRequest s_SHAgStreamingRequest;
        static NativeArray<byte> s_SHAb;
        private static DiskStreamingRequest s_SHAbStreamingRequest;
        static NativeArray<byte>  s_SHBr;
        private static DiskStreamingRequest s_SHBrStreamingRequest;
        static NativeArray<byte> s_SHBg;
        private static DiskStreamingRequest s_SHBgStreamingRequest;
        static NativeArray<byte>  s_SHBb;
        private static DiskStreamingRequest s_SHBbStreamingRequest;
        static NativeArray<byte>  s_SHC;
        private static DiskStreamingRequest s_SHCStreamingRequest;

        private static bool s_HasAllocStreamingRequestL0L1 = false;
        private static bool s_HasAllocStreamingRequestL2 = false;

        public static EasyProbeMetaData s_Metadata;
        public static bool s_NeedReloadMetadata = true;
        
        
        public static Vector3 s_ProbeVolumeSize;
        public static Vector4 s_ProbeVolumeWorldOffset;
        
        private static bool s_EnableStreaming = false;
            
        private static EasyProbeSetup.MemoryBudget s_Budget;
        private static ProbeVolumeSHBands s_Bands;
        
        private const int k_BytesPerHalf4 = 8;
        
        private static Dictionary<string, FileHandle> s_FileHandleMap = new();
        private static byte[] s_MetadataBuffer;
        
        static bool AllocBufferDataIfNeeded(ref Texture probeRT, ref NativeArray<byte> sh,
            int width, int height, int depth, string name, bool allocRenderTexture = false)
        {
            bool hasAlloc = false;
            
            if (probeRT == null || probeRT.width != width || probeRT.height != height ||
                ((probeRT as Texture3D != null) && (probeRT as Texture3D).depth != depth) ||
                ((probeRT as RenderTexture != null) && (probeRT as RenderTexture).depth != depth))
            {
                probeRT = CreateDataTexture(width, height,
                    depth, GraphicsFormat.R16G16B16A16_SFloat, name,
                    allocRenderTexture);
                hasAlloc = true;
            }

            var totalLength = width * height * depth * k_BytesPerHalf4;
            if (sh == null || sh.Length != totalLength)
            {
                if (sh != null && sh.IsCreated)
                {
                    sh.Dispose();
                }

                sh = new NativeArray<byte>(totalLength, Allocator.Persistent, NativeArrayOptions.ClearMemory);
                hasAlloc = true;
            }
            
            return hasAlloc;
        }
        
        public static int EstimateTextureMemoryCost(int width, int height, int depth, GraphicsFormat format)
        {
            int elementSize = format == GraphicsFormat.R16G16B16A16_SFloat ? 8 :
                format == GraphicsFormat.R8G8B8A8_UNorm ? 4 : 1;
            return (width * height * depth) * elementSize;
        }
        
        public static int EstimateBufferMemoryCost(int width, int height, int depth)
        {
            return (width * height * depth) * 64;
        }
        
        public static Texture CreateDataTexture(int width, int height, int depth, GraphicsFormat format, string name, bool allocateRendertexture)
        {
            Texture texture;
            if (allocateRendertexture)
            {
                texture = new RenderTexture(new RenderTextureDescriptor()
                {
                    width = width,
                    height = height,
                    volumeDepth = depth,
                    graphicsFormat = format,
                    mipCount = 1,
                    enableRandomWrite = true,
                    dimension = TextureDimension.Tex3D,
                    msaaSamples = 1,
                });
                
            }
            else
            {
                texture = new Texture3D(width, height, depth, format, TextureCreationFlags.None, 1);
            }

            texture.hideFlags = HideFlags.HideAndDontSave;
            texture.name = name;
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;
            
            if (allocateRendertexture)
                (texture as RenderTexture).Create();
            return texture;
        }

        static bool ReadBytesFromRelativePath(string path, ref byte[] result)
        {
            var currentScenePath = SceneManagement.SceneManager.GetActiveScene().path;
            var lastIndexOfSep = currentScenePath.LastIndexOf("/");
            currentScenePath = currentScenePath.Substring(0, lastIndexOfSep);

            var filePath = currentScenePath + path;
            
            try
            {
                result = File.ReadAllBytes(filePath);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError("[EasyProbeStreaming](ReadByteFromRelativePath):" + e.Message);
            }
            
            return true;
        }
        
        public static bool LoadMetadata(ref EasyProbeMetaData metaData, string volumeHash = "")
        {
            
            // TODO volumeHash
            int size = Marshal.SizeOf(typeof(EasyProbeMetaData));
            if (s_MetadataBuffer == null || s_MetadataBuffer.Length < size)
            {
                s_MetadataBuffer = new byte[size];
            }
            
            if (!ReadBytesFromRelativePath(s_MetadataPath, ref s_MetadataBuffer))
            { 
                Debug.LogError("[EasyProbeStreaming](LoadMetadata): load meta data failed.");
                return false;
            }

            IntPtr ptr = Marshal.AllocHGlobal(size);
            bool isSuccess = false;
            try
            {
                Marshal.Copy(s_MetadataBuffer, 0, ptr, size);
                metaData = Marshal.PtrToStructure<EasyProbeMetaData>(ptr);
                isSuccess = true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[EasyProbeStreaming](LoadMetadata): {e.Message}");
            }
            finally
            {
                Marshal.FreeHGlobal(ptr);
            }

            return isSuccess;
        }
        
        public static void UpdateCellStreaming(ScriptableRenderContext context, ref RenderingData renderingData, 
            EasyProbeSetup.EasyProbeSettings settings, float radius)
        {
            if (s_NeedReloadMetadata)
            {
                if (!LoadMetadata(ref s_Metadata))
                {
                    Debug.LogError("[EasyProbeStreaming](UpdateCellStreaming): load metadata error.");
                    return;
                }

                s_NeedReloadMetadata = false;

            }

            var camera = renderingData.cameraData.camera;

            if (ProcessStreaming(camera, radius))
            {
                UploadTextureData();
            
                PushRuntimeData(context, ref renderingData.cameraData);
            }
            
        }

        public static void UpdateDataLocationTexture<T>(Texture output, NativeArray<T> input) where T : struct
        {
            var output3D = output as Texture3D;
            var outputNativeArray = output3D.GetPixelData<T>(0);
            Debug.Assert(outputNativeArray.Length >= input.Length);
            outputNativeArray.GetSubArray(0, input.Length).CopyFrom(input);
            (output as Texture3D).Apply();
        }

        public static Vector3Int CalculateBufferSize(ref EasyProbeMetaData metaData, Vector3Int boxMin, Vector3Int boxMax)
        {
            var width = (metaData.probeCountPerCellAxis - 1) * (boxMax.x - boxMin.x) / metaData.cellSize + 1;
            var height = (metaData.probeCountPerCellAxis - 1) * (boxMax.y - boxMin.y) / metaData.cellSize + 1;
            var depth = (metaData.probeCountPerCellAxis - 1) * (boxMax.z - boxMin.z) / metaData.cellSize + 1;
            return new Vector3Int(width, height, depth);
        }

        static FileHandle GetFileHandle(string path)
        {
            if (!s_FileHandleMap.TryGetValue(path, out var fileHandle))
            {
                fileHandle = AsyncReadManager.OpenFileAsync(path);
                s_FileHandleMap.Add(path, fileHandle);
            }
            
            return fileHandle;
        }

        static bool PrepareBuffer(Vector3Int boxMinWS, Vector3Int boxMaxWS)
        {
            #region Calculate Buffer Size

            var size = CalculateBufferSize(ref s_Metadata, boxMinWS, boxMaxWS);
            var width = size.x;
            var height = size.y;
            var depth = size.z;

            if (width > SystemInfo.maxTexture3DSize || height > SystemInfo.maxTexture3DSize ||
                depth > SystemInfo.maxTexture3DSize)
            {
                Debug.LogError("[EasyProbeStreaming] (DoStreaming) : 3d texture size is out of range.");
                return false;
            }

            if (width <= 0 || height <= 0 || depth <= 0)
            {
                Debug.LogError("[EasyProbeStreaming] (DoStreaming) : 3d texture size is out of range.");
                return false;
            }

            #endregion

            #region Alloc Buffer

            AllocBufferDataIfNeeded(ref s_EasyProbeSHAr, ref s_SHAr,
                width, height, depth, s_EasyProbeSHArName);
            AllocBufferDataIfNeeded(ref s_EasyProbeSHAg, ref s_SHAg,
                width, height, depth, s_EasyProbeSHAgName);
            AllocBufferDataIfNeeded(ref s_EasyProbeSHAb, ref s_SHAb,
                width, height, depth, s_EasyProbeSHAbName);
            
            AllocBufferDataIfNeeded(ref s_EasyProbeSHBr, ref s_SHBr,
                width, height, depth, s_EasyProbeSHBrName);
            AllocBufferDataIfNeeded(ref s_EasyProbeSHBg, ref s_SHBg,
                width, height, depth, s_EasyProbeSHBgName);
            AllocBufferDataIfNeeded(ref s_EasyProbeSHBb, ref s_SHBb,
                width, height, depth, s_EasyProbeSHBbName);
            
            AllocBufferDataIfNeeded(ref s_EasyProbeSHC, ref s_SHC,
                width, height, depth, s_EasyProbeSHCName);

            #endregion

            return true;
        }

        static void PrepareShaderConstant(Vector3Int boxMaxWS, Vector3Int boxMinWS, Vector3Int clampedCellMinWS)
        {
            s_ProbeVolumeSize.x = boxMaxWS.x - boxMinWS.x + s_Metadata.probeSpacing;
            s_ProbeVolumeSize.y = boxMaxWS.y - boxMinWS.y + s_Metadata.probeSpacing;
            s_ProbeVolumeSize.z = boxMaxWS.z - boxMinWS.z + s_Metadata.probeSpacing;
            
            var halfProbeSpacing = s_Metadata.probeSpacing / 2f;
            s_ProbeVolumeWorldOffset = new Vector4(
                clampedCellMinWS.x - halfProbeSpacing, 
                clampedCellMinWS.y - halfProbeSpacing,
                clampedCellMinWS.z - halfProbeSpacing, 
                1.0f);
        }

        static void AllocStreamingRequestIfNeeded(int maxRequestCount)
        {
            if (s_HasAllocStreamingRequestL0L1)
            {
                if (!s_SHArStreamingRequest.ResizeIfNeeded(maxRequestCount))
                {
                    s_SHArStreamingRequest.Clear();
                }
                
                if (!s_SHAgStreamingRequest.ResizeIfNeeded(maxRequestCount))
                {
                    s_SHAgStreamingRequest.Clear();
                }
                
                if (!s_SHAbStreamingRequest.ResizeIfNeeded(maxRequestCount))
                {
                    s_SHAbStreamingRequest.Clear();
                }
                
            }
            else
            {
                s_SHArStreamingRequest = new DiskStreamingRequest(maxRequestCount);
                s_SHAgStreamingRequest = new DiskStreamingRequest(maxRequestCount);
                s_SHAbStreamingRequest = new DiskStreamingRequest(maxRequestCount);

                s_HasAllocStreamingRequestL0L1 = true;
            }
            
            if (s_HasAllocStreamingRequestL2)
            {
                if (!s_SHBrStreamingRequest.ResizeIfNeeded(maxRequestCount))
                {
                    s_SHBrStreamingRequest.Clear();
                }
                
                if (!s_SHBgStreamingRequest.ResizeIfNeeded(maxRequestCount))
                {
                    s_SHBgStreamingRequest.Clear();
                }
                
                if (!s_SHBbStreamingRequest.ResizeIfNeeded(maxRequestCount))
                {
                    s_SHBbStreamingRequest.Clear();
                }
                
                if (!s_SHCStreamingRequest.ResizeIfNeeded(maxRequestCount))
                {
                    s_SHCStreamingRequest.Clear();
                }
                
            }
            else
            {
                s_SHBrStreamingRequest = new DiskStreamingRequest(maxRequestCount);
                s_SHBgStreamingRequest = new DiskStreamingRequest(maxRequestCount);
                s_SHBbStreamingRequest = new DiskStreamingRequest(maxRequestCount);
                s_SHCStreamingRequest = new DiskStreamingRequest(maxRequestCount);
                s_HasAllocStreamingRequestL2 = true;
            }
        }
        
        static unsafe void PrepareStreamingRequest(Vector3Int boxMaxWS, Vector3Int boxMinWS, Vector3Int clampedCellMinWS, Vector3Int clampedCellMaxWS)
        {
            var probeCountPerSlice = s_Metadata.probeCountPerVolumeAxis.x * s_Metadata.probeCountPerVolumeAxis.y; 
            var validCellCountPerAxis = (clampedCellMaxWS - clampedCellMinWS) / s_Metadata.cellSize;
            var boxCellCountPerAxis = (boxMaxWS - boxMinWS) / s_Metadata.cellSize;
            var probeCountPerAxis = (s_Metadata.probeCountPerCellAxis - 1) * validCellCountPerAxis + Vector3Int.one;
            var boxProbeCountPerAxis = (s_Metadata.probeCountPerCellAxis - 1) * boxCellCountPerAxis + Vector3Int.one;
            var outsideProbeCountPerAxis = boxProbeCountPerAxis - probeCountPerAxis;
            var bytesToReadPerLine = probeCountPerAxis.x * k_BytesPerHalf4;
            var bytesInBoxPerLine = boxProbeCountPerAxis.x * k_BytesPerHalf4;
            
            var cellOffset = (clampedCellMinWS - s_Metadata.cellMin) / s_Metadata.cellSize;
            var probeOffset = (s_Metadata.probeCountPerCellAxis - 1) * cellOffset;
            var probeIndexStart = cellOffset.x * (s_Metadata.probeCountPerCellAxis - 1)
                                + probeOffset.y * s_Metadata.probeCountPerVolumeAxis.x
                                + probeOffset.z * probeCountPerSlice;

            int bufferOffset = 0;
            
            var totalProbeCount = probeCountPerSlice * s_Metadata.probeCountPerVolumeAxis.z;
            
            var shArBaseAddress = (byte*) s_SHAr.GetUnsafePtr();
            var shArMappedAddress = shArBaseAddress;
            var shAgBaseAddress = (byte*) s_SHAg.GetUnsafePtr();
            var shAgMappedAddress = shAgBaseAddress;
            var shAbBaseAddress = (byte*) s_SHAb.GetUnsafePtr();
            var shAbMappedAddress = shAbBaseAddress;

            AllocStreamingRequestIfNeeded(probeCountPerAxis.z * probeCountPerAxis.y);
            
            for (int slice = 0; slice < probeCountPerAxis.z; ++slice)
            {
                for (int line = 0; line < probeCountPerAxis.y; ++line)
                {
                    var l0l1ProbeDataLineStart = (probeIndexStart 
                                                  + slice * probeCountPerSlice
                                                  + line * s_Metadata.probeCountPerVolumeAxis.x) * k_BytesPerHalf4;
                    var fileOffset = l0l1ProbeDataLineStart;
                    
                    shArMappedAddress += bufferOffset;
                    shAgMappedAddress += bufferOffset;
                    shAbMappedAddress += bufferOffset;
                    
                    s_SHArStreamingRequest.AddReadCommand(fileOffset, bytesToReadPerLine, shArMappedAddress);
                    
                    fileOffset += totalProbeCount * k_BytesPerHalf4;
                    s_SHAgStreamingRequest.AddReadCommand(fileOffset, bytesToReadPerLine, shAgMappedAddress);
                    
                    fileOffset += totalProbeCount * k_BytesPerHalf4;
                    s_SHAbStreamingRequest.AddReadCommand(fileOffset, bytesToReadPerLine, shAbMappedAddress);
                    
                    bufferOffset += bytesInBoxPerLine;
                }
                
                bufferOffset += bytesInBoxPerLine * outsideProbeCountPerAxis.y;
            }
        }
        
        static bool DoStreaming(Camera camera, float radius)
        {
            #if UNITY_EDITOR || ENABLE_PROFILER
            Profiler.BeginSample(ProfilerSampler.s_DoStreaming);
            #endif

            var cameraAABB = CalculateSphereAABB(CalculateCameraFrustumSphere(ref s_Metadata, camera, radius));
            CalculateCellRange(cameraAABB, out var clampedCellMinWS, out var clampedCellMaxWS,
                out var boxMinWS, out var boxMaxWS);

            if (!PrepareBuffer(boxMinWS, boxMaxWS))
            {
                Debug.LogError("[EasyProbeStreaming](DoStreaming): failed to prepare buffer.");
                return false;
            }

            PrepareShaderConstant(boxMaxWS, boxMinWS, clampedCellMinWS);
            
            PrepareStreamingRequest(boxMaxWS, boxMinWS, clampedCellMinWS, clampedCellMaxWS);
            
            #region Streaming Loading

            // L0L1
            #if UNITY_EDITOR || ENABLE_PROFILER
            Profiler.BeginSample(ProfilerSampler.s_ProcessStreamingRequest);
            #endif
            
            var currentScenePath = SceneManagement.SceneManager.GetActiveScene().path;
            var lastIndexOfSep = currentScenePath.LastIndexOf("/");
            currentScenePath = currentScenePath.Substring(0, lastIndexOfSep);

            var filePath = currentScenePath + s_L0L1DataPath;

            var fileHandle = GetFileHandle(filePath);

            s_SHArStreamingRequest.RunCommands(fileHandle);
            s_SHAbStreamingRequest.RunCommands(fileHandle);
            s_SHAgStreamingRequest.RunCommands(fileHandle);
            
            #if UNITY_EDITOR || ENABLE_PROFILER
            Profiler.EndSample();
            #endif
            
            #endregion
            
            #if UNITY_EDITOR || ENABLE_PROFILER
            Profiler.EndSample();
            #endif
            
            return true;
        }


        static unsafe bool LoadInOnce()
        {
          
            var cellMin = s_Metadata.cellMin;
            var cellMax = s_Metadata.cellMax;
            var probeSpacing = s_Metadata.probeSpacing;
            var halfSize = probeSpacing / 2.0f;

            var globalProbeCountInTotal = s_Metadata.probeCountPerVolumeAxis.x * s_Metadata.probeCountPerVolumeAxis.y *
                                          s_Metadata.probeCountPerVolumeAxis.z;
            s_ProbeVolumeWorldOffset = 
                new Vector4(cellMin.x - halfSize, cellMin.y - halfSize, cellMin.z - halfSize, 1.0f);
            s_ProbeVolumeSize = cellMax - cellMin;
            s_ProbeVolumeSize += new Vector3(
                probeSpacing,
                probeSpacing,
                probeSpacing
            );
            
            var probeCountPerAxie = s_ProbeVolumeSize / probeSpacing;
            var width = (int)probeCountPerAxie.x;
            var height = (int)probeCountPerAxie.y;
            var depth = (int)probeCountPerAxie.z;
            
            var hasAlloc = false;
            
            hasAlloc |= AllocBufferDataIfNeeded(ref s_EasyProbeSHAr, ref s_SHAr,
                width, height, depth, s_EasyProbeSHArName);
            hasAlloc |= AllocBufferDataIfNeeded(ref s_EasyProbeSHAg, ref s_SHAg,
                width, height, depth, s_EasyProbeSHAgName);
            hasAlloc |= AllocBufferDataIfNeeded(ref s_EasyProbeSHAb, ref s_SHAb,
                width, height, depth, s_EasyProbeSHAbName);
            
            hasAlloc |= AllocBufferDataIfNeeded(ref s_EasyProbeSHBr, ref s_SHBr,
                width, height, depth, s_EasyProbeSHBrName);
            hasAlloc |= AllocBufferDataIfNeeded(ref s_EasyProbeSHBg, ref s_SHBg,
                width, height, depth, s_EasyProbeSHBgName);
            hasAlloc |= AllocBufferDataIfNeeded(ref s_EasyProbeSHBb, ref s_SHBb,
                width, height, depth, s_EasyProbeSHBbName);
            
            hasAlloc |= AllocBufferDataIfNeeded(ref s_EasyProbeSHC, ref s_SHC,
                width, height, depth, s_EasyProbeSHCName);
            
            if (hasAlloc)
            {
                var totalProbeCountToLoad = width * height * depth;
                var bytesToLoad = totalProbeCountToLoad * k_BytesPerHalf4;
                var fileOffsetPerComponent = globalProbeCountInTotal * k_BytesPerHalf4;
                
                var currentScenePath = SceneManagement.SceneManager.GetActiveScene().path;
                var lastIndexOfSep = currentScenePath.LastIndexOf("/");
                currentScenePath = currentScenePath.Substring(0, lastIndexOfSep);

                AllocStreamingRequestIfNeeded(1);
                
                // L0L1
                var filePath = currentScenePath + s_L0L1DataPath;
                var fileHandle = GetFileHandle(filePath);
                
                s_SHArStreamingRequest.AddReadCommand(0, bytesToLoad, (byte*) s_SHAr.GetUnsafePtr());
                s_SHArStreamingRequest.RunCommands(fileHandle);
                
                s_SHAgStreamingRequest.AddReadCommand(fileOffsetPerComponent, bytesToLoad, (byte*) s_SHAg.GetUnsafePtr());
                s_SHAgStreamingRequest.RunCommands(fileHandle);
                
                s_SHAbStreamingRequest.AddReadCommand(fileOffsetPerComponent * 2, bytesToLoad, (byte*) s_SHAb.GetUnsafePtr());
                s_SHAbStreamingRequest.RunCommands(fileHandle);
                
                // L2
                filePath = currentScenePath + s_L2DataPath;
                fileHandle = GetFileHandle(filePath);
                
                s_SHBrStreamingRequest.AddReadCommand(0, bytesToLoad, (byte*) s_SHBr.GetUnsafePtr());
                s_SHBrStreamingRequest.RunCommands(fileHandle);
                
                s_SHBgStreamingRequest.AddReadCommand(fileOffsetPerComponent, bytesToLoad, (byte*) s_SHBg.GetUnsafePtr());
                s_SHBgStreamingRequest.RunCommands(fileHandle);
                
                s_SHBbStreamingRequest.AddReadCommand(fileOffsetPerComponent * 2, bytesToLoad, (byte*) s_SHBb.GetUnsafePtr());
                s_SHBbStreamingRequest.RunCommands(fileHandle);
                
                s_SHCStreamingRequest.AddReadCommand(fileOffsetPerComponent * 3, bytesToLoad, (byte*) s_SHC.GetUnsafePtr());
                s_SHCStreamingRequest.RunCommands(fileHandle);
            }

            return true;
        }
        
        public static bool ProcessStreaming(Camera camera, float radius)
        {
            if (camera.cameraType == CameraType.Preview || camera.cameraType == CameraType.Reflection)
            {
                return false;
            }
            
            if (EasyProbeSetup.Instance != null)
            {
                if (s_EnableStreaming != EasyProbeSetup.Instance.settings.enableStreaming)
                {
                    s_EnableStreaming = EasyProbeSetup.Instance.settings.enableStreaming;
                    
                }
                
                if (s_EnableStreaming && camera.cameraType == CameraType.Game)
                {
                    return DoStreaming(camera, radius);
                }
                else
                {
                     return LoadInOnce();
                }
            }

            return false;
        }

        static Vector3 Max3(Vector3 a, Vector3 b)
        {
            return new Vector3(Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y), Mathf.Max(a.z, b.z));
        }

        static Vector3 Min3(Vector3 a, Vector3 b)
        {
            return new Vector3(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Min(a.z, b.z));
        }
        
        static Vector3Int Max3(Vector3Int a, Vector3Int b)
        {
            return new Vector3Int(Math.Max(a.x, b.x), Math.Max(a.y, b.y), Math.Max(a.z, b.z));
        }

        static Vector3Int Min3(Vector3Int a, Vector3Int b)
        {
            return new Vector3Int(Math.Min(a.x, b.x), Math.Min(a.y, b.y), Math.Min(a.z, b.z));
        }
        
        public static Vector3Int GetCellIndexStart(Vector3 minPoint, int cellSize)
        {
            var index = minPoint / cellSize;
            return new Vector3Int(
                Mathf.FloorToInt(index.x),
                Mathf.FloorToInt(index.y),
                Mathf.FloorToInt(index.z)
            );
        }

        public static Vector3Int GetCellIndexEnd(Vector3 maxPoint, int cellSize)
        {
            var index = maxPoint / cellSize;
            return new Vector3Int(
                Mathf.CeilToInt(index.x),
                Mathf.CeilToInt(index.y),
                Mathf.CeilToInt(index.z)
            );
        }
        
        public static void CalculateCellRange(Bounds cameraAABB, 
            out Vector3Int clampedCellMinWS, 
            out Vector3Int clampedCellMaxWS,
            out Vector3Int boxMinWS,
            out Vector3Int boxMaxWS)
        {
            int cellSize = s_Metadata.cellSize;
            var cellMin = s_Metadata.cellMin;
            var cellMax = s_Metadata.cellMax;

            // {
            //     // test
            //     boxMaxWS = cellMax;
            //     boxMinWS = cellMin;
            //
            //     clampedCellMinWS = cellMin;
            //     clampedCellMaxWS = cellMax;
            //     return;
            // }
            
            var boxCenter = GetCellIndexStart(cameraAABB.center, cellSize) * cellSize 
                            + new Vector3Int(cellSize / 2, cellSize / 2, cellSize / 2);
            
            boxMinWS = GetCellIndexStart(cameraAABB.min, cellSize) * cellSize;
            boxMaxWS = GetCellIndexEnd(cameraAABB.max, cellSize) * cellSize;
            var extend = Max3(boxMaxWS - boxCenter, boxCenter - boxMinWS);
            var maxExtend = Mathf.Max(extend.x, extend.y, extend.z);
            
            boxMinWS = boxCenter - Vector3Int.one * maxExtend;
            boxMaxWS = boxCenter + Vector3Int.one * maxExtend;
                
            clampedCellMinWS = Max3(boxMinWS, cellMin);
            clampedCellMaxWS = Min3(boxMaxWS, cellMax);
            
        }
        
        
        public static Bounds CalculateSphereAABB(Vector4 sphere)
        {
            return new Bounds(new Vector3(sphere.x, sphere.y, sphere.z), Vector3.one * sphere.w * 2f);
        }
        
        public static Vector4 CalculateCameraFrustumSphere(ref EasyProbeMetaData metaData, Camera camera,float radius)
        {
            Vector3[] nearCorners = new Vector3[4];
            
            camera.CalculateFrustumCorners(new Rect(0, 0, 1, 1), camera.nearClipPlane, Camera.MonoOrStereoscopicEye.Mono, nearCorners);
            
            for (int i = 0; i < 4; i++)
            {
                nearCorners[i] = camera.transform.TransformPoint(nearCorners[i]);
            }
            
            Vector3 centerOfNearPlane = Vector3.zero;

            foreach (var corner in nearCorners)
            {
                centerOfNearPlane += corner;
            }

            centerOfNearPlane /= 4;

            var distanceFromTopLeftNearCorner = Vector3.Distance(centerOfNearPlane, nearCorners[0]);

            radius = Mathf.Max(radius, distanceFromTopLeftNearCorner * 1.4142135623730951f);
            radius = Mathf.Max(metaData.cellSize / 2f, radius);
            
            var distanceFromNearCenter = Mathf.Sqrt(radius * radius - distanceFromTopLeftNearCorner * distanceFromTopLeftNearCorner);
            Vector3 center = Vector3.zero;
            center = centerOfNearPlane + camera.transform.forward * distanceFromNearCenter;
            
            return new Vector4(center.x, center.y, center.z, radius);
        }
        
        public static void UploadTextureData()
        {
#if UNITY_EDITOR
            try
#endif
            {
                s_SHArStreamingRequest?.Wait();
                UpdateDataLocationTexture(s_EasyProbeSHAr, s_SHAr);
                s_SHAgStreamingRequest?.Wait();
                UpdateDataLocationTexture(s_EasyProbeSHAg, s_SHAg);
                s_SHAbStreamingRequest?.Wait();
                UpdateDataLocationTexture(s_EasyProbeSHAb, s_SHAb);
                
                s_SHBrStreamingRequest?.Wait();
                UpdateDataLocationTexture(s_EasyProbeSHBr, s_SHBr);
                s_SHBgStreamingRequest?.Wait();
                UpdateDataLocationTexture(s_EasyProbeSHBg, s_SHBg);
                s_SHBbStreamingRequest?.Wait();
                UpdateDataLocationTexture(s_EasyProbeSHBb, s_SHBb);
                
                s_SHCStreamingRequest?.Wait();
                UpdateDataLocationTexture(s_EasyProbeSHC, s_SHC);
            }
#if UNITY_EDITOR
            catch (Exception e)
            {
                Debug.LogError("[EasyProbeStreaming] (UploadTextureData): upload texture failed.");
            }
#endif
        }
        
        public static void PushRuntimeData(ScriptableRenderContext context, ref CameraData cameraData)
        {
            var cmd = CommandBufferPool.Get(s_CommandBufferName);
            
            cmd.SetGlobalTexture(_EasyProbeSHAr, s_EasyProbeSHAr);
            cmd.SetGlobalTexture(_EasyProbeSHAg, s_EasyProbeSHAg);
            cmd.SetGlobalTexture(_EasyProbeSHAb, s_EasyProbeSHAb);
            cmd.SetGlobalTexture(_EasyProbeSHBr, s_EasyProbeSHBr);
            cmd.SetGlobalTexture(_EasyProbeSHBg, s_EasyProbeSHBg);
            cmd.SetGlobalTexture(_EasyProbeSHBb, s_EasyProbeSHBb);
            cmd.SetGlobalTexture(_EasyProbeSHC, s_EasyProbeSHC);
            
            cmd.SetGlobalVector(_EasyProbeVolumeSize, s_ProbeVolumeSize);
            s_ProbeVolumeWorldOffset.w = EasyProbeVolume.s_EasyProbeIntensity;
            cmd.SetGlobalVector(_EasyProbeVolumeWorldOffset, s_ProbeVolumeWorldOffset);
            
            cmd.SetGlobalFloat(_EasyProbeNoiseFrameIndex, cameraData.antialiasing == AntialiasingMode.TemporalAntiAliasing ?
                Time.frameCount : 0);
            cmd.SetGlobalFloat(_EasyPVSamplingNoise, EasyProbeVolume.s_EasyPVSamplingNoise);
            
            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();
            CommandBufferPool.Release(cmd);
        }

        public static void Dispose()
        { 
            s_SHArStreamingRequest?.Cancel();
            s_SHArStreamingRequest?.Dispose();
            
            s_SHAgStreamingRequest?.Cancel();
            s_SHAgStreamingRequest?.Dispose();
            
            s_SHAbStreamingRequest?.Cancel();
            s_SHAbStreamingRequest?.Dispose();
            
            s_SHBrStreamingRequest?.Cancel();
            s_SHBrStreamingRequest?.Dispose();
            
            s_SHBgStreamingRequest?.Cancel();
            s_SHBgStreamingRequest?.Dispose();
            
            s_SHBbStreamingRequest?.Cancel();
            s_SHBbStreamingRequest?.Dispose();
            
            s_SHCStreamingRequest?.Cancel();
            s_SHCStreamingRequest?.Dispose();

            s_HasAllocStreamingRequestL0L1 = false;
            s_HasAllocStreamingRequestL2 = false;
            
            foreach (var keyValue in s_FileHandleMap)
            {
                keyValue.Value.JobHandle.Complete();
                keyValue.Value.Close();
            }
            
            s_FileHandleMap.Clear();
            
            
            CoreUtils.Destroy(s_EasyProbeSHAr);
            s_EasyProbeSHAr = null;
            CoreUtils.Destroy(s_EasyProbeSHAg);
            s_EasyProbeSHAg = null;
            CoreUtils.Destroy(s_EasyProbeSHAb);
            s_EasyProbeSHAb = null;
            CoreUtils.Destroy(s_EasyProbeSHBr);
            s_EasyProbeSHBr = null;
            CoreUtils.Destroy(s_EasyProbeSHBg);
            s_EasyProbeSHBg = null;
            CoreUtils.Destroy(s_EasyProbeSHBb);
            s_EasyProbeSHBb = null;
            CoreUtils.Destroy(s_EasyProbeSHC);
            s_EasyProbeSHC = null;

            s_SHAr.Dispose();
            s_SHAg.Dispose();
            s_SHAb.Dispose();
            
            s_SHBr.Dispose();
            s_SHBg.Dispose();
            s_SHBb.Dispose();

            s_SHC.Dispose();

            s_NeedReloadMetadata = true;
        }
    }

}