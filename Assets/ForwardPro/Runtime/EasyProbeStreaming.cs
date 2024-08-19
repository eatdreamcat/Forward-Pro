
using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Mathematics;
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

    public struct ProbeStreamingRequest
    {
        public int fileOffset;
        public int length;
        public int bufferOffset;
    }
    
    public static class EasyProbeStreaming 
    {
#if UNITY_EDITOR || ENABLE_PROFILER
        static class ProfilerSampler
        {
            public static string s_DoStreaming = "DoStreaming";
            public static string s_TextureUploadCPU = "TextureUploadCPU";
            public static string s_BytesStreamRead = "BytesStreamRead";
            public static string s_FileStreamSeek = "FileStreamSeek";
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
        
        public static Texture s_EasyProbeSHAr = null;
        public static Texture s_EasyProbeSHAg = null;
        public static Texture s_EasyProbeSHAb = null;
        public static Texture s_EasyProbeSHBr = null;
        public static Texture s_EasyProbeSHBg = null;
        public static Texture s_EasyProbeSHBb = null;
        public static Texture s_EasyProbeSHC = null;
        
        public static byte[] s_SHAr;
        public static byte[] s_SHAg;
        public static byte[] s_SHAb;
        public static byte[] s_SHBr;
        public static byte[] s_SHBg;
        public static byte[] s_SHBb;
        public static byte[] s_SHC;

        public static EasyProbeMetaData s_Metadata;
        public static List<EasyCellData> s_CellDatas = new List<EasyCellData>();
        public static bool s_NeedReloadMetadata = true;
        
        
        public static Vector3 s_ProbeVolumeSize;
        public static Vector4 s_ProbeVolumeWorldOffset;

        private static int s_BufferStartIndex;
        private static int s_BufferEndIndex;

        private static int s_BytesInUse = 0;

        private static List<ProbeStreamingRequest> s_SHArRequests = new();
        private static List<ProbeStreamingRequest> s_SHAgRequests = new();
        private static List<ProbeStreamingRequest> s_SHAbRequests = new();
        
        private static List<ProbeStreamingRequest> s_SHBrRequests = new();
        private static List<ProbeStreamingRequest> s_SHBgRequests = new();
        private static List<ProbeStreamingRequest> s_SHBbRequests = new();
        private static List<ProbeStreamingRequest> s_SHCRequests = new();
        
        private static bool s_EnableStreaming = false;
            
        private static EasyProbeSetup.MemoryBudget s_Budget;
        private static ProbeVolumeSHBands s_Bands;
        
        private const int k_BytesPerHalf4 = 8;
        
        struct FileStreamKey
        {
            public FileAccess access;
            public FileMode mode;
            public string path;
        }
        
        private static Dictionary<FileStreamKey, FileStream> s_FileStreamMap = new();
        private static byte[] s_MetadataBuffer;
        
        static bool AllocBufferDataIfNeeded(ref Texture probeRT, ref byte[] sh,
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
                sh = new byte[totalLength];
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
        
        public static bool ReadBytesFromRelativePath(FileStream fs, ref byte[] buffer, int bufferOffset, long fileOffset, int length, out int readBytes)
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

        public static void UpdateDataLocationTexture<T>(Texture output, T[] input) where T : struct
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

        static bool LoadSHBytes(FileStream fileStream, List<ProbeStreamingRequest> requests, ref byte[] buffer, out int totalReadBytes)
        {
            totalReadBytes = 0;
            for (int i = 0; i < requests.Count; ++i)
            {
                var request = requests[i];
                if (!ReadBytesFromRelativePath(fileStream, ref buffer, request.bufferOffset, request.fileOffset, request.length, out var bytes))
                {
                    Debug.LogError($"[EasyProbeStreaming](ProcessStreaming): failed to load sh data {fileStream.Name} at offset {request.fileOffset}");
                    return false;
                }

                totalReadBytes += bytes;
            }
            return true;
        }
        
        static bool DoStreaming(Camera camera, float radius)
        {
            #if UNITY_EDITOR || ENABLE_PROFILER
            Profiler.BeginSample(ProfilerSampler.s_DoStreaming);
            #endif

            var cameraAABB = CalculateSphereAABB(CalculateCameraFrustumSphere(ref s_Metadata, camera, radius));
            CalculateCellRange(cameraAABB, camera, out var clampedCellMinWS, out var clampedCellMaxWS,
                out var boxMinWS, out var boxMaxWS);

            if (clampedCellMaxWS.x < clampedCellMinWS.x || clampedCellMaxWS.y < clampedCellMinWS.y ||
                clampedCellMaxWS.z < clampedCellMinWS.z)
            {
                return false;
            }
            
            s_ProbeVolumeSize.x = boxMaxWS.x - boxMinWS.x + s_Metadata.probeSpacing;
            s_ProbeVolumeSize.y = boxMaxWS.y - boxMinWS.y + s_Metadata.probeSpacing;
            s_ProbeVolumeSize.z = boxMaxWS.z - boxMinWS.z + s_Metadata.probeSpacing;
            
            var halfProbeSpacing = s_Metadata.probeSpacing / 2f;
            s_ProbeVolumeWorldOffset = new Vector4(
                clampedCellMinWS.x - halfProbeSpacing, 
                clampedCellMinWS.y - halfProbeSpacing,
                clampedCellMinWS.z - halfProbeSpacing, 
                1.0f);
            
            s_SHArRequests.Clear();
            s_SHAgRequests.Clear();
            s_SHAbRequests.Clear();
            
            s_SHBrRequests.Clear();
            s_SHBgRequests.Clear();
            s_SHBbRequests.Clear();
            s_SHCRequests.Clear();
            
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
            
            for (int slice = 0; slice < probeCountPerAxis.z; ++slice)
            {
                for (int line = 0; line < probeCountPerAxis.y; ++line)
                {
                    var l0l1ProbeDataLineStart = (probeIndexStart 
                                                  + slice * probeCountPerSlice
                                                  + line * s_Metadata.probeCountPerVolumeAxis.x) * k_BytesPerHalf4;
                    var request = new ProbeStreamingRequest()
                    {
                        fileOffset = l0l1ProbeDataLineStart,
                        length = bytesToReadPerLine,
                        bufferOffset = bufferOffset
                    };
                    s_SHArRequests.Add(request);
                    request.fileOffset += totalProbeCount * k_BytesPerHalf4;
                    s_SHAgRequests.Add(request);
                    request.fileOffset += totalProbeCount * k_BytesPerHalf4;
                    s_SHAbRequests.Add(request);
                    bufferOffset += bytesInBoxPerLine;
                }
                
                bufferOffset += bytesInBoxPerLine * outsideProbeCountPerAxis.y;
            }
            
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
            
            #region Streaming Loading

            // L0L1
            #if UNITY_EDITOR || ENABLE_PROFILER
            Profiler.BeginSample(ProfilerSampler.s_ProcessStreamingRequest);
            #endif
            
            var currentScenePath = SceneManagement.SceneManager.GetActiveScene().path;
            var lastIndexOfSep = currentScenePath.LastIndexOf("/");
            currentScenePath = currentScenePath.Substring(0, lastIndexOfSep);

            var filePath = currentScenePath + s_L0L1DataPath;

            var fileStream = GetFileStream(filePath, FileMode.Open, FileAccess.Read);
          
            if (!LoadSHBytes(fileStream, s_SHArRequests, ref s_SHAr, out var shArBytes))
            {
                return false;
            }
            
            if (!LoadSHBytes(fileStream, s_SHAgRequests, ref s_SHAg, out var shAgBytes))
            {
                return false;
            }
            
            if (!LoadSHBytes(fileStream, s_SHAbRequests, ref s_SHAb, out var shAbBytes))
            {
                return false;
            }
            
            #if UNITY_EDITOR || ENABLE_PROFILER
            Profiler.EndSample();
            #endif
            
            #endregion
            
            #if UNITY_EDITOR || ENABLE_PROFILER
            Profiler.EndSample();
            #endif
            
            return true;
        }


        static bool LoadInOnce()
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

                // L0L1
                var filePath = currentScenePath + s_L0L1DataPath;
                var fileStream = GetFileStream(filePath, FileMode.Open, FileAccess.Read);
                if (!LoadSHBytes(fileStream, new List<ProbeStreamingRequest>()
                    {
                        new ProbeStreamingRequest()
                        {
                            bufferOffset = 0,
                            fileOffset = 0,
                            length = bytesToLoad
                        }
                    }, ref s_SHAr, out var shArBytes))
                {
                    Debug.LogError("[EasyProbeStreaming](ProcessStreaming): failed to load shAr data.");
                    return false;
                }
                
                if (!LoadSHBytes(fileStream, new List<ProbeStreamingRequest>()
                    {
                        new ProbeStreamingRequest()
                        {
                            bufferOffset = 0,
                            fileOffset = fileOffsetPerComponent,
                            length = bytesToLoad
                        }
                    }, ref s_SHAg, out var shAgBytes))
                {
                    Debug.LogError("[EasyProbeStreaming](ProcessStreaming): failed to load shAg data.");
                    return false;
                }
                
                if (!LoadSHBytes(fileStream, new List<ProbeStreamingRequest>()
                    {
                        new ProbeStreamingRequest()
                        {
                            bufferOffset = 0,
                            fileOffset = fileOffsetPerComponent * 2,
                            length = bytesToLoad 
                        }
                    }, ref s_SHAb, out var shAbBytes))
                {
                    Debug.LogError("[EasyProbeStreaming](ProcessStreaming): failed to load shAb data.");
                    return false;
                }
                
                // L2
                filePath = currentScenePath + s_L2DataPath;
                fileStream = GetFileStream(filePath, FileMode.Open, FileAccess.Read);
                if (!LoadSHBytes(fileStream, new List<ProbeStreamingRequest>()
                    {
                        new ProbeStreamingRequest()
                        {
                            bufferOffset = 0,
                            fileOffset = 0,
                            length = bytesToLoad
                        }
                    }, ref s_SHBr, out var shBrBytes))
                {
                    Debug.LogError("[EasyProbeStreaming](ProcessStreaming): failed to load shBr data.");
                    return false;
                }
                
                if (!LoadSHBytes(fileStream, new List<ProbeStreamingRequest>()
                    {
                        new ProbeStreamingRequest()
                        {
                            bufferOffset = 0,
                            fileOffset = fileOffsetPerComponent,
                            length = bytesToLoad
                        }
                    }, ref s_SHBg, out var shBgBytes))
                {
                    Debug.LogError("[EasyProbeStreaming](ProcessStreaming): failed to load shBg data.");
                    return false;
                }
                
                if (!LoadSHBytes(fileStream, new List<ProbeStreamingRequest>()
                    {
                        new ProbeStreamingRequest()
                        {
                            bufferOffset = 0,
                            fileOffset = fileOffsetPerComponent * 2,
                            length = bytesToLoad 
                        }
                    }, ref s_SHBb, out var shBbBytes))
                {
                    Debug.LogError("[EasyProbeStreaming](ProcessStreaming): failed to load shBb data.");
                    return false;
                }
                
                if (!LoadSHBytes(fileStream, new List<ProbeStreamingRequest>()
                    {
                        new ProbeStreamingRequest()
                        {
                            bufferOffset = 0,
                            fileOffset = fileOffsetPerComponent * 3,
                            length = bytesToLoad 
                        }
                    }, ref s_SHC, out var shCBytes))
                {
                    Debug.LogError("[EasyProbeStreaming](ProcessStreaming): failed to load shC data.");
                    return false;
                }
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
                
                if (s_EnableStreaming)
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
        
        public static void CalculateCellRange(Bounds cameraAABB, bool debugMode,
            out Vector3Int clampedCellMinWS, 
            out Vector3Int clampedCellMaxWS,
            out Vector3Int boxMinWS,
            out Vector3Int boxMaxWS)
        {
#if UNITY_EDITOR
            
            if (debugMode && EasyProbeSetup.Instance != null
                          && EasyProbeSetup.Instance.settings.sceneViewStreamingWithCustomBox)
            {
                cameraAABB = EasyProbeSetup.Instance.settings.streamingBounds;
            }
#endif
            
            int cellSize = s_Metadata.cellSize;
            var cellMin = s_Metadata.cellMin;
            var cellMax = s_Metadata.cellMax;
            
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
            UpdateDataLocationTexture(s_EasyProbeSHAr, s_SHAr);
            UpdateDataLocationTexture(s_EasyProbeSHAg, s_SHAg);
            UpdateDataLocationTexture(s_EasyProbeSHAb, s_SHAb);
            
            UpdateDataLocationTexture(s_EasyProbeSHBr, s_SHBr);
            UpdateDataLocationTexture(s_EasyProbeSHBg, s_SHBg);
            UpdateDataLocationTexture(s_EasyProbeSHBb, s_SHBb);
            
            UpdateDataLocationTexture(s_EasyProbeSHC, s_SHC);
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

            s_SHAr = null;
            s_SHAg = null;
            s_SHAb = null;
            
            s_SHBr = null;
            s_SHBg = null;
            s_SHBb = null;

            s_SHC = null;

            s_NeedReloadMetadata = true;

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