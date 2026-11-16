Shader "TrustNoOne/LobbyMirror"
{
    Properties
    {
        [NoScaleOffset] _MirrorFeed("Reflection", 2D) = "black" {}
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MirrorFeed);
            SAMPLER(sampler_MirrorFeed);
            float4x4 _MirrorViewProjection;

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 reflectionCS : TEXCOORD0;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(positionWS);
                output.reflectionCS = mul(_MirrorViewProjection, float4(positionWS, 1));
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                if (input.reflectionCS.w <= 0.0001) return half4(0.025, 0.04, 0.05, 1);
                float2 uv = input.reflectionCS.xy * (0.5 / input.reflectionCS.w) + 0.5;
                #if UNITY_UV_STARTS_AT_TOP
                uv.y = 1.0 - uv.y;
                #endif
                if (any(uv < 0.0) || any(uv > 1.0)) return half4(0.025, 0.04, 0.05, 1);
                return half4(SAMPLE_TEXTURE2D(_MirrorFeed, sampler_MirrorFeed, uv).rgb, 1);
            }
            ENDHLSL
        }
    }
}
