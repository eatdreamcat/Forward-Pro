using System;
using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEditorInternal;
using UnityEngine.SceneManagement;

namespace UnityEngine.Rendering.EasyProbeVolume
{
    [CanEditMultipleObjects]
    [CustomEditor(typeof(EasyProbeVolume))]
    public class EasyProbeVolumeEditor : Editor
    {
        internal const EditMode.SceneViewEditMode k_EditShape = EditMode.SceneViewEditMode.ReflectionProbeBox;
        internal static class Styles
        {
            internal static readonly GUIContent s_Size = new GUIContent("Size",
                "Modify the size of this Easy Probe Volume. This is unaffected by the GameObject's Transform's Scale property.");
            internal static readonly GUIContent s_ProbeSpacing = new GUIContent("Probe Spacing",
                "Modify the space of probe placement. Note that this value is global.");
            internal static readonly GUIContent s_LightRoot = new GUIContent("Light Root",
                "Modify the root of lights to baking.");

            internal static readonly GUIContent s_ProbeCellSize = new GUIContent("Probe Cell Size");

            internal static readonly GUIContent s_DisplayCell = new GUIContent("Display Cell");
            internal static readonly GUIContent s_DisplayProbe = new GUIContent("Display Probe");
            internal static readonly GUIContent s_ProbeDrawSize = new GUIContent("Probe Draw Size");
            
            internal static readonly GUIContent s_SampleDirDensity = new GUIContent("Sample Direction Density");
            internal static readonly GUIContent s_SampleCount = new GUIContent("Sample Count");
            internal static readonly GUIContent s_PointLightAttenuationConstant = new GUIContent("Point Light Attenuation Constant");
            internal static readonly GUIContent s_ProbeDebugType = new GUIContent("DebugDraw");
            internal static readonly GUIContent s_ProbeVolumeNoise = new GUIContent("Probe Sampling Noise");
            internal static readonly GUIContent s_ProbeVolumeIntensity = new GUIContent("Probe Shading Intensity");

            internal static readonly GUIContent s_DebugStreaming = new GUIContent("Debug Streaming");
            internal static readonly GUIContent s_StreamingCamera = new GUIContent("Streaming Camera");
            internal static readonly GUIContent s_StreamingMemoryBudget = new GUIContent("Memory Budget");
            internal static readonly GUIContent s_StreamingRadiusScale = new GUIContent("Radius Scale");
            
            internal static readonly Color k_GizmoColorBase = new Color32(137, 222, 144, 255);
            
            internal static readonly Color k_GizmoColorCell = new Color32(50, 255, 255, 255);

            internal static readonly Color k_GizmoColorBoundingSphere = new Color32(255, 155, 100, 255);
            
            internal static readonly Color k_GizmoColorBoundingBox = new Color32(255, 255, 0, 255);
            internal static readonly Color k_GizmoColorActualVolumeBox = new Color32(255, 0, 0, 255);
            internal static readonly Color k_GizmoColorValidVolumeBox = new Color32(50, 50, 255, 255);

            internal static readonly Color[] k_BaseHandlesColor = new Color[]
            {
                k_GizmoColorBase,
                k_GizmoColorBase,
                k_GizmoColorBase,
                k_GizmoColorBase,
                k_GizmoColorBase,
                k_GizmoColorBase
            };
        }
        
        public enum SampleCount
        {
            _1 = 1,
            _4 = 4,
            _8 = 8,
            _16 = 16,
            _32 = 32,
            _64 = 64,
            _128 = 128,
            _256 = 256,
            _512 = 512,
            _1024 = 1024,
            _2048 = 2048
        }

        public enum ProbeDebug
        {
            Position,
            Attenuation,
            Visibility,
            Diffuse
        }
        
        private SampleCount m_SampleCount = SampleCount._4;
        
        private static List<EasyProbeLightSource> s_LightSources = new();
        private static List<Light> s_Lights = new();
        static HierarchicalBox _ShapeBox;
        static HierarchicalBox s_ShapeBox
        {
            get
            {
                if (_ShapeBox == null)
                {
                    _ShapeBox = new HierarchicalBox(Styles.k_GizmoColorBase, Styles.k_BaseHandlesColor);
                }
                return _ShapeBox;
            }
        }

