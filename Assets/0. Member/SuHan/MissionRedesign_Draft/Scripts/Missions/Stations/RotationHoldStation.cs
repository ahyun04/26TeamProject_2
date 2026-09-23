using Fusion;
using UnityEngine;

namespace TrustNoOne.Missions
{
    /// <summary>
    /// [역할] "F 를 누르고 있는 동안 무언가를 돌리는" 미니게임 공통 부모 (밸브, 안테나). 2a 명세 2-2.
    ///  - 이번 판 목표 회전량(기본 360~720°)을 호스트가 무작위로 정한다 → 유지 시간 = 목표 각도 / 회전 속도 (90°/초면 4~8초)
    ///  - 표시 각도는 동기화된 홀드 진행률에서 계산한다. 각도용 네트워크 변수를 따로 두지 않아 진행률과 어긋날 일이 없다.
    ///  - 손을 떼거나 중단하면 진행률이 0 이 되므로 처음 각도로 바로 돌아간다 (명세 P1, 되감기 연출 없음 P8).
    ///  - 초기화(ResetForNext · 제한 시간)마다 목표 각도를 다시 뽑는다 → 다음 사람은 새 목표.
    ///
    /// [옛 코드] ValveMission / AntennaMission 의 회전 부분이 거의 같아 여기로 합쳤다.
    /// </summary>
    public class RotationHoldStation : HoldStation
    {
        [Header("회전")]
        [SerializeField] private Transform rotatingPart;

        [Tooltip("로컬 회전축. 0 벡터면 FallbackAxis 를 쓴다.")]
        [SerializeField] private Vector3 rotationAxis = Vector3.up;

        [Tooltip("초당 회전 각도.")]
        [Min(1f)]
        [SerializeField] private float rotationSpeed = 90f;

        [Header("목표 회전량 (판마다 무작위)")]
        [SerializeField] private float minTargetAngle = 360f;
        [SerializeField] private float maxTargetAngle = 720f;

        [Networked] private float TargetAngle { get; set; }

        private Quaternion startRotation;

        /// <summary>rotationAxis 가 비어 있을 때 쓸 축. 하위 클래스가 바꾼다 (밸브는 정면 축).</summary>
        protected virtual Vector3 FallbackAxis => Vector3.up;

        protected override float HoldDuration => Mathf.Max(TargetAngle, 1f) / Mathf.Max(rotationSpeed, 1f);

        /// <summary>지금 표시할 누적 회전 각도.</summary>
        protected float CurrentAngle => HoldProgress01 * TargetAngle;

        private void Awake()
        {
            if (rotatingPart != null)
                startRotation = rotatingPart.localRotation;
        }

        protected override void OnStationSpawned()
        {
            base.OnStationSpawned();

            if (HasStateAuthority && TargetAngle <= 0f)
                RollTargetAngle();

            ApplyRotation();
        }

        protected override void OnResetHost()
        {
            base.OnResetHost();
            RollTargetAngle();
        }

        protected override void OnStationRender()
        {
            base.OnStationRender();
            ApplyRotation();
        }

        private void RollTargetAngle()
        {
            float min = Mathf.Max(1f, Mathf.Min(minTargetAngle, maxTargetAngle));
            float max = Mathf.Max(min, Mathf.Max(minTargetAngle, maxTargetAngle));
            TargetAngle = Random.Range(min, max);
        }

        private void ApplyRotation()
        {
            if (rotatingPart == null || Object == null || !Object.IsValid)
                return;

            Vector3 axis = rotationAxis.sqrMagnitude > 0f ? rotationAxis.normalized : FallbackAxis;
            rotatingPart.localRotation = startRotation * Quaternion.AngleAxis(CurrentAngle, axis);
        }
    }
}
