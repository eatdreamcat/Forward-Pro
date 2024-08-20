using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace UnityEngine.Rendering.EasyProbeVolume
{
    public struct EasyProbeLightSource
    {
        private Bounds m_Bounds;

        public Vector3 lightPosition;
        public float lightIntensity;
        public Color lightColor;
        public float lightRange;
        public LightType type;

        public bool isRandomColor;

        public EasyProbeLightSource(
            Bounds bounds,
            Vector3 position,
            float intensity,
            Color color,
            float range,
            LightType type,
            bool isRandomColor
            )
        {
            m_Bounds = bounds;
            this.isRandomColor = isRandomColor;
            lightPosition = position;
            lightIntensity = intensity;
            lightColor = color;
            lightRange = range;
            this.type = type;
        }
        
        public bool IntersectCell(Bounds cell)
        {
            return m_Bounds.Intersects(cell);
        }
    }
}
