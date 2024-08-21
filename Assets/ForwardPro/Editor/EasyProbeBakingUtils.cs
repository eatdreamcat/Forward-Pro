using System.Collections;
using System.Collections.Generic;
using System.Threading;
using Unity.Collections;
using UnityEngine;

namespace UnityEngine.Rendering.EasyProbeVolume
{
    public static class EasyProbeBakingUtils 
    {
        private static readonly float INV_PI = 1 / 3.14159265359f;
        // private static readonly float PI =  3.14159265359f;
        private static readonly float TWO_PI = 3.14159265359f * 2;
        
        public static float BasicConstant(int level)
        {
            switch (level)
            {
                // l = 0
                case 0:
                    return 0.5f * Mathf.Sqrt(INV_PI);
                // l = 1
                case 1:
                    return Mathf.Sqrt(3f / 4f * INV_PI);
                case 2:
                    return Mathf.Sqrt(3f / 4f * INV_PI);
                case 3:
                    return Mathf.Sqrt(3f / 4f * INV_PI);
                // l = 2
                case 4:
                    return 0.5f * Mathf.Sqrt(15f * INV_PI);
                case 5:
                    return 0.5f * Mathf.Sqrt(15f * INV_PI);
                case 6:
                    return 0.25f * 3 * Mathf.Sqrt(5f * INV_PI);
                case 7:
                    return 0.5f * Mathf.Sqrt(15f * INV_PI);
                case 8:
                    return 0.25f * Mathf.Sqrt(15f * INV_PI);
                // l = 3
                case 9:
                    return 0.25f * Mathf.Sqrt(35f / 2f * INV_PI);
                case 10:
                    return 0.5f * Mathf.Sqrt(105f * INV_PI);
                case 11:
                    return 0.25f * Mathf.Sqrt(21f / 2f * INV_PI);
                case 12:
                    return 0.25f * Mathf.Sqrt(7f * INV_PI);
                case 13:
                    return 0.25f * Mathf.Sqrt(21f / 2f * INV_PI);
                case 14:
                    return 0.25f * Mathf.Sqrt(105f * INV_PI) ;
                case 15:
                    return 0.25f * Mathf.Sqrt(35f / 2f * INV_PI);
                default:
                    return 0f;
            }
        }
        
        public static float SHBasicFull(Vector3 normal, int level)
        {
            normal.Normalize();
            float x = normal.x;
            float y = normal.y;
            float z = normal.z;
            switch (level)
            {
                // l = 0 
                case 0:
                    return 0.5f * Mathf.Sqrt(INV_PI);
                // l = 1
                case 1:
                    return Mathf.Sqrt(3f / 4f * INV_PI) * x;
                case 2:
                    return Mathf.Sqrt(3f / 4f * INV_PI) * y;
                case 3:
                    return Mathf.Sqrt(3f / 4f * INV_PI) * z;
                // l = 2
                case 4:
                    return 0.5f * Mathf.Sqrt(15f * INV_PI) * x * y;
                case 5:
                    return 0.5f * Mathf.Sqrt(15f * INV_PI) * z * y;
                case 6:
                    return 0.25f * 3 * Mathf.Sqrt(5f * INV_PI) * z * z /* - 0.25f * Mathf.Sqrt(5f * INV_PI)*/;
                case 7:
                    return 0.5f * Mathf.Sqrt(15f * INV_PI) * z * x;
                case 8:
                    return 0.25f * Mathf.Sqrt(15f * INV_PI) * (x * x - y * y);
                // l = 3
                case 9:
                    return 0.25f * Mathf.Sqrt(35f / 2f * INV_PI) * (3 * x * x * y - y * y * y);
                case 10:
                    return 0.5f * Mathf.Sqrt(105f * INV_PI) * x * y * z;
                case 11:
                    return 0.25f * Mathf.Sqrt(21f / 2f * INV_PI) * (5 * z * z * y - y);
                case 12:
                    return 0.25f * Mathf.Sqrt(7f * INV_PI) * 5 * z * z * z - 3 * z;
                case 13:
                    return 0.25f * Mathf.Sqrt(21f / 2f * INV_PI) * (5 * z * z * x - x);
                case 14:
                    return 0.25f * Mathf.Sqrt(105f * INV_PI) * (x * x * z - y * y * z);
                case 15:
                    return 0.25f * Mathf.Sqrt(35f / 2f * INV_PI) * (x * x * x- 3 * y * y * x);
                default:
                    return 0f;
            }
        }
        