        private static HierarchicalBox _CellBox;
        
        static HierarchicalBox s_CellBox
        {
            get
            {
                if (_CellBox == null)
                {
                    _CellBox = new HierarchicalBox(Styles.k_GizmoColorCell);
                }
                return _CellBox;
            }
        }
        
        private static HierarchicalBox _BoundingBox;
        
        static HierarchicalBox s_BoundingBox
        {
            get
            {
                if (_BoundingBox == null)
                {
                    _BoundingBox = new HierarchicalBox(Styles.k_GizmoColorBoundingBox);
                }
                return _BoundingBox;
            }
        }
        
        private static HierarchicalBox _VolumeBox;
        
        static HierarchicalBox s_VolumeBox
        {
            get
            {
                if (_VolumeBox == null)
                {
                    _VolumeBox = new HierarchicalBox(Styles.k_GizmoColorActualVolumeBox);
                }
                return _VolumeBox;
            }
        }
        
        private static HierarchicalBox _ValidVolumeBox;
        
        static HierarchicalBox s_ValidVolumeBox
        {
            get
            {
                if (_ValidVolumeBox == null)
                {
                    _ValidVolumeBox = new HierarchicalBox(Styles.k_GizmoColorValidVolumeBox);
                }
                return _ValidVolumeBox;
            }
        }

        private static HierarchicalSphere _BoundingSphere;

        private static HierarchicalSphere s_BoundingSphere
        {
            get
            {
                if (_BoundingSphere == null)
                {
                    _BoundingSphere = new HierarchicalSphere(Styles.k_GizmoColorBoundingSphere);
                }
                return _BoundingSphere;
            }
        }


        private static EasyProbeSphere _ProbeSphere;

        private static EasyProbeSphere s_ProbeSphere
        {
            get
            {
                if (_ProbeSphere == null)
                    _ProbeSphere = new EasyProbeSphere();
                return _ProbeSphere;
            }
        }

        private static float s_ProbeRadius = 0.1f;
        private static bool s_DisplayCell = false;
        private static bool s_DisplayProbe = false;
        private static bool s_DebugStreaming = false;
        private static float s_RadiusScale = 1f;
        private static ProbeDebug s_DebugDraw = ProbeDebug.Diffuse;

        private static EasyProbeMetaData s_EasyProbeMetadata;
        private static bool s_NeedReloadMetadata = true;
        
        public static int GetAdjustedMultiple(int a, int b, int max)
        {
            int n = (int)Math.Ceiling((double)b / a);
            int result = n * a;
            
            if (result > max)
            {
                result = (max / a) * a;
            }

            result += (result % 2);
            return result;
        }
        
        SerializedEasyProbeVolume m_SerializedProbeVolume;

        private void OnEnable()
        {
            m_SerializedProbeVolume = new SerializedEasyProbeVolume(serializedObject);
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            Draw(m_SerializedProbeVolume, this);
            m_SerializedProbeVolume.Apply();
        }
        
