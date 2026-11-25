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
    ///  (17b) 무중력은 판 중 사건이라, 무중력 이동에 들어간 동안(ZeroGMovement.ZeroGActive — [Networked], 다른 사람 화면에서도 같음)만
    ///  Animator 에 날기 컨트롤러 복사본을 끼우고 숙인다. 평소에는 팀원 원래 컨트롤러 · 숙이지 않음.
    ///  (17c) 뒤집기: 숙인 뒤 몸 모델을 몸 가운데(발 위 0.7 m)를 중심으로 몸 앞 축으로 뒤집힌 각도만큼 돌린다 — 모든 PC 에서 (ZeroGMovement.RenderRollDegrees).
    ///  아이템 손 따라가기(0)보다 먼저라 들고 있는 아이템도 같이 뒤집힌다. 죽은 몸은 돌리지 않는다
    ///  (17d) 흔들림: 뒤집은 뒤 가만히 있으면(0.3 m/s 이하) 몸 모델을 뒤집힌 몸의 위 축으로 ±4 cm · 3초 주기로 옮긴다 — 모든 PC,
    ///  보이는 것만 (충돌체 · 카메라는 그대로 — 카메라 흔들림은 ZeroGCameraRoll). 팀원 코드가 몸 자리를 매 프레임 새로 써 쌓이지 않는다
    /// [근거] 17a 명세 ZG33 · ZG34 · ZG35 (테스트 후 추가), 17b 명세 ZG59 (17a ZG33 "변형 Animator = 날기 복사본" 바뀜), 17c 명세 ZG74 · ZG75,
    ///  17d 명세 ZG81 · ZG82
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

        [Header("날기 동작 (17b — 무중력 동안만, ZG59)")]
        [Tooltip("몸 Animator — 평소에는 팀원 원래 컨트롤러 (메뉴가 채운다)")]
        [SerializeField] private Animator animator;

        [Tooltip("무중력 동안 끼울 날기 컨트롤러 복사본 Player_ZeroG.controller (메뉴가 채운다)")]
        [SerializeField] private RuntimeAnimatorController flyController;

        [Header("뒤집기 (17c — ZG74)")]
        [Tooltip("뒤집는 중심 높이(m, 발 기준) — 팀원 Player 프리팹 SimpleKCC 키 1.4 의 절반 (Claude 제안). 화면 돌리기(ZeroGCameraRoll)와 같은 값")]
        [SerializeField] private float bodyCenterHeight = ZeroGFlip.DefaultBodyCenterHeight;

        [Header("떠 있는 흔들림 (17d — 보이는 것만, ZG81 · ZG82)")]
        [Tooltip("몸 모델 흔들림 크기(m, 몸 위아래 ±) — 피드백 \"위아래로 살짝살짝\" ±4 cm. 카메라 흔들림(ZeroGCameraRoll 의 같은 이름 칸)과 따로")]
        [SerializeField, Min(0f)] private float bobAmplitude = ZeroGFloat.BobAmplitude;

        private ZeroGMovement movement;
        private float bobWeight;   // 흔들림 세기 0 ~ 1 (가만히 = 1 쪽으로 1초에 걸쳐)
        private RuntimeAnimatorController normalController;
        private bool flying;
        private SimpleKCC kcc;
        private PlayerHealth health;
        private Behaviour lookVisual;   // 팀원 몸 방향 코드 — 꺼져 있으면 숙이지 않는다 (회전이 쌓이지 않게)
        private float current;

        private void Awake()
        {
            kcc = GetComponent<SimpleKCC>();
            health = GetComponent<PlayerHealth>();
            lookVisual = GetComponent("PlayerLookVisualComponent") as Behaviour;
            movement = GetComponent<ZeroGMovement>();

            if (animator != null)
                normalController = animator.runtimeAnimatorController;
        }

        private void LateUpdate()
        {
            bool zeroG = movement != null && movement.ZeroGActive;
            SwapController(zeroG);

            if (bodyPivot == null || hips == null || kcc == null || kcc.Object == null || !kcc.Object.IsValid)
                return;

            if (lookVisual == null || !lookVisual.isActiveAndEnabled)
                return;

            float target = 0f;
            bool alive = health == null || !health.IsDead;

            if (zeroG && alive)
            {
                Vector3 velocity = kcc.RealVelocity;
                velocity.y = 0f;   // 날기 동작(이동 묶음)도 수평 속력으로 고른다 — 위아래만 움직이면 Fly_idle · 숙이지 않음
                target = ZeroGMotion.BodyLeanAngle(velocity.magnitude, startSpeed, fullLeanSpeed, leanAngle);
            }

            current = Mathf.MoveTowards(current, target, turnSpeed * Time.deltaTime);

            // 17c: 뒤집는 축 = 몸 앞 — 팀원 코드가 몸을 수평으로 맞춘 직후의 오른쪽 축에서, 숙이기 전에 잡는다
            Vector3 bodyForward = Vector3.Cross(bodyPivot.right, Vector3.up);

            if (Mathf.Abs(current) >= 0.01f)
                bodyPivot.RotateAround(hips.position, bodyPivot.right, current);

            float roll = movement != null ? movement.RenderRollDegrees() : 0f;

            if (Mathf.Abs(roll) >= 0.01f && bodyForward.sqrMagnitude > 1e-6f)
                bodyPivot.RotateAround(transform.position + Vector3.up * bodyCenterHeight, bodyForward.normalized, roll);

            // 17d: 가만히 있으면 뒤집힌 몸의 위 축으로 흔들림 — 뒤집기와 같은 회전으로 위 축을 돌린다 (보이는 것만, ZG81)
            bool still = zeroG && alive && ZeroGFloat.IsStill(kcc.RealVelocity.magnitude);
            bobWeight = ZeroGFloat.StepBobWeight(bobWeight, still, ZeroGFloat.BobFadeSeconds, Time.deltaTime);
            float bob = ZeroGFloat.BobOffset(Time.time, movement != null ? movement.BobPhase : 0f, bobAmplitude * bobWeight, ZeroGFloat.BobPeriod);

            if (Mathf.Abs(bob) >= 1e-4f)
            {
                Vector3 bodyUp = Mathf.Abs(roll) >= 0.01f && bodyForward.sqrMagnitude > 1e-6f
                    ? Quaternion.AngleAxis(roll, bodyForward.normalized) * Vector3.up
                    : Vector3.up;
                bodyPivot.position += bodyUp * bob;
            }
        }

        /// <summary>무중력에 들어가고 나올 때 한 번씩 컨트롤러를 바꿔 끼운다 (바꾸면 동작이 처음 상태에서 다시 시작 — 명세 6장 위험 3)</summary>
        private void SwapController(bool zeroG)
        {
            if (zeroG == flying || animator == null || flyController == null || normalController == null)
                return;

            flying = zeroG;
            animator.runtimeAnimatorController = zeroG ? flyController : normalController;
        }
    }
}
