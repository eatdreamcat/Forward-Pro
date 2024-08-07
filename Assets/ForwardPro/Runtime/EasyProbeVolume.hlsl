#ifndef _EASY_PROVE_VOLUME_HLSL_
#define _EASY_PROVE_VOLUME_HLSL_

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/SphericalHarmonics.hlsl"

// SH block feature
/**
*  real3 x1;
    // Linear (L1) + constant (L0) polynomial terms
    x1.r = dot(_EasySHCoefficients[0], vA);
    x1.g = dot(_EasySHCoefficients[1], vA);
    x1.b = dot(_EasySHCoefficients[2], vA);

_EasySHCoefficients:
    0 - 2: xyz = L1 rgb, w : L1 rgb
    
 */
float4 _EasySHCoefficients[7];

float3 SampleEasySH9(half3 N)
{
    return SampleSH9(_EasySHCoefficients, N);
}

// Test
float3 _SHLightingCoefficients[9];

half3 SampleEasySHTest(half3 normalWS)
{
        // l= 0, m = 0
        half3 res = _SHLightingCoefficients[0].xyz;
  
        // l = 1, m = 1
        res += half3(    dot(normalWS.x, _SHLightingCoefficients[1].x),
                         dot(normalWS.x, _SHLightingCoefficients[1].y),
                         dot(normalWS.x, _SHLightingCoefficients[1].z));

        // l = 1, m = -1
        res += half3(    dot(normalWS.y, _SHLightingCoefficients[2].x),
                         dot(normalWS.y, _SHLightingCoefficients[2].y),
                         dot(normalWS.y, _SHLightingCoefficients[2].z));

        // l = 1, m = 0
        res += half3(    dot(normalWS.z, _SHLightingCoefficients[3].x),
                         dot(normalWS.z, _SHLightingCoefficients[3].y),
                         dot(normalWS.z, _SHLightingCoefficients[3].z));

    
        // l = 2, m = -2
        res += half3(    dot(normalWS.y * normalWS.x, _SHLightingCoefficients[4].x),
                         dot(normalWS.y * normalWS.x, _SHLightingCoefficients[4].y),
                         dot(normalWS.y * normalWS.x, _SHLightingCoefficients[4].z));

        // l = 2, m = -1
        res += half3(    dot(normalWS.y * normalWS.z, _SHLightingCoefficients[5].x),
                         dot(normalWS.y * normalWS.z, _SHLightingCoefficients[5].y),
                         dot(normalWS.y * normalWS.z, _SHLightingCoefficients[5].z));
    
        // l = 2, m = 0
        res += half3(    dot(3 * normalWS.z * normalWS.z - 1, _SHLightingCoefficients[6].x),
                         dot(3 * normalWS.z * normalWS.z - 1, _SHLightingCoefficients[6].y),
                         dot(3 * normalWS.z * normalWS.z - 1, _SHLightingCoefficients[6].z));

        // l = 2, m = 1
        res += half3(    dot(normalWS.x * normalWS.z, _SHLightingCoefficients[7].x),
                         dot(normalWS.x * normalWS.z, _SHLightingCoefficients[7].y),
                         dot(normalWS.x * normalWS.z, _SHLightingCoefficients[7].z));

        // l = 2, m = 2
        res += half3(    dot(normalWS.x * normalWS.x - normalWS.y * normalWS.y, _SHLightingCoefficients[8].x),
                         dot(normalWS.x * normalWS.x - normalWS.y * normalWS.y, _SHLightingCoefficients[8].y),
                         dot(normalWS.x * normalWS.x - normalWS.y * normalWS.y, _SHLightingCoefficients[8].z));
        
        return res;
}

#endif