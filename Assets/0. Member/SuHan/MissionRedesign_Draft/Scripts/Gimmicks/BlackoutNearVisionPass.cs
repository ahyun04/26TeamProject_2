using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
#if UNITY_6000_0_OR_NEWER
using UnityEngine.Rendering.RenderGraphModule;
#endif

namespace TrustNoOne.Gimmicks
{
    /// <summary>
    /// [역할] 살인마 암전의 "가까운 곳만 보이기" 그리기 단계 (URP ScriptableRenderPass). 화면 전체를 삼각형 하나로 덮어
    ///  KillerBlackoutNearVision 셰이더가 점마다 카메라에서의 거리만큼 검게 칠한다 (1.5m 까지 그대로 → 3m 부터 검정 — 기본값).
    ///  BlackoutScreenRig 가 암전이 내 화면에 적용되는 동안 내 카메라가 그리기 시작할 때마다 그 카메라의 그리기 목록에 넣는다 (EnqueuePass).
    /// [이유] 공유 렌더러 설정(URP_Asset_Renderer — 팀원 파일)에 등록하지 않고 내 카메라에만 끼운다 (접근법 2 — 공유 파일 없음).
    ///  렌더러의 깊이 복사가 "투명 다음"(m_CopyDepthMode AfterTransparents)이라 카메라 안의 투명 판은 그 프레임 깊이를 못 읽는다 →
    ///  이 단계가 "깊이가 필요하다"(ConfigureInput Depth)고 알리면 URP 가 깊이 복사를 이 단계 바로 앞으로 당긴다 (URP 14.0.11 UniversalRenderer.cs 715 ~ 718).
    ///  그래서 플레이어 카메라의 깊이 설정은 바꾸지 않는다. 넣은 단계는 그 카메라를 다 그린 뒤 URP 가 목록에서 뺀다 (ScriptableRenderer.cs 2120).
    ///  시점 = 투명 물체 다음 · 후처리 앞 → 멀리 있는 이름표 · 효과도 그 뒤 벽 거리만큼 가려지고, 팔 전용 카메라(쌓기)는 그 다음이라 팔은 밝다.
    ///  (업그레이드 뒤 추가) Unity 6000.3 · URP 17.3 은 새 그리기 방식(Render Graph)만 쓴다 — 정의 URP_COMPATIBILITY_MODE 가 없으면
    ///  옛 Execute 를 부르지 않는다. 그래서 같은 일(깊이를 읽어 화면 전체 삼각형 하나)을 RecordRenderGraph 로도 한다.
    ///  옛 Execute 는 2022 이거나 호환 정의가 있을 때만 남긴다 (둘 다 컴파일 · "더 이상 안 씀" 경고 없음).
    /// [근거] 16d 명세 KB57 · KB58 · KB60, 조사 1-3, (업그레이드 뒤) KB67 ~ KB69 — URP 17.3 FullScreenPassRendererFeature 와 같은 모양
    /// </summary>
    public sealed class BlackoutNearVisionPass : ScriptableRenderPass
    {
        private const string ProfilerTag = "KillerBlackoutNearVision";

        private Material material;

        public BlackoutNearVisionPass()
        {
            renderPassEvent = RenderPassEvent.AfterRenderingTransparents;
            // 깊이 텍스처가 필요하다고 알림 → URP 가 이 단계 바로 앞에 깊이를 복사한다 (KB58)
            ConfigureInput(ScriptableRenderPassInput.Depth);
        }

        /// <summary>이번 프레임에 쓸 재질 (BlackoutScreenRig 의 실행 중 복사본 — 진하기 · 거리 값이 들어 있음)</summary>
        public void Setup(Material nearVisionMaterial)
        {
            material = nearVisionMaterial;
        }

#if !UNITY_6000_0_OR_NEWER || URP_COMPATIBILITY_MODE
        // 옛 방식 (Unity 2022 · URP 14, 또는 Unity 6 에서 호환 정의를 켰을 때)
        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (material == null)
                return;

            CommandBuffer cmd = CommandBufferPool.Get(ProfilerTag);
            // 화면 전체 삼각형 하나 (꼭짓점 위치는 셰이더가 번호로 만든다 — GetFullScreenTriangleVertexPosition)
            cmd.DrawProcedural(Matrix4x4.identity, material, 0, MeshTopology.Triangles, 3);
            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }
#endif

#if UNITY_6000_0_OR_NEWER
        /// <summary>그리기 함수에 넘기는 값 (Render Graph 는 단계마다 이런 묶음을 따로 들고 있는다)</summary>
        private sealed class PassData
        {
            internal Material material;
        }

        // 새 방식 (Unity 6000.3 · URP 17.3 — 업그레이드 뒤 추가 KB67): 깊이를 읽는다고 알리고, 지금 화면 색 위에 검게 덮는다
        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            if (material == null)
                return;

            UniversalResourceData resources = frameData.Get<UniversalResourceData>();

            using (IRasterRenderGraphBuilder builder = renderGraph.AddRasterRenderPass<PassData>(ProfilerTag, out PassData data))
            {
                data.material = material;

                // ConfigureInput(Depth) 로 URP 가 만든 카메라 깊이 텍스처 — 셰이더가 _CameraDepthTexture 로 거리를 읽는다 (KB58)
                if (resources.cameraDepthTexture.IsValid())
                    builder.UseTexture(resources.cameraDepthTexture);

                // 기존 화면 색 위에 반투명으로 덮으므로 읽기 + 쓰기 (지우지 않음)
                builder.SetRenderAttachment(resources.activeColorTexture, 0, AccessFlags.ReadWrite);

                builder.SetRenderFunc((PassData passData, RasterGraphContext context) =>
                {
                    // 옛 방식과 같은 화면 전체 삼각형 하나
                    context.cmd.DrawProcedural(Matrix4x4.identity, passData.material, 0, MeshTopology.Triangles, 3);
                });
            }
        }
#endif
    }
}
