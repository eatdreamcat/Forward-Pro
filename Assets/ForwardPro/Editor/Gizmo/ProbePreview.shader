Shader "Hiden/EasyProbeVolume/ProbePreview"
{
    Properties
    {
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 100

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "../../Runtime/EasyProbeVolume.hlsl"
        ENDHLSL
        Pass
        {
            Fog { Mode Off }
            Cull Back
            
            HLSLPROGRAM

            #pragma vertex ProbePreviewVert
            #pragma fragment ProbePreviewFrag

            #pragma shader_feature_local_fragment  _ _Diffuse _Attenuation _Position _Visibility

            CBUFFER_START(perMaterial)
            float4 _EasySHCoefficients[7];
            float _ProbeAtten;
            float _Visiablity;
            float3 _VolumeSize;
            CBUFFER_END
            
            struct Attribute
            {
                float3 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varying
            {
                float4 positionCS : SV_POSITION;
                float3 normalOS : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
            };

            Varying ProbePreviewVert(Attribute input)
            {
                Varying output = (Varying)0;
                output.positionCS = TransformObjectToHClip(input.positionOS);
                output.positionWS = TransformObjectToWorld(input.positionOS);
                output.normalOS = input.normalOS;
                return output;
            }
            
            half4 ProbePreviewFrag(Varying input): SV_Target
            {
                #if _Position
                return half4(input.positionWS / _VolumeSize, 1.0);
                #elif _Diffuse
                // donothing
                #elif _Attenuation
                return half4(_ProbeAtten.xxx, 1.0);
                #elif _Visibility
                return half4(_Visiablity.xxx, 1.0);
                #endif
                
                half3 normalWS = TransformObjectToWorldNormal(normalize(input.normalOS));
                return half4(SampleEasySH9(normalWS, _EasySHCoefficients), 1.0);
            }
            
            ENDHLSL
        }
    }
}
