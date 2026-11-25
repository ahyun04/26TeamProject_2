using Fusion.Addons.SimpleKCC;
using UnityEngine;

namespace TrustNoOne.Gimmicks
{
    /// <summary>
    /// 무중력 뒤집기 — 내 화면 돌리기 (플레이어 · 관전자 무중력 프리팹 변형에 붙는다, 본인 PC 에서만 움직인다).
    /// [역할] 팀원 카메라 코드(PlayerCameraController — 기본 순서 0, LateUpdate 에서 카메라 받침대 CameraPivot 의 회전을 위아래 시선으로 매 프레임 새로 씀)
    ///  바로 뒤(순서 100)에 받침대를 시선 축으로 뒤집힌 각도만큼 더 돌리고, 받침대 자리(눈)를 몸 가운데(발 위 0.7 m)를 중심으로 옮긴다.
    ///  각도는 ZeroGMovement.RenderRollDegrees (사건 일정에서 — 새 [Networked] 없음).
    /// [이유] 질문 1 A — 보는 곳(시선 · 조준점)은 그대로 두고 화면만 시선 축으로 180° → 상호작용 조준 · 공격 방향 · W 이동이 모두 시선과 같다.
    ///  팀원 코드가 회전은 매 프레임 새로 쓰므로 쌓이지 않지만 자리는 쓰지 않는다 → 각도가 0 으로 돌아오면 처음 자리로 되돌린다.
    ///  팀원 카메라 코드가 꺼져 있으면(회전이 쌓일 수 있음) 돌리지 않는다. 몸 숙이기(−45)와 한 부품에 넣지 않은 까닭: 그쪽은 아이템 손 따라가기(0)보다 먼저여야 한다.
    ///  (17d) 흔들림: 가만히 있으면(0.3 m/s 이하) 받침대 자리를 돌아간 화면 위 축으로 ±4 cm · 3초 주기로 더 옮긴다 (보이는 것만 — 실제 위치 · 충돌 그대로).
    ///  움직이면 1초에 걸쳐 사라진다. 멀미가 나면 Bob Amplitude 0 (몸 모델 흔들림은 ZeroGBodyLean 에서 따로)
    /// [근거] 17c 명세 ZG67 · ZG70 · ZG73 · ZG75, 17d 명세 ZG81 · ZG82
    /// </summary>
    [DefaultExecutionOrder(100)]
    public class ZeroGCameraRoll : MonoBehaviour
    {
        [Tooltip("카메라 받침대 — 팀원 카메라 코드의 Camera Pivot 과 같은 것 (메뉴가 채운다)")]
        [SerializeField] private Transform cameraPivot;

        [Tooltip("뒤집는 중심 높이(m, 발 기준) — 팀원 Player 프리팹 SimpleKCC 키 1.4 의 절반 (Claude 제안)")]
        [SerializeField] private float bodyCenterHeight = ZeroGFlip.DefaultBodyCenterHeight;

        [Header("떠 있는 흔들림 (17d — 보이는 것만, ZG81 · ZG82)")]
        [Tooltip("카메라 흔들림 크기(m, 화면 위아래 ±) — 피드백 \"위아래로 살짝살짝\" ±4 cm. 멀미가 나면 0 (몸 모델 흔들림은 ZeroGBodyLean 의 같은 이름 칸)")]
        [SerializeField, Min(0f)] private float bobAmplitude = ZeroGFloat.BobAmplitude;

        private ZeroGMovement movement;
        private SimpleKCC kcc;
        private float bobWeight;               // 흔들림 세기 0 ~ 1 (가만히 = 1 쪽으로 1초에 걸쳐)
        private Behaviour cameraController;   // 팀원 카메라 코드 — 꺼져 있으면 돌리지 않는다 (이름으로 찾음)
        private Vector3 restPosition;          // 받침대 처음 자리 (팀원 프리팹 — 눈 1.3 m)
        private bool moved;

        private void Awake()
        {
            movement = GetComponent<ZeroGMovement>();
            kcc = GetComponent<SimpleKCC>();
            cameraController = GetComponent("PlayerCameraController") as Behaviour;

            if (cameraPivot != null)
                restPosition = cameraPivot.localPosition;
        }

        private void LateUpdate()
        {
            if (cameraPivot == null)
                return;

            float roll = 0f;

            // 본인 화면만 (다른 사람 복제본의 카메라는 꺼져 있고, 팀원 코드도 본인 것만 회전을 새로 쓴다)
            bool mine = movement != null && movement.Object != null && movement.Object.IsValid && movement.HasInputAuthority
                        && cameraController != null && cameraController.isActiveAndEnabled;

            if (mine)
                roll = movement.RenderRollDegrees();

            // 17d: 가만히 있으면 화면 위아래로 흔들림 — 움직이면 1초에 걸쳐 사라짐 (보이는 것만, ZG81)
            bool still = mine && movement.FloatsNow && kcc != null && ZeroGFloat.IsStill(kcc.RealVelocity.magnitude);
            bobWeight = ZeroGFloat.StepBobWeight(bobWeight, still, ZeroGFloat.BobFadeSeconds, Time.deltaTime);
            float bob = mine ? ZeroGFloat.BobOffset(Time.time, movement.BobPhase, bobAmplitude * bobWeight, ZeroGFloat.BobPeriod) : 0f;

            if (Mathf.Abs(roll) < 0.01f && Mathf.Abs(bob) < 1e-4f)
            {
                if (moved)
                {
                    cameraPivot.localPosition = restPosition;
                    moved = false;
                }

                return;
            }

            // 받침대 자리 = 몸 가운데를 중심으로 돌린 눈 자리 + 돌아간 화면 위 축 × 흔들림.
            // 루트는 좌우로만 돌아 루트 기준 수평 오른쪽 = (1, 0, 0) — RollAround 와 같은 회전이라 화면 위와 딱 맞는다 (17d)
            cameraPivot.localPosition = ZeroGFlip.RollAround(restPosition, new Vector3(0f, bodyCenterHeight, 0f), roll)
                                        + ZeroGFlip.ScreenUp(Vector3.right, Vector3.up, roll) * bob;

            if (Mathf.Abs(roll) >= 0.01f)
                cameraPivot.localRotation = cameraPivot.localRotation * Quaternion.Euler(0f, 0f, roll);   // 시선(앞) 축 회전 — 보는 곳은 그대로

            moved = true;
        }

        private void OnDisable()
        {
            if (moved && cameraPivot != null)
            {
                cameraPivot.localPosition = restPosition;
                moved = false;
            }
        }
    }
}
