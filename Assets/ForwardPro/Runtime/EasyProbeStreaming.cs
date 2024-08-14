
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine.Experimental.Rendering;
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
        // xyz: perVolumeAxis, w: perCellAxis
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

    public struct ProbeBytesStreaming
    {
        public int offset;
        public int length;
    }
    
    public static class EasyProbeStreaming 
    {
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
        
        public static NativeArray<half4> s_SHAr;
        public static NativeArray<half4> s_SHAg;
        public static NativeArray<half4> s_SHAb;
        public static NativeArray<half4> s_SHBr;
        public static NativeArray<half4> s_SHBg;
        public static NativeArray<half4> s_SHBb;
        public static NativeArray<half4> s_SHC;

        public static EasyProbeMetaData s_Metadata;
        public static List<EasyCellData> s_CellDatas = new List<EasyCellData>();
        public static bool s_NeedReloadMetadata = true;
        
        
        public static Vector3 s_ProbeVolumeSize;
        public static Vector4 s_ProbeVolumeWorldOffset;

        private static int s_BufferStartIndex;
        private static int s_BufferEndIndex;

        private static int s_BytesInUse = 0;

        private static List<ProbeBytesStreaming> s_L0L1Requests = new();
        private static List<ProbeBytesStreaming> s_L2Requests = new();
        private static List<byte> s_L0L1TempBuffer = new();
        private static List<byte> s_L2TempBuffer = new();
            
        private static EasyProbeSetup.MemoryBudget s_Budget;
        private static ProbeVolumeSHBands s_Bands;

        private const int k_L0L1Stride = 64 * 3;
        private const int k_L2Stride = 64 * 4;
        
        static bool AllocBufferDataIfNeeded(ref Texture probeRT, ref NativeArray<half4> sh,
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

            var totalLength = width * height * depth;
            if (sh == null || !sh.IsCreated || sh.Length != totalLength)
            {
                if (sh != null && sh.IsCreated)
                {
                    sh.Dispose();
                }

                sh = new NativeArray<half4>(totalLength, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
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

        static byte[] ReadBytesFromRelativePath(string path)
        {
            var currentScenePath = SceneManagement.SceneManager.GetActiveScene().path;
            var lastIndexOfSep = currentScenePath.LastIndexOf("/");
            currentScenePath = currentScenePath.Substring(0, lastIndexOfSep);

            var filePath = currentScenePath + path;
            
            try
            {
                byte[] bytes = File.ReadAllBytes(filePath);
                return bytes;
            }
            catch (Exception e)
            {
                Debug.LogError("[EasyProbeStreaming](ReadByteFromRelativePath):" + e.Message);
            }
            
            return null;
        }
        
        public static byte[] ReadBytesFromRelativePath(string filePath, long startPosition, int length)
        {
            try
            {
                byte[] buffer = new byte[length];
                using (FileStream fs = new FileStream(filePath, FileMode.Open, FileAccess.Read))
                {
                    fs.Seek(startPosition, SeekOrigin.Begin);
                    int bytesRead = fs.Read(buffer, 0, length);
                    if (bytesRead < length)
                    {
                        Array.Resize(ref buffer, bytesRead);
                    }
                }
                
                return buffer;
            }
            catch (Exception e)
            {
                Debug.LogError("[EasyProbeStreaming](ReadByteFromRelativePath):" + e.Message);
            }

            return null;
        }
        
        public static bool LoadMetadata(ref EasyProbeMetaData metaData, string volumeHash = "")
        {
            int size = Marshal.SizeOf(typeof(EasyProbeMetaData));
            // TODO volumeHash
            var bytes = ReadBytesFromRelativePath(s_MetadataPath);
            if (bytes.Length != size)
            { 
                Debug.LogError("[EasyProbeStreaming](LoadMetadata): Byte array size does not match the size of the structure.");
                return false;
            }

            IntPtr ptr = Marshal.AllocHGlobal(size);
            bool isSuccess = false;
            try
            {
                Marshal.Copy(bytes, 0, ptr, size);
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
            
            ProcessStreaming(camera, radius);
            
            UploadTextureData();
            
            PushRuntimeData(context, ref renderingData.cameraData);
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
            var width = (metaData.probeCountPerCellAxis - 1) * (boxMax.x - boxMin.x) + 1;
            var height = (metaData.probeCountPerCellAxis - 1) * (boxMax.y - boxMin.y) + 1;
            var depth = (metaData.probeCountPerCellAxis - 1) * (boxMax.z - boxMin.z) + 1;
            return new Vector3Int(width, height, depth);
        }

        static void DoStreaming(Camera camera, float radius)
        {
             var cameraAABB = CalculateSphereAABB(CalculateCameraFrustumSphere(ref s_Metadata, camera, radius));
            CalculateCellRange(cameraAABB, out var clampedCellMin, out var clampedCellMax, out var boxMin, out var boxMax);
            
            s_ProbeVolumeSize = boxMax - boxMin;
            s_ProbeVolumeWorldOffset = new Vector4(boxMin.x, boxMin.y, boxMin.z, 1.0f);
            
            s_L0L1TempBuffer.Clear();
            s_L0L1Requests.Clear();
            
            s_L2TempBuffer.Clear();
            s_L2Requests.Clear();
            
            var probeCountPerSlice = s_Metadata.probeCountPerVolumeAxis.x * s_Metadata.probeCountPerVolumeAxis.y; 
            var cellCountPerAxis = (clampedCellMax - clampedCellMin) / s_Metadata.cellSize;
            var probeCountPerAxis = (s_Metadata.probeCountPerCellAxis - 1) * cellCountPerAxis + Vector3Int.one;
            var l0l1ProbeDataLineLength = probeCountPerAxis.x * k_L0L1Stride;
            
            var cellOffset = (clampedCellMin - s_Metadata.cellMin) / s_Metadata.cellSize;
            var probeStartAtX = cellOffset.x * (s_Metadata.probeCountPerCellAxis - 1)
                                + cellOffset.y * s_Metadata.probeCountPerVolumeAxis.x
                                + cellOffset.z * probeCountPerSlice;
            
            for (int slice = 0; slice < probeCountPerAxis.z; ++slice)
            {
                for (int line = 0; line < probeCountPerAxis.y; ++line)
                {
                    var l0l1ProbeDataLineStart = (probeStartAtX + slice * probeCountPerSlice + line * probeCountPerAxis.y) * k_L0L1Stride;
                    s_L0L1Requests.Add(new ProbeBytesStreaming()
                    {
                        offset = l0l1ProbeDataLineStart,
                        length = l0l1ProbeDataLineLength
                    });
                }
            }

            #region Calculate Buffer Size

            var size = CalculateBufferSize(ref s_Metadata, boxMin, boxMax);
            var width = size.x;
            var height = size.y;
            var depth = size.z;
            
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
            while (s_L0L1Requests.Count < 0)
            {
                var request = s_L0L1Requests[0];
                var bytes = ReadBytesFromRelativePath(s_L0L1DataPath, request.offset, request.length);
                
                if (bytes == null)
                {
                    Debug.LogError($"[EasyProbeStreaming](ProcessStreaming): failed to load l0l1 data at offset {request.offset}");
                    return;
                }
                
                s_L0L1TempBuffer.AddRange(bytes);
                s_L0L1Requests.RemoveAt(0);
            }
            
            {
                using var l0l1Array = new NativeArray<byte>(s_L0L1TempBuffer.Count, Allocator.Temp,
                    NativeArrayOptions.UninitializedMemory);
                l0l1Array.CopyFrom(s_L0L1TempBuffer.ToArray());
                var l0l1HalfArray = l0l1Array.Slice().SliceConvert<half4>();
                    
                for (int i = 0; i < l0l1HalfArray.Length; i += 3)
                {
                    int index = i / 3;
                    s_SHAr[index] = l0l1HalfArray[i];
                    s_SHAg[index] = l0l1HalfArray[i + 1];
                    s_SHAb[index] = l0l1HalfArray[i + 2];
                }
            }
            
            #endregion
        }
        
        public static void ProcessStreaming(Camera camera, float radius)
        {
            
            #region TestCode
            
            var cellMin = s_Metadata.cellMin;
            var cellMax = s_Metadata.cellMax;
            var probeSpacing = s_Metadata.probeSpacing;
            var cellSize = s_Metadata.cellSize;
            var halfSize = probeSpacing / 2.0f;
            
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
                // L0L1
                var l0l1Bytes = ReadBytesFromRelativePath(s_L0L1DataPath);
            
                if (l0l1Bytes == null)
                {
                    Debug.LogError("[EasyProbeStreaming](ProcessStreaming): failed to load l0l1 data.");
                    return;
                }
            
                {
                    using var l0l1Array = new NativeArray<byte>(l0l1Bytes.Length, Allocator.Temp,
                        NativeArrayOptions.UninitializedMemory);
                    l0l1Array.CopyFrom(l0l1Bytes);
                    var l0l1HalfArray = l0l1Array.Slice().SliceConvert<half4>();
                    
                    for (int i = 0; i < l0l1HalfArray.Length; i += 3)
                    {
                        int index = i / 3;
                        s_SHAr[index] = l0l1HalfArray[i];
                        s_SHAg[index] = l0l1HalfArray[i + 1];
                        s_SHAb[index] = l0l1HalfArray[i + 2];
                    }
                }
            
                // L2
                var l2Bytes = ReadBytesFromRelativePath(s_L2DataPath);
            
                if (l2Bytes == null)
                {
                    Debug.LogError("[EasyProbeStreaming](ProcessStreaming): failed to load l2 data.");
                    return;
                }
            
                {
                    using var l2Array = new NativeArray<byte>(l2Bytes.Length, Allocator.Temp,
                        NativeArrayOptions.UninitializedMemory);
                    l2Array.CopyFrom(l2Bytes);
                    var l2HalfArray = l2Array.Slice().SliceConvert<half4>();
                    for (int i = 0; i < l2HalfArray.Length; i += 4)
                    {
                        int index = i / 4;
                        s_SHBr[index] = l2HalfArray[i];
                        s_SHBg[index] = l2HalfArray[i + 1];
                        s_SHBb[index] = l2HalfArray[i + 2];
                        s_SHC[index] = l2HalfArray[i + 3];
                    }
                }
            }
            
            
            #endregion
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
            out Vector3Int clampedCellMin, 
            out Vector3Int clampedCellMax,
            out Vector3Int boxMin,
            out Vector3Int boxMax)
        {
            int cellSize = s_Metadata.cellSize;
            var cellMin = s_Metadata.cellMin;
            var cellMax = s_Metadata.cellMax;

            var boxCenter = GetCellIndexStart(cameraAABB.center, cellSize) * cellSize 
                            + new Vector3Int(cellSize / 2, cellSize / 2, cellSize / 2);
            
            boxMin = GetCellIndexStart(cameraAABB.min, cellSize) * cellSize;
            boxMax = GetCellIndexEnd(cameraAABB.max, cellSize) * cellSize;
            var maxExtend = Max3(boxMax - boxCenter, boxCenter - boxMin);

            boxMin = boxCenter - maxExtend;
            boxMax = boxCenter + maxExtend;
                
            clampedCellMin = Max3(boxMin, cellMin);
            clampedCellMax = Min3(boxMax, cellMax);
            
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

            if (s_SHAr.IsCreated)
            {
                s_SHAr.Dispose();
                s_SHAr = default;
            }

            if (s_SHAg.IsCreated)
            {
                s_SHAg.Dispose();
                s_SHAg = default;
            }

            if (s_SHAb.IsCreated)
            {
                s_SHAb.Dispose();
                s_SHAb = default;
            }

            if (s_SHBr.IsCreated)
            {
                s_SHBr.Dispose();
                s_SHBr = default;
            }

            if (s_SHBg.IsCreated)
            {
                s_SHBg.Dispose();
                s_SHBg = default;
            }

            if (s_SHBb.IsCreated)
            {
                s_SHBb.Dispose();
                s_SHBb = default;
            }

            if (s_SHC.IsCreated)
            {
                s_SHC.Dispose();
                s_SHC = default;
            }

            s_NeedReloadMetadata = true;
            
            // s_FileStream?.Dispose();
            
        }
    }

}