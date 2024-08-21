using System;
using System.Collections.Generic;
using Unity.Collections;

namespace UnityEngine.Rendering.EasyProbeVolume
{
    public class EasyProbe
    {
        public List<EasyProbeCell> cells = new ();
        public Vector3Int position;
        public EasyProbe(Vector3Int position)
        {
            this.position = position;
        }
        
    }
}