        private void Draw(SerializedEasyProbeVolume serialized, Editor owner)
        {
            EasyProbeVolume pv = (serialized.serializedObject.targetObject as EasyProbeVolume);
            
            EditorGUILayout.Space();
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(serialized.lightRoot, Styles.s_LightRoot);
            EditorGUILayout.Space();
            EditorGUILayout.PropertyField(serialized.volumeSize, Styles.s_Size);
            if (EditorGUI.EndChangeCheck())
            {
                serialized.volumeSize.vector3Value = Vector3.Max(serialized.volumeSize.vector3Value, Vector3.zero);
            }
            
            EditorGUILayout.Space();
            
            // serialized.probeSpacing.intValue = EditorGUILayout.IntSlider(Styles.s_ProbeSpacing, serialized.probeSpacing.intValue, 1,
            //     EasyProbeVolume.s_MaxProbeSpacing);
            EasyProbeBaking.s_ProbeSpacing = EditorGUILayout.IntSlider(Styles.s_ProbeSpacing, EasyProbeBaking.s_ProbeSpacing, 1,
                EasyProbeBaking.s_MaxProbeSpacing);
            EditorGUILayout.Space();
            EasyProbeBaking.s_ProbeCellSize = EditorGUILayout.IntSlider(Styles.s_ProbeCellSize, 
                EasyProbeBaking.s_ProbeCellSize, EasyProbeBaking.s_ProbeSpacing,
                EasyProbeBaking.s_MaxProbeCellSize);
            EasyProbeBaking.s_ProbeCellSize = GetAdjustedMultiple(EasyProbeBaking.s_ProbeSpacing,
                EasyProbeBaking.s_ProbeCellSize, EasyProbeBaking.s_MaxProbeCellSize);
            EasyProbeVolume.s_EasyPVSamplingNoise = EditorGUILayout.Slider(Styles.s_ProbeVolumeNoise,
                EasyProbeVolume.s_EasyPVSamplingNoise, 0.0f, 1.0f);
            EasyProbeVolume.s_EasyProbeIntensity = EditorGUILayout.Slider(Styles.s_ProbeVolumeIntensity,
                EasyProbeVolume.s_EasyProbeIntensity,
                0.0f, 5.0f);
            
            EditorGUILayout.Space();

            // m_SampleDirDensity = (SampleDirDensity)EditorGUILayout.EnumPopup(Styles.s_SampleDirDensity, m_SampleDirDensity);
            m_SampleCount = (SampleCount)EditorGUILayout.EnumPopup(Styles.s_SampleCount, m_SampleCount);
            EasyProbeBaking.s_PointAttenConstantK =
                EditorGUILayout.Slider(Styles.s_PointLightAttenuationConstant, EasyProbeBaking.s_PointAttenConstantK,
                    0.01f, 0.1f);
            EditorGUILayout.Space();

            s_DisplayCell = EditorGUILayout.Toggle(Styles.s_DisplayCell, s_DisplayCell);

            s_DisplayProbe = EditorGUILayout.Toggle(Styles.s_DisplayProbe, s_DisplayProbe);
            if (s_DisplayProbe)
            {
                EditorGUI.indentLevel++;
                s_ProbeRadius = EditorGUILayout.Slider(Styles.s_ProbeDrawSize, s_ProbeRadius, 0f, 1f);
                s_DebugDraw = (ProbeDebug)EditorGUILayout.EnumPopup(Styles.s_ProbeDebugType, s_DebugDraw);
                EditorGUI.indentLevel--;
            }
            

            s_DebugStreaming = EditorGUILayout.Toggle(Styles.s_DebugStreaming, s_DebugStreaming);
            if (s_DebugStreaming)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(serialized.streamingCamera, Styles.s_StreamingCamera);
                s_RadiusScale = EditorGUILayout.Slider(Styles.s_StreamingRadiusScale, s_RadiusScale, 0.01f, 1.0f);
                if (EasyProbeSetup.Instance != null)
                {
                    EasyProbeSetup.Instance.settings.budget =
                        (EasyProbeSetup.MemoryBudget)EditorGUILayout.EnumPopup(
                            Styles.s_StreamingMemoryBudget,
                            EasyProbeSetup.Instance.settings.budget);
                }
                EditorGUI.indentLevel--;
            }
            
            EditorGUILayout.Space();
            
            if (GUILayout.Button("Fit to lights"))
            {
                var bounds = pv.ComputeBounds();
                pv.transform.position = bounds.center;
                serialized.volumeSize.vector3Value = 
                    Vector3.Max(bounds.size, 
                        Vector3.zero);
            }
            
            EditorGUILayout.Space();

            if (GUILayout.Button("Check Collider"))
            {
                CheckCollider();
            }
            
