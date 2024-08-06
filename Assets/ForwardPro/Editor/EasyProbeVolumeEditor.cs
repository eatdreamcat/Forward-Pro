using System;
using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEditorInternal;

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
            
            internal static readonly Color k_GizmoColorBase = new Color32(137, 222, 144, 255);
            
            internal static readonly Color k_GizmoColorCell = new Color32(50, 255, 255, 255);

            internal static readonly Color k_GizmoColorProbe = new Color32(255, 255, 255, 255);

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

        static HierarchicalBox _ShapeBox;
        static HierarchicalBox s_ShapeBox
        {
            get
            {
                if (_ShapeBox == null)
                    _ShapeBox = new HierarchicalBox(Styles.k_GizmoColorBase, Styles.k_BaseHandlesColor);
                return _ShapeBox;
            }
        }

        private static HierarchicalBox _CellBox;
        
        static HierarchicalBox s_CellBox
        {
            get
            {
                if (_CellBox == null)
                    _CellBox = new HierarchicalBox(Styles.k_GizmoColorCell);
                return _CellBox;
            }
        }


        private static HierarchicalSphere _ProbeSphere;

        private static HierarchicalSphere s_ProbeSphere
        {
            get
            {
                if (_ProbeSphere == null)
                    _ProbeSphere = new HierarchicalSphere(Styles.k_GizmoColorProbe);
                return _ProbeSphere;
            }
        }

        private static float s_ProbeRadius = 0.1f;
        private static bool s_DisplayCell = false;
        private static bool s_DisplayProbe = false;
        
        public static int GetAdjustedMultiple(int a, int b, int max)
        {
            // 计算最小的整数 n，使得 n * a >= b
            int n = (int)Math.Ceiling((double)b / a);
            int result = n * a;
        
            // 如果结果超过了最大值，则调整为小于最大值的最大倍数
            if (result > max)
            {
                result = (max / a) * a;
            }
        
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
            EasyProbeVolume.s_ProbeSpacing = EditorGUILayout.IntSlider(Styles.s_ProbeSpacing, EasyProbeVolume.s_ProbeSpacing, 1,
                EasyProbeVolume.s_MaxProbeSpacing);
            EditorGUILayout.Space();
            EasyProbeVolume.s_ProbeCellSize = EditorGUILayout.IntSlider(Styles.s_ProbeCellSize, 
                EasyProbeVolume.s_ProbeCellSize, EasyProbeVolume.s_ProbeSpacing,
                EasyProbeVolume.s_MaxProbeCellSize);
            EasyProbeVolume.s_ProbeCellSize =
                GetAdjustedMultiple(EasyProbeVolume.s_ProbeSpacing, EasyProbeVolume.s_ProbeCellSize, EasyProbeVolume.s_MaxProbeCellSize);
            
            EditorGUILayout.Space();

            s_DisplayCell = EditorGUILayout.Toggle(Styles.s_DisplayCell, s_DisplayCell);

            s_DisplayProbe = EditorGUILayout.Toggle(Styles.s_DisplayProbe, s_DisplayProbe);
            if (s_DisplayProbe)
            {
                s_ProbeRadius = EditorGUILayout.Slider(Styles.s_ProbeDrawSize, s_ProbeRadius, 0f, 10f);
            }
            
            EditorGUILayout.Space();
            
            if (GUILayout.Button("Fit to lights"))
            {
                var bounds = pv.ComputeBounds();
                pv.transform.position = bounds.center;
                serialized.volumeSize.vector3Value = 
                    Vector3.Max(bounds.size 
                                + new Vector3(EasyProbeVolume.s_ProbeSpacing,
                                    EasyProbeVolume.s_ProbeSpacing,
                                    EasyProbeVolume.s_ProbeSpacing), 
                        Vector3.zero);
            }
            
            EditorGUILayout.Space();
            if (GUILayout.Button("Place Probes"))
            {
                EasyProbeBaking.PlaceProbes();
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
                // Draw Cell
                if (s_DisplayCell)
                {
                    foreach (var cell in EasyProbeBaking.s_ProbeCells)
                    {
                        using (new Handles.DrawingScope(Matrix4x4.TRS(Vector3.zero, Quaternion.identity, Vector3.one)))
                        {
                            // Bounding box.
                            s_CellBox.center = cell.position;
                            s_CellBox.size = new Vector3(
                                EasyProbeVolume.s_ProbeCellSize,
                                EasyProbeVolume.s_ProbeCellSize,
                                EasyProbeVolume.s_ProbeCellSize
                            );
                            s_CellBox.DrawHull(EditMode.editMode == k_EditShape);
                        }
                    }
                }
            }

            {
                // Draw Probe
                if (s_DisplayProbe)
                {
                    foreach (var probe in EasyProbeBaking.s_Probes)
                    {
                        using (new Handles.DrawingScope(Matrix4x4.TRS(Vector3.zero, Quaternion.identity, Vector3.one)))
                        {
                            // Bounding box.
                            s_ProbeSphere.center = probe.position;
                            s_ProbeSphere.radius = s_ProbeRadius;
                            s_ProbeSphere.DrawHull(true);
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
