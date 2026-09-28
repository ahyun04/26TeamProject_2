using UnityEngine;

namespace TrustNoOne.Missions
{
    /// <summary>
    /// [역할] 피스톤 하나(A/B/C)의 모습: 압력계 바늘 · 피스톤 추 · 표시등. 상태는 PressureStation 이 매 Render 에 Apply 로 넣어 준다. 2d 명세 3-4.
    ///  - 바늘: 압력계 중심의 회전축 오브젝트(needlePivot)를 높이에 따라 minAngle ~ maxAngle 로 돌린다 (PR10 — 바늘 메시 피벗이 중심이라는 보장이 없어
    ///    변환기가 압력계 중심에 회전축을 만들어 바늘을 그 아래로 옮겼다)
    ///  - 추: 원래 위치 기준 weightAxis 방향으로 bottomOffset ~ topOffset 을 오르내린다
    ///  - 표시등(PR11): 움직임 = 원래 색 / 멈춤 = 빨강 / 고정 = 초록. MaterialPropertyBlock 으로 그 재질 칸만 칠한다 (재질 복제 없음).
    ///    색 값은 태우님 Neon_green · Neon_red 재질에서 가져왔다. 표시등 칸이 없으면(-1) 칠하지 않는다.
    ///    [대체 표시 — PR13] 지금 모델에는 피스톤별 네온 램프가 없어(네온은 패널 본체 하나뿐) 변환기가 압력계 자체(아틀라스 칸)를
    ///    기본색만 초록 · 빨강으로 칠하게 설정한다 (applyEmission = false). 램프 모델이 들어오면 변환기가 네온 칸을 찾아 자동으로 바뀐다.
    /// [보정] 각도 · 오프셋 기본값은 변환기가 추정해 넣는다. 화면을 보고 인스펙터에서 조정한다 (명세 7장).
    /// </summary>
    public class PressureVisual : MonoBehaviour
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

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

        [Header("표시등")]
        [SerializeField] private Renderer lampRenderer;
        [Tooltip("표시등 재질 칸. -1 이면 표시등 없음")]
        [SerializeField] private int lampMaterialIndex = -1;
        [SerializeField] private Color lockedBase = new Color(0.511f, 1f, 0.626f);
        [ColorUsage(true, true)]
        [SerializeField] private Color lockedEmission = new Color(0.165f, 2.505f, 0.202f);
        [SerializeField] private Color stalledBase = new Color(1f, 0.346f, 0.338f);
        [ColorUsage(true, true)]
        [SerializeField] private Color stalledEmission = new Color(5.574f, 0.2f, 0.256f);

        [Tooltip("발광색도 바꿀지. 네온 램프 칸이면 true, 압력계 자체를 칠하는 대체 표시(아틀라스 재질)면 false — 아틀라스의 발광을 꺼 버리지 않게")]
        [SerializeField] private bool applyEmission = true;

        private Quaternion needleStart;
        private Vector3 weightStart;
        private bool initialized;
        private PistonState? shownState;
        private MaterialPropertyBlock propertyBlock;

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
        }

        private void ApplyLamp(PistonState state)
        {
            if (shownState == state)
                return;

            shownState = state;

            if (lampRenderer == null || lampMaterialIndex < 0)
                return;

            if (propertyBlock == null)
                propertyBlock = new MaterialPropertyBlock();

            if (state == PistonState.Moving)
            {
                // 원래 색: 이 칸의 블록을 비운다
                propertyBlock.Clear();
                lampRenderer.SetPropertyBlock(propertyBlock, lampMaterialIndex);
                return;
            }

            bool locked = state == PistonState.Locked;
            lampRenderer.GetPropertyBlock(propertyBlock, lampMaterialIndex);
            propertyBlock.SetColor(BaseColorId, locked ? lockedBase : stalledBase);

            if (applyEmission)
                propertyBlock.SetColor(EmissionColorId, locked ? lockedEmission : stalledEmission);

            lampRenderer.SetPropertyBlock(propertyBlock, lampMaterialIndex);
        }
    }
}
