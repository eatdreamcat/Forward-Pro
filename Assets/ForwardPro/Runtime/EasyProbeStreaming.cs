
using System;
using System.IO;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering.Universal;

namespace UnityEngine.Rendering.EasyProbeVolume
{
    [StructLayout(LayoutKind.Sequential)]
    public struct EasyProbeMetaData
    {
        public Vector4 cellMinAndSpacing;
        public Vector4 cellMaxAndCellSize;
        // xyz: perVolumeAxis, w: perCellAxis
        public Vector4 probeCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct EasyCellMetaData
    {
        public Vector4 position;
        // xyz: volume index, w: flatten index
        public Vector4 cellIndex;
        // // x:l0l1 x start, y: l0l1 y start, z: l0l1 x end, w: l0l1 y end,
        // public Vector4 probeDataPerSliceLayoutL0L1;
        // public Vector4 probeDataPerSliceLayoutL2;
        // // xy: L0L1 start,end; zw: L1 start,end
        // public Vector4 probeDataSliceLayout;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct EasyProbeBandL0L1
    {
        public NativeArray<half4> shAr;
        public NativeArray<half4> shAb;
        public NativeArray<half4> shAc;
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
        public static bool s_NeedReloadMetadata = true;
        
        
        public static Vector3 s_ProbeVolumeSize;
        public static Vector4 s_ProbeVolumeWorldOffset;

        private static int s_BufferStartIndex;
        private static int s_BufferEndIndex;

        private static int s_BytesInUse = 0;
        
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
        
        static int EstimateMemoryCost(int width, int height, int depth, GraphicsFormat format)
        {
            int elementSize = format == GraphicsFormat.R16G16B16A16_SFloat ? 8 :
                format == GraphicsFormat.R8G8B8A8_UNorm ? 4 : 1;
            return (width * height * depth) * elementSize;
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

        static byte[] ReadByteFromRelativePath(string path)
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
        
        static bool LoadMetadata()
        {
            int size = Marshal.SizeOf(typeof(EasyProbeMetaData));
            var bytes = ReadByteFromRelativePath(s_MetadataPath);
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
                s_Metadata = Marshal.PtrToStructure<EasyProbeMetaData>(ptr);
                s_NeedReloadMetadata = false;
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
        
        public static void UpdateCellStreaming(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (s_NeedReloadMetadata)
            {
                if (!LoadMetadata())
                {
                    Debug.LogError("[EasyProbeStreaming](UpdateCellStreaming): load metadata error.");
                    return;
                }
                
            }

            var camera = renderingData.cameraData.camera;
            
            ProcessStreaming(camera);
            
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

        public static void ProcessStreaming(Camera camera)
        {
            var cellMin = s_Metadata.cellMinAndSpacing;
            var cellMax = s_Metadata.cellMaxAndCellSize;
            var probeSpacing = s_Metadata.cellMinAndSpacing.w;
            var cellSize = s_Metadata.cellMaxAndCellSize.w;
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
            int width = (int)probeCountPerAxie.x;
            int height = (int)probeCountPerAxie.y;
            int depth = (int)probeCountPerAxie.z;

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
                var l0l1Bytes = ReadByteFromRelativePath(s_L0L1DataPath);

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
                
                    for (int i = 0; i < l0l1HalfArray.Length; i+=3)
                    {
                        int index = i / 3;
                        s_SHAr[index] = l0l1HalfArray[i];
                        s_SHAg[index] = l0l1HalfArray[i + 1];
                        s_SHAb[index] = l0l1HalfArray[i + 2];
                    }
                }
                
                // L2
                var l2Bytes = ReadByteFromRelativePath(s_L2DataPath);

                if (l2Bytes == null)
                {
                    Debug.LogError("[EasyProbeStreaming](ProcessStreaming): failed to load l2 data.");
                    return;
                }

                {
                    using var l2Array = new NativeArray<byte>(l2Bytes.Length, Allocator.Temp,
                        NativeArrayOptions.ClearMemory);
                    l2Array.CopyFrom(l2Bytes);
                    var l2HalfArray = l2Array.Slice().SliceConvert<half4>();
                    for (int i = 0; i < l2HalfArray.Length; i+=4)
                    {
                        int index = i / 4;
                        s_SHBr[index] = l2HalfArray[i];
                        s_SHBg[index] = l2HalfArray[i + 1];
                        s_SHBb[index] = l2HalfArray[i + 2];
                        s_SHC[index] = l2HalfArray[i + 3];
                    }
                }
            }
            
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
        }
    }

}