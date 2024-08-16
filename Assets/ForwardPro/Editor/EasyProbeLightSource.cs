using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace UnityEngine.Rendering.EasyProbeVolume
{
    public class EasyProbeLightSource
    {
        private Bounds m_Bounds;

        private Light m_Light;
        
        public Light light => m_Light;

        public bool isRandomColor;

        public EasyProbeLightSource(Bounds bounds, Light light, bool isRandomColor)
        {
            m_Bounds = bounds;
            m_Light = light;
            this.isRandomColor = isRandomColor;
        }
        
        public bool IntersectCell(EasyProbeCell cell)
        {
            return m_Bounds.Intersects(cell.bounds);
        }
    }
}
