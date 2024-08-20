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

#ifdef USE_SH_BAND_L2

TEXTURE3D_HALF(_EasyProbeSHBr);
TEXTURE3D_HALF(_EasyProbeSHBg);
TEXTURE3D_HALF(_EasyProbeSHBb);
TEXTURE3D_HALF(_EasyProbeSHC);

#endif


#else // !USE_APV_TEXTURE_HALF

TEXTURE3D(_EasyProbeSHAr);
TEXTURE3D(_EasyProbeSHAg);
TEXTURE3D(_EasyProbeSHAb);

#ifdef USE_SH_BAND_L2

TEXTURE3D(_EasyProbeSHBr);
TEXTURE3D(_EasyProbeSHBg);
TEXTURE3D(_EasyProbeSHBb);
TEXTURE3D(_EasyProbeSHC);

#endif


#endif // USE_APV_TEXTURE_HALF

float3 _EasyProbeVolumeSize;
float4 _EasyProbeVolumeWorldOffset;
float4 _ProbeVolumeClampedMax;
float _EasyPVSamplingNoise;
float _EasyProbeNoiseFrameIndex;
half _EasyProbeToggle;

#define _EasyVolumeWorldOffset _EasyProbeVolumeWorldOffset.xyz
#define _EasyVolumeWorldPosMax _ProbeVolumeClampedMax.xyz
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

    UNITY_BRANCH
    if (_EasyProbeToggle <= 0)
    {
        return 0;
    }
    
    positionWS = AddNoiseToSamplingPosition(positionWS, positionSS, direction);

    if (any(positionWS.xyz > _EasyVolumeWorldPosMax.xyz))
    {
        return 0;
    }
    
    float3 uvw = ((positionWS - _EasyVolumeWorldOffset) / _EasyProbeVolumeSize).xyz;
    float mask = any(uvw < 0.001) || any(uvw > 0.999);
    mask = 1 - mask;
    
    half4 shAr = half4(SAMPLE_TEXTURE3D_LOD(_EasyProbeSHAr, s_linear_clamp_sampler, uvw, 0).rgba);
    half4 shAg = half4(SAMPLE_TEXTURE3D_LOD(_EasyProbeSHAg, s_linear_clamp_sampler, uvw, 0).rgba);
    half4 shAb = half4(SAMPLE_TEXTURE3D_LOD(_EasyProbeSHAb, s_linear_clamp_sampler, uvw, 0).rgba);
  

    // Linear + constant polynomial terms
    float3 res = max(0, SHEvalLinearL0L1(N, shAr, shAg, shAb));

    #ifdef USE_SH_BAND_L2

    half4 shBr = half4(SAMPLE_TEXTURE3D_LOD(_EasyProbeSHBr, s_linear_clamp_sampler, uvw, 0).rgba);
    half4 shBg = half4(SAMPLE_TEXTURE3D_LOD(_EasyProbeSHBg, s_linear_clamp_sampler, uvw, 0).rgba);
    half4 shBb = half4(SAMPLE_TEXTURE3D_LOD(_EasyProbeSHBb, s_linear_clamp_sampler, uvw, 0).rgba);
    half4 shCr = half4(SAMPLE_TEXTURE3D_LOD(_EasyProbeSHC, s_linear_clamp_sampler, uvw, 0).rgba);
    // Quadratic polynomials
    res += max(0, SHEvalLinearL2(N, shBr, shBg, shBb, shCr));

    #endif


    #ifdef UNITY_COLORSPACE_GAMMA
    res = LinearToSRGB(res);
    #endif
    
    return res * _EasyProbeIntensity * mask;
}

#endif