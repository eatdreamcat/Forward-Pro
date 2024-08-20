using System;
using System.Collections.Generic;
using Unity.Collections;

namespace UnityEngine.Rendering.EasyProbeVolume
{
    public class EasyProbe : IDisposable
    {
        public List<EasyProbeCell> cells = new ();
        public Vector3Int position;
       
        public NativeArray<float> coefficients = new NativeArray<float>(27, Allocator.TempJob, NativeArrayOptions.ClearMemory);

        public float atten = 0.0f;
        public float visibilty = 0.0f;
        
        public EasyProbe(Vector3Int position)
        {
            this.position = position;
        }

        public void Dispose()
        {
            coefficients.Dispose();
        }
    }
}