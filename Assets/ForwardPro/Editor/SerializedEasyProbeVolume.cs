using UnityEditor;

namespace UnityEngine.Rendering.EasyProbeVolume
{
    internal class SerializedEasyProbeVolume
    {
        internal SerializedProperty volumeSize;
        // internal SerializedProperty probeSpacing;
        internal SerializedProperty lightRoot;
        
        internal SerializedObject serializedObject;

        internal SerializedEasyProbeVolume(SerializedObject obj)
        {
            serializedObject = obj;

            volumeSize = serializedObject.FindProperty("volumeSize");
            // probeSpacing = serializedObject.FindProperty("probeSpacing");
            lightRoot = serializedObject.FindProperty("lightRoot");
        }

        internal void Apply()
        {
            serializedObject.ApplyModifiedProperties();
        }
    }
}