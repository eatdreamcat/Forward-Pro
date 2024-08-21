using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
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
        
        public static void PackAndPushCoefficients(
            Material material, EasyProbeVolume volume,
            ref NativeArray<float> probeAtten,
            ref NativeArray<float> probeVisibility,
            ref NativeArray<float> coefficients,
            int probeIndex)
        {

            if (coefficients.IsCreated == false || probeAtten.IsCreated == false || probeVisibility.IsCreated == false)
            {
                return;
            }
            
            #if UNITY_EDITOR
            
            material.SetFloat(_ProbeAtten, probeAtten[probeIndex]);
            material.SetFloat(_Visiablity, probeVisibility[probeIndex]);
            material.SetVector(_VolumeSize, volume.volumeSize);
            
            #endif

            var baseIndex = probeIndex * 27;
            // L0L1
            for (int i = 0; i < 3; ++i)
            {
                var index = i + baseIndex;
                var L1 = index + 3;
                s_PackedCoefficients[i] = new Vector4(
                    coefficients[L1], // L1 shAr  shAg  shAg
                    coefficients[L1 + 3], 
                    coefficients[L1 + 6], 
                    coefficients[index] // L0: RGB
                );
            }

            // L2
            for (int i = 3; i < k_PackedCoefficientCount - 1; ++i)
            {
                var index = i + baseIndex;
                var L2 = 9 + index;
                s_PackedCoefficients[i] = new Vector4(
                    coefficients[L2], // L2 RGB 1th xx
                    coefficients[L2 + 3], // L2 RGB 2th yz
                    coefficients[L2 + 6], // L2 RGB 3th zz
                    coefficients[L2 + 9]  // L2 RGB 4th zx
                );
                
            }
            
            // final 5th quadratic in L2
            s_PackedCoefficients[k_PackedCoefficientCount - 1] = new Vector4(
                coefficients[baseIndex + 24],
                coefficients[baseIndex + 25],
                coefficients[baseIndex + 26],
                1.0f
                );
            
            material.SetVectorArray(_EasySHCoefficients, s_PackedCoefficients);
        }
    }
}

