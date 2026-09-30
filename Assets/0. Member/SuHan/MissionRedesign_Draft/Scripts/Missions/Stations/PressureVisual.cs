using UnityEngine;

namespace TrustNoOne.Missions
{
    /// <summary>
    /// [역할] 피스톤 하나(A/B/C)의 모습: 압력계 바늘 · 피스톤 추 · 표시등(피스톤 글자). 상태는 PressureStation 이 매 Render 에 Apply 로 넣어 준다. 2d 명세 3-4.
    ///  - 바늘: 압력계 중심의 회전축 오브젝트(needlePivot)를 높이에 따라 minAngle ~ maxAngle 로 돌린다 (PR10 — 바늘 메시 피벗이 중심이라는 보장이 없어
    ///    변환기가 압력계 중심에 회전축을 만들어 바늘을 그 아래로 옮겼다)
    ///  - 추: 원래 위치 기준 weightAxis 방향으로 bottomOffset ~ topOffset 을 오르내린다
    ///  - 표시등(PR14): 피스톤 원통의 글자(A/B/C). 움직임 = 원래 재질 / 고정 = 초록 / 멈춤 = 빨강.
    ///    [왜 재질 교체인가] 글자가 쓰는 아틀라스 재질은 발광(_EMISSION)이 꺼져 있어 색만 덧칠하면 빛나지 않는다.
    ///     Neon_green · Neon_red 는 발광이 켜진 단색 재질이라 그대로 끼우면 기획 의도(초록불 · 빨간불)대로 보인다.
    ///     렌더러가 "어느 재질을 쓰는지"만 바꾸므로 재질 에셋 자체는 바뀌지 않고, 복제본도 생기지 않는다.
    ///    [근거] V2 피스톤 모델에서 글자가 Pressure_piston_X_Alphabet 라는 별도 부품으로 분리돼, 원통은 그대로 두고 글자만 재질을 바꿀 수 있다.
    /// [보정] 각도 · 오프셋 기본값은 변환기가 추정해 넣는다. 화면을 보고 인스펙터에서 조정한다 (명세 7장).
    /// </summary>
    public class PressureVisual : MonoBehaviour
    {
        [Header("바늘")]
        [SerializeField] private Transform needlePivot;
        [Tooltip("회전축 (needlePivot 로컬 방향)")]
        [SerializeField] private Vector3 needleAxis = Vector3.forward;
        [SerializeField] private float minAngle = -120f;
        [SerializeField] private float maxAngle = 120f;

        [Header("추")]
        [SerializeField] private Transform weight;
        [Tooltip("위쪽 방향 (weight 부모 로컬 방향)")]
        [SerializeField] private Vector3 weightAxis = Vector3.up;
        [SerializeField] private float bottomOffset = -0.3f;
        [SerializeField] private float topOffset = 0.3f;

        [Header("표시등 (피스톤 글자)")]
        [Tooltip("글자 렌더러 (V2 피스톤 모델의 Pressure_piston_X_Alphabet). 비어 있으면 표시등 없이 동작")]
        [SerializeField] private Renderer lampRenderer;
        [Tooltip("고정했을 때 끼울 재질 (Neon_green)")]
        [SerializeField] private Material lockedMaterial;
        [Tooltip("멈췄을 때 끼울 재질 (Neon_red)")]
        [SerializeField] private Material stalledMaterial;

        private Quaternion needleStart;
        private Vector3 weightStart;
        private bool initialized;
        private PistonState? shownState;

        // 상태별 재질 배열을 미리 만들어 둔다 (매번 새 배열을 만들지 않게)
        private Material[] originalMaterials;
        private Material[] lockedMaterials;
        private Material[] stalledMaterials;

        private void Awake()
        {
            Initialize();
        }

        public void Apply(float height, PistonState state)
        {
            Initialize();
            float h = Mathf.Clamp01(height);

            if (needlePivot != null)
            {
                Vector3 axis = needleAxis.sqrMagnitude > 0f ? needleAxis.normalized : Vector3.forward;
                needlePivot.localRotation = needleStart * Quaternion.AngleAxis(Mathf.Lerp(minAngle, maxAngle, h), axis);
            }

            if (weight != null)
            {
                Vector3 axis = weightAxis.sqrMagnitude > 0f ? weightAxis.normalized : Vector3.up;
                weight.localPosition = weightStart + axis * Mathf.Lerp(bottomOffset, topOffset, h);
            }

            ApplyLamp(state);
        }

        private void Initialize()
        {
            if (initialized)
                return;

            initialized = true;

            if (needlePivot != null)
                needleStart = needlePivot.localRotation;

            if (weight != null)
                weightStart = weight.localPosition;

            if (lampRenderer != null)
            {
                originalMaterials = lampRenderer.sharedMaterials;
                lockedMaterials = Fill(originalMaterials.Length, lockedMaterial);
                stalledMaterials = Fill(originalMaterials.Length, stalledMaterial);
            }
        }

        private void ApplyLamp(PistonState state)
        {
            if (shownState == state)
                return;

            shownState = state;

            if (lampRenderer == null || originalMaterials == null)
                return;

            Material[] target = state switch
            {
                PistonState.Locked => lockedMaterials,
                PistonState.Stalled => stalledMaterials,
                _ => null,
            };

            // 재질이 비어 있는 상태는 원래 재질로 (이전 색이 남지 않게)
            lampRenderer.sharedMaterials = target ?? originalMaterials;
        }

        /// <summary>글자 메시의 모든 재질 칸을 같은 재질로 채운 배열. 재질이 없으면 null.</summary>
        private static Material[] Fill(int count, Material material)
        {
            if (material == null)
                return null;

            Material[] result = new Material[Mathf.Max(1, count)];

            for (int i = 0; i < result.Length; i++)
                result[i] = material;

            return result;
        }
    }
}
