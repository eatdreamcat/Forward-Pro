using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace UnityEngine.Rendering.EasyProbeVolume
{
    public static class EasyProbeRenderingUtils
    {
        private const int k_PackedCoefficientCount = 7;
        public static int _EasySHCoefficients = Shader.PropertyToID("_EasySHCoefficients");

        public static Vector4[] s_PackedCoefficients = new Vector4[k_PackedCoefficientCount];
        /**
         *  0 - 2:
         *  xyz : L1 rgb, w: L0 rgb
         */
        private static Vector4[] m_TestCoefficients = new Vector4[9];
        public static void PackAndPushCoefficients(Material material, EasyProbe probe, EasyProbeVolume volume)
        {
            //test
            for (int i = 0; i < m_TestCoefficients.Length; ++i)
            {
                m_TestCoefficients[i] = new Vector4(
                    probe.coefficients[i * 3],
                    probe.coefficients[i * 3 + 1],
                    probe.coefficients[i * 3 + 2]
                );
            }
            material.SetVectorArray("_SHLightingCoefficients", m_TestCoefficients);
            material.SetFloat("_ProbeAtten", probe.atten);
            material.SetFloat("_Visiablity", probe.visibilty);
            material.SetVector("_VolumeSize", volume.volumeSize);
            
            return;
            for (int i = 0; i < 3; ++i)
            {
                s_PackedCoefficients[i] = new Vector4(
                    probe.coefficients[i * 3 + 3],
                    probe.coefficients[i * 3 + 4],
                    probe.coefficients[i * 3 + 5],
                    probe.coefficients[i]
                );
            }
            
            material.SetVectorArray(_EasySHCoefficients, s_PackedCoefficients);
        }
    }
}

