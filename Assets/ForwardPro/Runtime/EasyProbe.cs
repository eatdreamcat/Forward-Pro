using System.Collections.Generic;

namespace UnityEngine.Rendering.EasyProbeVolume
{
    public class EasyProbe
    {
        public Vector3Int position;
        public List<float> coefficients = new();
    }
}