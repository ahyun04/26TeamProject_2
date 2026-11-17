// [역할] 살인마 암전의 "가까운 곳만 보이기" (16d). 화면 전체 삼각형 하나로 그려지고, 점마다
//  깊이 텍스처 → 월드 위치 → 카메라에서의 직선 거리를 구해 그만큼 검은색을 덮는다.
//  진하기 = smoothstep(_NearStart, _NearEnd, 거리) × _Darkness. 시작 ≥ 끝이면 끝 거리에서 딱 끊긴다 (step).
//  깊이가 없는 곳(하늘)은 먼 곳으로 계산돼 검다. 투명 물체는 그 뒤 불투명 표면의 거리로 가려진다.
// [이유] 기획 피드백 "바로 앞까지만 보이도록" — 정면 깊이가 아니라 직선 거리를 써야 화면 가장자리가 더 멀리 보이지 않는다.
//  같은 식을 순수 규칙 BlackoutVisualRules.NearVisionAlpha 로 엔진 밖에서 테스트한다 (셰이더는 dotnet 으로 검사할 수 없음).
//  그리는 곳은 BlackoutNearVisionPass (투명 물체 다음 · 후처리 앞, 깊이 복사를 그 앞으로 당김).
// [근거] 16d 명세 KB59 · KB61, 깊이로 월드 위치 되살리기 = URP 14 문서 예제와 같은 방식 (SampleSceneDepth · ComputeWorldSpacePosition)
Shader "TrustNoOne/KillerBlackoutNearVision"
{
    Properties
    {
        _Darkness ("Darkness (0 ~ 1)", Range(0, 1)) = 1
        _NearStart ("Near Visible Start (m)", Float) = 1.5
        _NearEnd ("Near Visible End (m)", Float) = 3
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Transparent" "Queue" = "Overlay" }

        Pass
        {
            Name "KillerBlackoutNearVision"
            ZTest Always
            ZWrite Off
            Cull Off
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _Darkness;
                float _NearStart;
                float _NearEnd;
            CBUFFER_END

            struct Attributes
            {
                uint vertexID : SV_VertexID;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                // 화면 전체를 덮는 삼각형 하나 (꼭짓점 번호 0 ~ 2 → 화면 공간 위치)
                output.positionCS = GetFullScreenTriangleVertexPosition(input.vertexID);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 uv = GetNormalizedScreenSpaceUV(input.positionCS);

                #if UNITY_REVERSED_Z
                    float depth = SampleSceneDepth(uv);
                #else
                    float depth = lerp(UNITY_NEAR_CLIP_VALUE, 1, SampleSceneDepth(uv));
                #endif

                float3 worldPos = ComputeWorldSpacePosition(uv, depth, UNITY_MATRIX_I_VP);
                float dist = distance(worldPos, _WorldSpaceCameraPos);

                // BlackoutVisualRules.NearVisionAlpha 와 같은 식
                float t = _NearEnd > _NearStart ? smoothstep(_NearStart, _NearEnd, dist) : step(_NearEnd, dist);
                return half4(0, 0, 0, saturate(t * _Darkness));
            }
            ENDHLSL
        }
    }
}
