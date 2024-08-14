#ifndef _EASY_PROVE_VOLUME_HLSL_
#define _EASY_PROVE_VOLUME_HLSL_

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/SphericalHarmonics.hlsl"

#ifndef UNITY_SHADER_VARIABLES_INCLUDED
SAMPLER(s_linear_clamp_sampler);
SAMPLER(s_point_clamp_sampler);
#endif

#ifdef USE_APV_TEXTURE_HALF

TEXTURE3D_HALF(_EasyProbeSHAr);
TEXTURE3D_HALF(_EasyProbeSHAg);
TEXTURE3D_HALF(_EasyProbeSHAb);
TEXTURE3D_HALF(_EasyProbeSHBr);
TEXTURE3D_HALF(_EasyProbeSHBg);
TEXTURE3D_HALF(_EasyProbeSHBb);
TEXTURE3D_HALF(_EasyProbeSHC);

#else // !USE_APV_TEXTURE_HALF

TEXTURE3D(_EasyProbeSHAr);
TEXTURE3D(_EasyProbeSHAg);
TEXTURE3D(_EasyProbeSHAb);
TEXTURE3D(_EasyProbeSHBr);
TEXTURE3D(_EasyProbeSHBg);
TEXTURE3D(_EasyProbeSHBb);
TEXTURE3D(_EasyProbeSHC);

#endif // USE_APV_TEXTURE_HALF

SAMPLER(sampler_EasyProbeSHAr);
SAMPLER(sampler_EasyProbeSHAg);
SAMPLER(sampler_EasyProbeSHAb);
SAMPLER(sampler_EasyProbeSHBr);
SAMPLER(sampler_EasyProbeSHBg);
SAMPLER(sampler_EasyProbeSHBb);
SAMPLER(sampler_EasyProbeSHC);

float3 _EasyProbeVolumeSize;
float4 _EasyProbeVolumeWorldOffset;
float _EasyPVSamplingNoise;
float _EasyProbeNoiseFrameIndex;

#define _EasyVolumeWorldOffset _EasyProbeVolumeWorldOffset.xyz
#define _EasyProbeIntensity _EasyProbeVolumeWorldOffset.w

// -------------------------------------------------------------
// Various weighting functions for occlusion or helper functions.
// -------------------------------------------------------------
float3 AddNoiseToSamplingPosition(float3 posWS, float2 positionSS, float3 direction)
{
    #ifdef UNITY_SPACE_TRANSFORMS_INCLUDED
    float3 right = mul((float3x3)GetViewToWorldMatrix(), float3(1.0, 0.0, 0.0));
    float3 top = mul((float3x3)GetViewToWorldMatrix(), float3(0.0, 1.0, 0.0));
    float noise01 = InterleavedGradientNoise(positionSS, _EasyProbeNoiseFrameIndex);
    float noise02 = frac(noise01 * 100.0);
    float noise03 = frac(noise01 * 1000.0);
    direction += top * (noise02 - 0.5) + right * (noise03 - 0.5);
    return _EasyPVSamplingNoise > 0 ? posWS + noise01 * _EasyPVSamplingNoise * direction : posWS;
    #else
    return posWS;
    #endif
}


float3 SampleEasySH9(half3 N, float4 easySHCoefficients[7])
{
    return SampleSH9(easySHCoefficients, N);
}


float3 SampleEasySH9(half3 N, float3 positionWS, float2 positionSS, float3 direction)
{

    positionWS = AddNoiseToSamplingPosition(positionWS, positionSS, direction);

    // TODO: uvw offset
    float3 uvw = ((positionWS - _EasyVolumeWorldOffset) / _EasyProbeVolumeSize).xyz;

    half4 shAr = half4(SAMPLE_TEXTURE3D_LOD(_EasyProbeSHAr, sampler_EasyProbeSHAr, uvw, 0).rgba);
    half4 shAg = half4(SAMPLE_TEXTURE3D_LOD(_EasyProbeSHAg, sampler_EasyProbeSHAg, uvw, 0).rgba);
    half4 shAb = half4(SAMPLE_TEXTURE3D_LOD(_EasyProbeSHAb, sampler_EasyProbeSHAb, uvw, 0).rgba);
    half4 shBr = half4(SAMPLE_TEXTURE3D_LOD(_EasyProbeSHBr, sampler_EasyProbeSHBr, uvw, 0).rgba);
    half4 shBg = half4(SAMPLE_TEXTURE3D_LOD(_EasyProbeSHBg, sampler_EasyProbeSHBg, uvw, 0).rgba);
    half4 shBb = half4(SAMPLE_TEXTURE3D_LOD(_EasyProbeSHBb, sampler_EasyProbeSHBb, uvw, 0).rgba);
    half4 shCr = half4(SAMPLE_TEXTURE3D_LOD(_EasyProbeSHC, sampler_EasyProbeSHC, uvw, 0).rgba);

    // Linear + constant polynomial terms
    float3 res = max(0, SHEvalLinearL0L1(N, shAr, shAg, shAb));
    
    // Quadratic polynomials
    res += max(0, SHEvalLinearL2(N, shBr, shBg, shBb, shCr));

    #ifdef UNITY_COLORSPACE_GAMMA
    res = LinearToSRGB(res);
    #endif
    
    return res * _EasyProbeIntensity;
}

#endif