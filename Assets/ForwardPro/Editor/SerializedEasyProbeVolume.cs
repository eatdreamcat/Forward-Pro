using UnityEditor;

namespace UnityEngine.Rendering.EasyProbeVolume
{
    internal class SerializedEasyProbeVolume
    {
        internal SerializedProperty volumeSize;
        
        internal SerializedProperty lightRoot;

        internal SerializedProperty streamingCamera;
        
        internal SerializedObject serializedObject;

        internal SerializedEasyProbeVolume(SerializedObject obj)
        {
            serializedObject = obj;

            volumeSize = serializedObject.FindProperty("volumeSize");
            
            lightRoot = serializedObject.FindProperty("lightRoot");

            streamingCamera = serializedObject.FindProperty("streamingCamera");
            
        }

        internal void Apply()
        {
            serializedObject.ApplyModifiedProperties();
        }
    }
}