using System.Collections.Generic;

namespace UnityEngine.Rendering.EasyProbeVolume
{
    public class EasyProbe
    {
        // L2
        public static int s_CoefficientCount = 27;
        
        public List<int> cells = new ();
        public Vector3Int position;
       
        public List<float> coefficients = new();

        public float atten = 0.0f;
        public float visibilty = 0.0f;

        public Vector3[] RGB_L1()
        {
            return new Vector3[3]
            {
                new Vector3(coefficients[3], coefficients[4], coefficients[5]),
                new Vector3(coefficients[6], coefficients[7], coefficients[8]),
                new Vector3(coefficients[9], coefficients[10], coefficients[11])
            };
        }

        public EasyProbe(Vector3Int position)
        {
            this.position = position;
            for (int i = 0; i < s_CoefficientCount; ++i)
            {
                coefficients.Add(0);
            }
        }
    }
}