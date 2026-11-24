using Fusion.Addons.SimpleKCC;
using UnityEngine;

namespace TrustNoOne.Gimmicks
{
    /// <summary>
    /// 날 때 몸을 앞으로 숙이기 (무중력 플레이어 프리팹 변형에 붙는다 — 모든 PC 에서 보이는 3인칭 몸).
    /// [역할] 팀원 몸 방향 코드(PlayerLookVisualComponent — 실행 순서 −50, LateUpdate 에서 몸 모델을 매 프레임 수평으로 맞춤) 바로 뒤에,
    ///  몸 모델(bodyPivot)을 엉덩이(Hips) 위치를 중심으로 몸의 오른쪽 축 기준 앞으로 기울인다. 각도는 수평 실제 속력으로 정한다 (ZeroGMotion.BodyLeanAngle).
    /// [이유] 날기 클립(Fly_move)에 엉덩이 뼈 회전 곡선이 없어 몸이 서 있는 채로 허우적거린다 (애니메이션 담당 피드백 — 사용자 결정 C:
    ///  코드로 먼저 숙이고, 클립이 고쳐지면 Lean Angle 을 0 으로). 카메라 · 충돌체는 몸 모델과 따로라 기울지 않는다.
    ///  매 프레임 팀원 코드가 몸 회전을 새로 쓰므로 이 회전은 쌓이지 않는다. 팀원 코드가 꺼져 있으면(쌓일 수 있음) 아무것도 하지 않는다.
    ///  들고 있는 아이템이 손을 따라가는 처리(기본 순서 0)보다 먼저 돌아야 아이템이 숙인 손에 붙는다 → 순서 −45.
    /// [근거] 17a 명세 ZG33 · ZG34 · ZG35 (테스트 후 추가)
    /// </summary>
    [DefaultExecutionOrder(-45)]
    public class ZeroGBodyLean : MonoBehaviour
    {
        [Tooltip("몸 모델 — 팀원 몸 방향 코드의 Body Pivot 과 같은 것 (메뉴가 채운다)")]
        [SerializeField] private Transform bodyPivot;

        [Tooltip("숙이는 중심 — 엉덩이 뼈 (메뉴가 몸 모델 아래에서 이름 Hips 로 찾아 채운다)")]
        [SerializeField] private Transform hips;

        [Header("숙이기 (Play 중 맞춰 보는 값 — 클립이 고쳐지면 Lean Angle 0)")]
        [Tooltip("최대로 숙이는 각도 (피드백 \"x축 30도\")")]
        [SerializeField] private float leanAngle = 30f;

        [Tooltip("이 수평 속력(m/s) 이하면 숙이지 않음 (Fly_idle)")]
        [SerializeField, Min(0f)] private float startSpeed = 0.3f;

        [Tooltip("이 수평 속력(m/s) 이상이면 최대로 숙임")]
        [SerializeField, Min(0f)] private float fullLeanSpeed = 1f;

        [Tooltip("각도가 바뀌는 빠르기 (초당 도)")]
        [SerializeField, Min(0f)] private float turnSpeed = 90f;

        private SimpleKCC kcc;
        private PlayerHealth health;
        private Behaviour lookVisual;   // 팀원 몸 방향 코드 — 꺼져 있으면 숙이지 않는다 (회전이 쌓이지 않게)
        private float current;

        private void Awake()
        {
            kcc = GetComponent<SimpleKCC>();
            health = GetComponent<PlayerHealth>();
            lookVisual = GetComponent("PlayerLookVisualComponent") as Behaviour;
        }

        private void LateUpdate()
        {
            if (bodyPivot == null || hips == null || kcc == null || kcc.Object == null || !kcc.Object.IsValid)
                return;

            if (lookVisual == null || !lookVisual.isActiveAndEnabled)
                return;

            float target = 0f;

            if (health == null || !health.IsDead)
            {
                Vector3 velocity = kcc.RealVelocity;
                velocity.y = 0f;   // 날기 동작(이동 묶음)도 수평 속력으로 고른다 — 위아래만 움직이면 Fly_idle · 숙이지 않음
                target = ZeroGMotion.BodyLeanAngle(velocity.magnitude, startSpeed, fullLeanSpeed, leanAngle);
            }

            current = Mathf.MoveTowards(current, target, turnSpeed * Time.deltaTime);

            if (Mathf.Abs(current) < 0.01f)
                return;

            bodyPivot.RotateAround(hips.position, bodyPivot.right, current);
        }
    }
}
