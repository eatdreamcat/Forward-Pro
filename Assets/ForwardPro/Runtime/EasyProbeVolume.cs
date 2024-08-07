
using System.Collections.Generic;
using UnityEditor;

namespace UnityEngine.Rendering.EasyProbeVolume
{
    [ExecuteAlways]
    [AddComponentMenu("Rendering/Easy Probe Volume")]
    public class EasyProbeVolume : MonoBehaviour
    {
        public static int s_ProbeSpacing = 2;
        public static int s_ProbeCellSize = 6;
        public static float s_PointAttenConstantK = 0.1f;
        
        public static List<EasyProbeVolume> s_ProbeVolumes = new();
        
        private List<Light> m_Lights = new();
        
        public static int s_MaxProbeSpacing = 10;
        public static int s_MaxProbeCellSize = 30;
        
        public Vector3 volumeSize = new Vector3(10, 10, 10);
        // [HideInInspector, Min(1)] public int probeSpacing = 1;

        [HideInInspector] public Transform lightRoot = null;

        public Vector3 Min
        {
            get
            {
                return transform.position - volumeSize / 2;
            }
        }

        public Vector3 Max
        {
            get
            {
                return transform.position + volumeSize / 2;
            }
        }

        public bool Valid => volumeSize is { x: > 0, y: > 0, z: > 0 };
        
        private void OnEnable()
        {
            s_ProbeVolumes.Add(this);
        }

        private void OnDisable()
        {
            s_ProbeVolumes.Remove(this);
        }

        private void OnDestroy()
        {
            s_ProbeVolumes.Remove(this);
        }

        public Bounds ComputeBounds()
        {
            Bounds bounds = new Bounds();

            if (lightRoot is null)
            {
                EditorUtility.DisplayDialog("Error", "Probe Volume's light root is null.", "ok");
            }
            
            bool foundABound = false;

            void ExpandBounds(Bounds bound)
            {
                if (!foundABound)
                {
                    bounds = bound;
                    foundABound = true;
                }
                else
                {
                    bounds.Encapsulate(bound);
                }
            }
            
            m_Lights.Clear();
            lightRoot.GetComponentsInChildren(m_Lights);
            foreach (var light in m_Lights)
            {
                
                if (CheckLightSupported(light))
                
                ExpandBounds(new Bounds(light.transform.position, new Vector3(light.range, light.range, light.range)));
            }
            
            return bounds;
        }
        
        public static bool CheckLightSupported(Light light)
        {
            return light.type == LightType.Point && light.lightmapBakeType != LightmapBakeType.Realtime;
        }
        
    }
}
