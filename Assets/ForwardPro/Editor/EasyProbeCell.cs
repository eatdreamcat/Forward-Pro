using System;
using System.Collections.Generic;

namespace UnityEngine.Rendering.EasyProbeVolume
{
    public class EasyProbeCell : IEquatable<EasyProbeCell>
    {
        public Vector3Int position = Vector3Int.zero;
        public int size = 0;
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

        public Bounds bounds => new Bounds(position, new Vector3(size, size, size));
    }
}