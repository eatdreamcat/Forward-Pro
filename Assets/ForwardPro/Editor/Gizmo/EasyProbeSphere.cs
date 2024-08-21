using Unity.Collections;
using UnityEditor;

namespace UnityEngine.Rendering.EasyProbeVolume
{
    public class EasyProbeSphere 
    {
        static Material k_Material_Cache;
        static Material k_Material => (k_Material_Cache == null || k_Material_Cache.Equals(null) ? (k_Material_Cache = new Material(Shader.Find("Hiden/EasyProbeVolume/ProbePreview"))) : k_Material_Cache);
        static Mesh k_MeshSphere_Cache;
        static Mesh k_MeshSphere => k_MeshSphere_Cache == null || k_MeshSphere_Cache.Equals(null) ? (k_MeshSphere_Cache = Resources.GetBuiltinResource<Mesh>("New-Sphere.fbx")) : k_MeshSphere_Cache;

        Material m_Material;
        Material material => m_Material == null || m_Material.Equals(null)
            ? (m_Material = new Material(k_Material))
            : m_Material;
        
        /// <summary>The position of the center of the box in Handle.matrix space.</summary>
        public Vector3 center { get; set; }

        /// <summary>The size of the box in Handle.matrix space.</summary>
        public float radius { get; set; }
        
        public void DrawProbe(ref NativeArray<float> probeAtten,
            ref NativeArray<float> probeVisibility,
            ref NativeArray<float> coefficients,
            int probeIndex,
            EasyProbeVolume volume, EasyProbeVolumeEditor.ProbeDebug probeDebug)
        {
            EasyProbeRenderingUtils.PackAndPushCoefficients(material, volume, ref probeAtten,
                 ref probeVisibility,
                 ref coefficients,
                 probeIndex);
            switch (probeDebug)
            {
                case EasyProbeVolumeEditor.ProbeDebug.Diffuse:
                    material.DisableKeyword("_Position");
                    material.DisableKeyword("_Attenuation");
                    material.DisableKeyword("_Visibility");
                    material.EnableKeyword("_Diffuse");
                    break;
                case EasyProbeVolumeEditor.ProbeDebug.Attenuation:
                    material.DisableKeyword("_Position");
                    material.DisableKeyword("_Diffuse");
                    material.DisableKeyword("_Visibility");
                    material.EnableKeyword("_Attenuation");
                    break;
                case EasyProbeVolumeEditor.ProbeDebug.Position:
                    material.DisableKeyword("_Attenuation");
                    material.DisableKeyword("_Diffuse");
                    material.DisableKeyword("_Visibility");
                    material.EnableKeyword("_Position");
                    break;
                case EasyProbeVolumeEditor.ProbeDebug.Visibility:
                    material.DisableKeyword("_Attenuation");
                    material.DisableKeyword("_Diffuse");
                    material.DisableKeyword("_Position");
                    material.EnableKeyword("_Visibility");
                    break;
            }
            material.SetPass(0);
            Matrix4x4 drawMatrix = Matrix4x4.TRS(center, Quaternion.identity, Vector3.one * radius * 2f);
            Graphics.DrawMeshNow(k_MeshSphere, drawMatrix);
        }
    }
}