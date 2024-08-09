
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering.Universal;

namespace UnityEngine.Rendering.EasyProbeVolume
{
    public struct CellMetaData
    {
        public int cellSize;
        public int probeSpacing;
        
    }
    
    public static class EasyProbeStreaming 
    {
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
        
        // TODO
        public static Texture s_EasyProbeSHAr = null;
        public static Texture s_EasyProbeSHAg = null;
        public static Texture s_EasyProbeSHAb = null;
        public static Texture s_EasyProbeSHBr = null;
        public static Texture s_EasyProbeSHBg = null;
        public static Texture s_EasyProbeSHBb = null;
        public static Texture s_EasyProbeSHC = null;
        
        public static Vector3 s_ProbeVolumeSize;
        public static Vector4 s_ProbeVolumeWorldOffset;
        
        static void AllocEasyProbeTextureIfNeeded(ref Texture probeRT)
        {
           
        }
        
        static int EstimateMemoryCost(int width, int height, int depth, GraphicsFormat format)
        {
            int elementSize = format == GraphicsFormat.R16G16B16A16_SFloat ? 8 :
                format == GraphicsFormat.R8G8B8A8_UNorm ? 4 : 1;
            return (width * height * depth) * elementSize;
        }
        
        public static Texture CreateDataTexture(int width, int height, int depth, GraphicsFormat format, string name, bool allocateRendertexture, ref int allocatedBytes)
        {
            allocatedBytes += EstimateMemoryCost(width, height, depth, format);

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
        public static void PipelineDisposed()
        {
            CoreUtils.Destroy(s_EasyProbeSHAr);
            CoreUtils.Destroy(s_EasyProbeSHAg);
            CoreUtils.Destroy(s_EasyProbeSHAb);
            CoreUtils.Destroy(s_EasyProbeSHBr);
            CoreUtils.Destroy(s_EasyProbeSHBg);
            CoreUtils.Destroy(s_EasyProbeSHBb);
            CoreUtils.Destroy(s_EasyProbeSHC);
        }
        
        public static void UpdateCellStreaming()
        {
            // TODO: 
        }

        public static void PushRuntimeData(ScriptableRenderContext context, CameraData cameraData)
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
    }

}