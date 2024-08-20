using System;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace UnityEngine.Rendering.EasyProbeVolume
{
    public class EasyProbeSetup : ScriptableRendererFeature
    {
        private static EasyProbeSetup s_Instance;
        public static EasyProbeSetup Instance => s_Instance;

        public enum SHBand
        {
            L0L1,
            L2
        }

        public enum MemoryBudget
        {
            Low,
            Medium,
            High
        }

        // TODO: test
        public static int k_BoundingRadiusLow = 5;
        public static int k_BoundingRadiusMedium = 10;
        public static int k_BoundingRadiusHigh = 20;

        [Serializable]
        public class EasyProbeSettings
        {
            public SHBand band;
            public MemoryBudget budget;
            public bool enableStreaming;

#if UNITY_EDITOR
            [HideInInspector]
            public bool sceneViewStreamingWithCustomBox;
            [HideInInspector]
            public Bounds streamingBounds = new (Vector3.zero, Vector3.one);
#endif
        }

        class EasyProbeSetupPass : ScriptableRenderPass
        {
            public static int _EasyProbeToggle = Shader.PropertyToID("_EasyProbeToggle");
            public EasyProbeSettings settings;

            // This method is called before executing the render pass.
            // It can be used to configure render targets and their clear state. Also to create temporary render target textures.
            // When empty this render pass will render to the active camera render target.
            // You should never call CommandBuffer.SetRenderTarget. Instead call <c>ConfigureTarget</c> and <c>ConfigureClear</c>.
            // The render pipeline will ensure target setup and clearing happens in a performant manner.
            public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
            {
            }

            // Here you can implement the rendering logic.
            // Use <c>ScriptableRenderContext</c> to issue drawing commands or execute command buffers
            // https://docs.unity3d.com/ScriptReference/Rendering.ScriptableRenderContext.html
            // You don't have to call ScriptableRenderContext.submit, the render pipeline will call it at specific points in the pipeline.
            public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
            {
                switch (settings.budget)
                {
                    case MemoryBudget.Low:
                        EasyProbeStreaming.UpdateCellStreaming(context, ref renderingData, settings, k_BoundingRadiusLow);
                        break;
                    case MemoryBudget.Medium:
                        EasyProbeStreaming.UpdateCellStreaming(context, ref renderingData, settings, k_BoundingRadiusMedium);
                        break;
                    case MemoryBudget.High:
                        EasyProbeStreaming.UpdateCellStreaming(context, ref renderingData, settings, k_BoundingRadiusHigh);
                        break;
                }
            }

            // Cleanup any allocated resources that were created during the execution of this render pass.
            public override void OnCameraCleanup(CommandBuffer cmd)
            {
            }
        }

        EasyProbeSetupPass m_ScriptablePass;

        public EasyProbeSettings settings = new EasyProbeSettings();

        /// <inheritdoc/>
        public override void Create()
        {
            s_Instance = this;
            m_ScriptablePass = new EasyProbeSetupPass();
            m_ScriptablePass.settings = settings;
            // Configures where the render pass should be injected.
            m_ScriptablePass.renderPassEvent = RenderPassEvent.BeforeRendering;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            EasyProbeStreaming.SetMetadataDirty();
        }

        // Here you can inject one or multiple render passes in the renderer.
        // This method is called when setting up the renderer once per-camera.
        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            Shader.SetGlobalFloat(EasyProbeSetupPass._EasyProbeToggle, 0.0f);
            if (EasyProbeVolume.s_ProbeVolumes.Count > 0)
            {
                renderer.EnqueuePass(m_ScriptablePass);
            }
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);

            EasyProbeStreaming.Dispose();

            s_Instance = null;
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }
    }

}

