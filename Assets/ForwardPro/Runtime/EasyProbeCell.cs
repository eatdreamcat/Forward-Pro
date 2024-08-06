using System;
using System.Collections.Generic;

namespace UnityEngine.Rendering.EasyProbeVolume
{
    public class EasyProbeCell : IEquatable<EasyProbeCell>
    {
        public List<int> probeIndices = new();
        public Vector3Int position;
        public int size;
        public bool Equals(EasyProbeCell other)
        {
            return position == other.position && size == other.size;
        }

        public override bool Equals(object obj)
        {
            return obj is EasyProbeCell other && Equals(other);
        }
        
        public Vector3Int Min => new Vector3Int(
            position.x - size / 2,
            position.y - size / 2,
            position.z - size / 2);
        
        public Vector3Int Max => new Vector3Int(
                position.x + size / 2,
                position.y + size / 2,
                position.z + size / 2);
    }
}