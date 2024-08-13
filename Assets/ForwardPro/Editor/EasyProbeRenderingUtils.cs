using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace UnityEngine.Rendering.EasyProbeVolume
{
    public static class EasyProbeRenderingUtils
    {
        private const int k_PackedCoefficientCount = 7;
        public static int _EasySHCoefficients = Shader.PropertyToID("_EasySHCoefficients");
       
        #if UNITY_EDITOR

        public static int _ProbeAtten = Shader.PropertyToID("_ProbeAtten");
        public static int _Visiablity = Shader.PropertyToID("_Visiablity");
        public static int _VolumeSize = Shader.PropertyToID("_VolumeSize");

        #endif
        /**
        *  0 - 2:
        *  xyz : L1 rgb, w: L0 rgb
        */
        public static Vector4[] s_PackedCoefficients = new Vector4[k_PackedCoefficientCount];
        
        public static void PackAndPushCoefficients(Material material, EasyProbe probe, EasyProbeVolume volume)
        {
            
            #if UNITY_EDITOR
            
            material.SetFloat(_ProbeAtten, probe.atten);
            material.SetFloat(_Visiablity, probe.visibilty);
            material.SetVector(_VolumeSize, volume.volumeSize);
            
            #endif
            
            // L0L1
            for (int i = 0; i < 3; ++i)
            {
                var L1 = i + 3;
                s_PackedCoefficients[i] = new Vector4(
                    probe.coefficients[L1], // L1 shAr  shAg  shAg
                    probe.coefficients[L1 + 3], 
                    probe.coefficients[L1 + 6], 
                    probe.coefficients[i] // L0: RGB
                );
            }

            // L2
            for (int i = 3; i < k_PackedCoefficientCount - 1; ++i)
            {
                var L2 = 9 + i;
                s_PackedCoefficients[i] = new Vector4(
                    probe.coefficients[L2], // L2 RGB 1th xx
                    probe.coefficients[L2 + 3], // L2 RGB 2th yz
                    probe.coefficients[L2 + 6], // L2 RGB 3th zz
                    probe.coefficients[L2 + 9]  // L2 RGB 4th zx
                );
                
            }
            
            // final 5th quadratic in L2
            s_PackedCoefficients[k_PackedCoefficientCount - 1] = new Vector4(
                probe.coefficients[24],
                probe.coefficients[25],
                probe.coefficients[26],
                1.0f
                );
            
            material.SetVectorArray(_EasySHCoefficients, s_PackedCoefficients);
        }
    }
}

