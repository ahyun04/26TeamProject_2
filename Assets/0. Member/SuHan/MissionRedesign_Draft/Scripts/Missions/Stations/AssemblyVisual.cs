using UnityEngine;

namespace TrustNoOne.Missions
{
    /// <summary>
    /// [역할] 장비 자리 4개에 붙은 부품 모습: 보이기 · 날아오기 · 완료 발광. 3c 명세 3-4.
    ///  무엇을 보일지는 AssemblyStation 이 매 Render 에 정한다 (3b 의 LifeSupportVisual 과 같은 역할).
    /// [붙는 모습 — EA9] 장비 모델의 Parts_{색}_axes 아래에 그 색 부품 모델 사본(로컬 위치 · 회전 0)이 숨겨져 있다.
    /// [날아오기 — EA6 · EA12] 조립 위치 원 중심 위(flyStart)에서 자리까지 감속하며 날아온다. 진행률은 장치가 시각 공식으로 준다.
    /// [완료 — EA6] 붙은 부품을 발광 초록(Neon_green)으로 바꿔 "정상 작동"을 보여 준다 (MaterialSwapper, 3a C9 · C10).
    /// [성능] 바뀔 때만 적용한다 (날아오는 0.4초 동안만 위치가 매 프레임 바뀐다).
    /// </summary>
    public class AssemblyVisual : MonoBehaviour
    {
        [Tooltip("자리별 붙은 부품 모델 (색 순서: 파랑 · 회색 · 빨강 · 노랑). 부모 = 자리 노드, 평소 숨김")]
        [SerializeField] private Transform[] attachedParts = new Transform[AssemblyRules.PartCount];

        [Tooltip("날아오기 시작점 (조립 위치 원 중심 위)")]
        [SerializeField] private Transform flyStart;

        [Tooltip("완료 발광 재질 (Neon_green)")]
        [SerializeField] private Material doneMaterial;

        private readonly float[] shownProgress = { -2f, -2f, -2f, -2f };
        private MaterialSwapper[][] glows;
        private bool? shownDone;

        private void Awake()
        {
            glows = new MaterialSwapper[attachedParts.Length][];

            for (int i = 0; i < attachedParts.Length; i++)
            {
                Renderer[] renderers = attachedParts[i] != null ? attachedParts[i].GetComponentsInChildren<Renderer>(true) : new Renderer[0];
                glows[i] = new MaterialSwapper[renderers.Length];

                for (int r = 0; r < renderers.Length; r++)
                    glows[i][r] = new MaterialSwapper(renderers[r], doneMaterial);
            }
        }

        /// <param name="color">색 번호 (자리 번호)</param>
        /// <param name="progress">−1 = 빈 자리(숨김), 0 ~ 1 = 날아오는 중, 1 = 자리에 붙음</param>
        public void ShowPart(int color, float progress)
        {
            if (color < 0 || color >= attachedParts.Length || attachedParts[color] == null || progress == shownProgress[color])
                return;

            shownProgress[color] = progress;
            Transform part = attachedParts[color];
            bool visible = progress >= 0f;

            if (part.gameObject.activeSelf != visible)
                part.gameObject.SetActive(visible);

            if (!visible)
                return;

            if (progress >= 1f || flyStart == null || part.parent == null)
            {
                part.localPosition = Vector3.zero;
                return;
            }

            // 감속 (처음엔 빠르게, 자리에 가까워지면 천천히)
            float eased = 1f - (1f - progress) * (1f - progress);
            part.position = Vector3.Lerp(flyStart.position, part.parent.position, eased);
        }

        /// <summary>완료면 붙은 부품을 발광 초록으로, 아니면 원래 재질로.</summary>
        public void ShowDone(bool done)
        {
            if (shownDone == done || glows == null)
                return;

            shownDone = done;

            foreach (MaterialSwapper[] part in glows)
            {
                foreach (MaterialSwapper glow in part)
                    glow.Show(done ? 0 : MaterialSwapper.Original);
            }
        }
    }
}