        public static Vector3 SampleSphereUniform(float u1, float u2, Vector3 sphereCenter)
        {
            float phi = TWO_PI * u2;
            float cosTheta = 1.0f - 2.0f * u1;
            return new Vector3(
                Mathf.Cos(phi) + sphereCenter.x,
                Mathf.Sin(phi) + sphereCenter.y,
                cosTheta + sphereCenter.z
            );
        }
        
        public static Vector3 GetCosineWeightedRandomDirection(float u1, float u2, Vector3 normalWS, Vector3 originWS, out float pdf)
        {
            normalWS.Normalize();
            var positionInSphere = SampleSphereUniform(u1, u2, originWS + normalWS);
            var biasVector = positionInSphere - originWS;
            biasVector.Normalize();
            var sampleDir = (normalWS + biasVector).normalized;
            pdf = INV_PI * Mathf.Max(0, Vector3.Dot(sampleDir, normalWS)); 
            return sampleDir;
        }
        
        static float CalculatePointLightAttenuation(float distanceSqr, float rangeSqr, float k)
        {
            float lightAtten = 1.0f / Mathf.Max(0.0001f, distanceSqr);
            float oneOverLightRangeSqr = 1.0f / Mathf.Max(0.0001f, rangeSqr);
            float factor = distanceSqr * oneOverLightRangeSqr;
            float smoothFactor = Mathf.Clamp01(1.0f - factor * factor);
            return lightAtten * (smoothFactor + k);
        }
        
        static Color SamplePointLight(Vector3 direction, Vector3 probePosition, EasyProbeLightSource light, Color lightColor)
        {
            var position = probePosition;
            var dirToLight = light.lightPosition - position;
            var color = lightColor *
                        Mathf.Max(0, Vector3.Dot(direction, dirToLight.normalized) * 0.5f + 0.5f);
            
            return color;
        }
        
        static Color SampleLight(Vector3 direction, Vector3 probePosition, EasyProbeLightSource light, Color lightColor)
        {
            switch (light.type)
            {
                case LightType.Point:
                    return SamplePointLight(direction, probePosition, light, lightColor);
            }
            
            return Color.black;
        }
        
        
        public static void BakeProbe(EasyProbeLightSource lightSource, Vector3 probePosition, 
            NativeArray<float> atten, NativeArray<float> probevisibility, NativeArray<float> coefficients,
            int sampleCount, int probeIndex, int lightIndex, int lightCount)
        {
            var dirToLight = lightSource.lightPosition - probePosition;
            var lightAtten =
                CalculatePointLightAttenuation(dirToLight.sqrMagnitude, lightSource.lightRange, 0.1f);
            lightAtten *= lightSource.lightIntensity;
            atten[probeIndex * lightCount + lightIndex] = lightAtten;
            
            var visibilty = 1.0f;
           
            // TODO: Raycast only supported on main thread
            // Ray ray = new Ray(probePosition, dirToLight.normalized);
            // if (Physics.Raycast(ray, dirToLight.magnitude))
            // {
            //     visibilty = 0.0f;
            // }
            probevisibility[probeIndex * lightCount + lightIndex] = visibilty;
            
            var dir = dirToLight.normalized;
            
            int probeCoefficientBaseIndex = probeIndex * 27 * sampleCount;
            
            for (int sampleIndex = 0; sampleIndex < sampleCount; ++sampleIndex)
            {
                var sampleDir = GetCosineWeightedRandomDirection(
                    HaltonSequence.Get((sampleIndex & 1023) + 1, 2), 
                    HaltonSequence.Get((sampleIndex & 1023) + 1, 3), 
                    dir, 
                    probePosition,
                    out var pdf
                );
                
                var radiance = SampleLight(sampleDir, probePosition, lightSource, lightSource.isRandomColor ? 
                    new Color(Random.Range(0f, 1f), Random.Range(0f, 1f), Random.Range(0f, 1f), 1.0f) : lightSource.lightColor) 
                    * lightAtten * visibilty / pdf / sampleCount;
                
                // probe.coefficients.Count = 27
                for (int coefficientIndex = 0; coefficientIndex < 27; coefficientIndex += 3)
                {
                    var level = coefficientIndex / 3;
                    var radianceEncoded = SHBasicFull(sampleDir, level) * BasicConstant(level) * radiance;
                    
                    coefficients[probeCoefficientBaseIndex + sampleIndex * 27 + coefficientIndex] += radianceEncoded.r;
                    coefficients[probeCoefficientBaseIndex + sampleIndex * 27 + coefficientIndex + 1] += radianceEncoded.g;
                    coefficients[probeCoefficientBaseIndex + sampleIndex * 27 + coefficientIndex + 2] += radianceEncoded.b;
                }
            }
                
            
        }
    }
}