            EditorGUILayout.Space();
            if (GUILayout.Button("Bake"))
            {
                s_NeedReloadMetadata = true;
                s_LightSources.Clear();
                s_Lights.Clear();
                pv.lightRoot.GetComponentsInChildren(s_Lights);
                foreach (var light in s_Lights)
                {
                    if (light.type != LightType.Point)
                    {
                        // TODO: currently only support point light
                        continue;
                    }
                    
                    s_LightSources.Add(new EasyProbeLightSource(
                        new Bounds(light.transform.position, new Vector3(light.range, light.range, light.range)),
                        light));
                }
                
                EasyProbeBaking.Bake(s_LightSources, (int)m_SampleCount);
            }
        }

        static bool MeshRendererIntersectVolume(Bounds meshBounds)
        {
            foreach (var volume in EasyProbeVolume.s_ProbeVolumes)
            {
                if (meshBounds.Intersects(new Bounds(volume.transform.position, volume.volumeSize)))
                {
                    return true;
                }
            }

            return false;
        }
        
        static void CheckCollider()
        {
            var scene = SceneManager.GetActiveScene();

            void SetColliderIfNeeded(GameObject gameObject)
            {
                var meshRenderers = gameObject.GetComponentsInChildren<MeshRenderer>();
                foreach (var meshRenderer in meshRenderers)
                {
                    meshRenderer.receiveGI = ReceiveGI.LightProbes;
                    if (MeshRendererIntersectVolume(meshRenderer.bounds))
                    {
                        var meshCollider = meshRenderer.GetComponent<MeshCollider>();
                        if (meshCollider == null)
                        {
                            meshCollider = meshRenderer.gameObject.AddComponent<MeshCollider>();
                            meshCollider.sharedMesh = meshRenderer.GetComponent<MeshFilter>().sharedMesh;
                        }
                    }
                }
            }

            var roots = scene.GetRootGameObjects();
            foreach (var rootGo in roots)
            {
                SetColliderIfNeeded(rootGo);
            }
            
        }

        static void DrawCell(Vector3Int cellBoxMin, Vector3Int cellBoxMax)
        {
            using (new Handles.DrawingScope(Matrix4x4.TRS(Vector3.zero, Quaternion.identity,
                       Vector3.one)))
            {
                var step = s_EasyProbeMetadata.cellSize;
                for (int x = cellBoxMin.x; x < cellBoxMax.x; x+=step)
                {
                    for (int y = cellBoxMin.y; y < cellBoxMax.y; y+=step)
                    {
                        for (int z = cellBoxMin.z; z < cellBoxMax.z; z+=step)
                        {
                            var position = new Vector3Int(
                                x  + step / 2,
                                y  + step / 2,
                                z  + step / 2
                            );
                                       
                            s_CellBox.center = position;
                            s_CellBox.size = new Vector3(
                                s_EasyProbeMetadata.cellSize,
                                s_EasyProbeMetadata.cellSize,
                                s_EasyProbeMetadata.cellSize
                            );
                            s_CellBox.DrawHull(false);
                                       
                        }
                    }
                }
            }
        }
        
        [DrawGizmo(GizmoType.InSelectionHierarchy)]
        static void DrawGizmosSelected(EasyProbeVolume probeVolume, GizmoType gizmoType)
        {
            using (new Handles.DrawingScope(Matrix4x4.TRS(probeVolume.transform.position, Quaternion.identity, Vector3.one)))
            {
                // Bounding box.
                s_ShapeBox.center = Vector3.zero;
                s_ShapeBox.size = probeVolume.volumeSize;
                s_ShapeBox.DrawHull(EditMode.editMode == k_EditShape);
            }
            
            {
                // Draw Probe
                if (s_DisplayProbe)
                {
                    foreach (var probe in EasyProbeBaking.s_Probes)
                    {
                        using (new Handles.DrawingScope(Matrix4x4.TRS(Vector3.zero, Quaternion.identity, Vector3.one)))
                        {
                            s_ProbeSphere.center = probe.position;
                            s_ProbeSphere.radius = s_ProbeRadius;
                            s_ProbeSphere.DrawProbe(probe, probeVolume, s_DebugDraw);
                        }
                    }
                }
            }
            
            {
                // Draw Streaming Cell
                if (s_DebugStreaming && probeVolume.streamingCamera != null && EasyProbeSetup.Instance != null)
                {
                    if (s_NeedReloadMetadata)
                    {
                        if (EasyProbeStreaming.LoadMetadata(ref s_EasyProbeMetadata))
                        {
                            s_NeedReloadMetadata = false;
                        }
                    }
                    
                    float radius = 0;
                    switch (EasyProbeSetup.Instance.settings.budget)
                    {
                        case EasyProbeSetup.MemoryBudget.Low:
                            radius = EasyProbeSetup.k_BoundingRadiusLow;
                            break;
                        case EasyProbeSetup.MemoryBudget.Medium:
                            radius = EasyProbeSetup.k_BoundingRadiusMedium;
                            break;
                        case EasyProbeSetup.MemoryBudget.High:
                            radius = EasyProbeSetup.k_BoundingRadiusHigh;
                            break;
                    }
                    var boundingSphere = 
                        EasyProbeStreaming.CalculateCameraFrustumSphere(ref s_EasyProbeMetadata, probeVolume.streamingCamera, radius * s_RadiusScale);
                    var sphereAABB = EasyProbeStreaming.CalculateSphereAABB(boundingSphere);

                    using (new Handles.DrawingScope(Matrix4x4.TRS(Vector3.zero, Quaternion.identity, Vector3.one)))
                    {
                        s_BoundingSphere.radius = boundingSphere.w;
                        s_BoundingSphere.center = new Vector3(boundingSphere.x, boundingSphere.y, boundingSphere.z);
                        s_BoundingSphere.DrawHull(false);

                        s_BoundingBox.center = sphereAABB.center;
                        s_BoundingBox.size = sphereAABB.size;
                        s_BoundingBox.DrawHull(false);

                    }

                    {
                        // Draw Streaming Cells
                        if (!s_NeedReloadMetadata)
                        {
                            EasyProbeStreaming.CalculateCellRange(sphereAABB, out var cellBoxMin, out var cellBoxMax
                                , out var volumeMin, out var volumeMax);
                            
                            if (s_DisplayCell)
                            {
                                DrawCell(cellBoxMin, cellBoxMax);
                            }
                            
                            using (new Handles.DrawingScope(Matrix4x4.TRS(Vector3.zero, Quaternion.identity,
                                       Vector3.one)))
                            {
                                var volume = volumeMax + volumeMin;
                                s_VolumeBox.center = new Vector3(volume.x, volume.y, volume.z) / 2.0f;
                                s_VolumeBox.size = volumeMax - volumeMin;
                                s_VolumeBox.DrawHull(false);
                            }
                        }
                    }

                }
                else
                {
                    {
                        // Draw All Cells
                        if (s_DisplayCell && !s_NeedReloadMetadata)
                        {
                            DrawCell(s_EasyProbeMetadata.cellMin, s_EasyProbeMetadata.cellMax);
                        }
                    }
                }
            }
           
        }

        
        protected void OnSceneGUI()
        {
            EasyProbeVolume probeVolume = target as EasyProbeVolume;
            
            //important: if the origin of the handle's space move along the handle,
            //handles displacement will appears as moving two time faster.
            using (new Handles.DrawingScope(Matrix4x4.TRS(Vector3.zero, Quaternion.identity, Vector3.one)))
            {
                //contained must be initialized in all case
                s_ShapeBox.center = probeVolume.transform.position;
                s_ShapeBox.size = probeVolume.volumeSize;

                s_ShapeBox.monoHandle = false;
                EditorGUI.BeginChangeCheck();
                s_ShapeBox.DrawHandle();
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObjects(new Object[] { probeVolume, probeVolume.transform }, "Change Adaptive Probe Volume Bounding Box");

                    probeVolume.volumeSize = s_ShapeBox.size;
                    Vector3 delta = s_ShapeBox.center - probeVolume.transform.position;
                    probeVolume.transform.position += delta; 
                }
            }
        }
        
    }
}
