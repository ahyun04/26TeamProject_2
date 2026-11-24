using Fusion;
using Fusion.Addons.SimpleKCC;
using LockdownProtocol.Lobby;
using UnityEngine;

namespace TrustNoOne.Gimmicks
{
    /// <summary>
    /// 무중력 이동 부품 (플레이어 · 관전자 무중력 프리팹 변형에 붙는다).
    /// [역할] 매 틱 입력 → 시점 → ZeroGMotion.Step → Photon 이동 부품(SimpleKCC)에 중력 0 · 그 속도로 이동 → 막힌 속도 기록.
    ///  속도와 막힌 속도는 [Networked] — 호스트와 본인 PC 가 되감아 다시 계산할 때도 관성이 같게 맞는다.
    /// [이유] 팀원 이동 부품(PlayerMovement)은 변형에서 꺼 둔다 (지우지 않음 — 다른 팀원 코드가 GetComponent 로 찾는다).
    ///  팀원 이동 부품이 하던 예외(죽음 · 판 시작 대기 · 기절 · 관전자 게임 종료)는 그대로 따른다.
    ///  Space(점프)는 쓰지 않는다 (기획서 조작표에 없음). Shift 는 위쪽 이동이라 달리기가 없다 → 스태미나에 "달리지 않음"을 알린다.
    /// [근거] 17a 명세 ZG8 · ZG13 · ZG14 · ZG15 · ZG21 · ZG22, 기획서 3 · 4 · 5 · 7장
    /// </summary>
    [RequireComponent(typeof(SimpleKCC))]
    public class ZeroGMovement : NetworkBehaviour
    {
        /// <summary>
        /// 멈춤을 싣는 입력 버튼 칸 (ZG16). 팀원 입력 구조체의 버튼 32칸 중 0 ~ 2 (점프 · 달리기 · 걷기)만 쓰여 가장 끝 칸을 쓴다.
        /// ⚠️ 팀원이 InputButton 에 칸을 더해 31번까지 쓰면 겹친다 (17a 명세 6장 위험 6).
        /// </summary>
        public const int HaltButton = 31;

        [Tooltip("무중력 이동 설정 에셋 (메뉴 SuHan/Build Zero-G Test Scene 이 연결). 비어 있으면 기본값 (ZG11)")]
        [SerializeField] private ZeroGMovementSettings settings;

        [Header("시점 (팀원 이동 부품과 같은 한계)")]
        [SerializeField] private float minimumPitch = -80f;
        [SerializeField] private float maximumPitch = 80f;

        [Networked] private Vector3 velocity { get; set; }
        [Networked] private Vector3 blocked { get; set; }

        private SimpleKCC kcc;
        private PlayerHealth health;
        private PlayerStamina stamina;
        private GameEndSystem gameEndSystem;   // 체력 없는 관전자의 게임 종료 중 이동 차단 (팀원 이동 부품과 같음)
        private ZeroGHaltInput haltInput;

        /// <summary>본인 PC 의 무중력 이동 부품 (테스트 글자가 읽는다 — ZG21)</summary>
        public static ZeroGMovement Local { get; private set; }

        /// <summary>지금 속력 (m/s)</summary>
        public float Speed => Object != null && Object.IsValid ? velocity.magnitude : 0f;

        /// <summary>마지막 틱에 멈춤 칸이 켜져 있었는지</summary>
        public bool Halted { get; private set; }

        /// <summary>마지막 틱에 무엇에 막혔는지: 없음 · 벽 · 천장 · 바닥</summary>
        public string ContactLabel => Object != null && Object.IsValid ? ZeroGMotion.DescribeContact(blocked) : "없음";

        public ZeroGMovementSettings Settings => settings;

        public override void Spawned()
        {
            kcc = GetComponent<SimpleKCC>();
            health = GetComponent<PlayerHealth>();
            stamina = GetComponent<PlayerStamina>();

            if (health == null)
                gameEndSystem = FindFirstObjectByType<GameEndSystem>();

            kcc.SetGravity(0f);   // SimpleKCC 중력은 위아래 크기 하나 (float)

            if (settings == null)
                Debug.LogWarning($"[ZeroGMovement] {name}: 설정 에셋이 비어 있어 기본값으로 움직입니다 (메뉴 SuHan/Build Zero-G Test Scene 이 연결).");

            if (!HasInputAuthority)
                return;

            Local = this;

            // 멈춤 입력은 상호작용이 있는 플레이어만 (관전자는 상호작용이 없다 — ZG17)
            if (GetComponent<PlayerInteraction>() != null)
            {
                haltInput = gameObject.AddComponent<ZeroGHaltInput>();
                haltInput.Begin(Runner);
            }
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            if (haltInput != null)
            {
                haltInput.End(runner);
                Destroy(haltInput);
                haltInput = null;
            }

            if (Local == this)
                Local = null;
        }

        public override void FixedUpdateNetwork()
        {
            if (kcc == null)
                return;

            bool simulates = HasStateAuthority || HasInputAuthority;

            // 무중력: 매 틱 중력 0 (한 번 넣은 값이 다른 곳에서 바뀌어도 다시 맞춘다)
            if (simulates)
                kcc.SetGravity(0f);   // SimpleKCC 중력은 위아래 크기 하나 (float)

            if (health != null && health.IsDead)
            {
                stamina?.updateStamina(default, false);

                if (simulates)
                {
                    velocity = Vector3.zero;
                    blocked = Vector3.zero;
                    kcc.SetActive(false);
                }

                return;
            }

            RoomManager room = RoomManager.Instance;
            bool isStarting = room != null && room.Object != null && room.Object.IsValid &&
                              room.Runner == Runner && room.CurrentRoomState == RoomManager.RoomState.Starting;
            bool isGameEnded = health == null && gameEndSystem != null && gameEndSystem.Object != null &&
                               gameEndSystem.Object.IsValid && gameEndSystem.IsGameEnded;

            if (isStarting || isGameEnded || (health != null && !health.CanAct))
            {
                stamina?.updateStamina(default, false);

                if (simulates)
                {
                    // 기절 중에도 시점은 돈다 (팀원 이동 부품과 같음). 이동은 없음 → 속도 0 (17a 계획 보충 1)
                    if (health != null && health.isStunned && health.canControlLook && GetInput(out NetworkInputData lookInput))
                        UpdateLook(lookInput);

                    velocity = Vector3.zero;
                    blocked = Vector3.zero;
                    kcc.Move(Vector3.zero);
                }

                return;
            }

            stamina?.updateStamina(default, false);

            bool hasInput = GetInput(out NetworkInputData input);

            // 다른 사람 화면의 복제본은 움직이지 않는다 (위치는 Photon 이 맞춘다). 호스트는 입력이 빠진 틱에도 관성으로 움직인다
            if (!hasInput && !HasStateAuthority)
                return;

            Vector3 wish = Vector3.zero;
            bool halt = false;
            float up = 0f;

            if (hasInput)
            {
                UpdateLook(input);

                halt = input.Buttons.IsSet(HaltButton);
                up = (input.IsPressed(InputButton.Sprint) ? 1f : 0f) - (input.IsPressed(InputButton.Walk) ? 1f : 0f);
                Vector2 move = Vector2.ClampMagnitude(input.MoveDirection, 1f);
                Vector3 flatRight = kcc.TransformRotation * Vector3.right;
                wish = ZeroGMotion.WishDirection(kcc.LookDirection, flatRight, move.y, move.x, up);
            }

            Halted = halt;

            ZeroGMotionSettings motion = settings != null ? settings.ToMotion() : ZeroGMotionSettings.Default;
            Vector3 next = ZeroGMotion.Step(velocity, wish, halt, blocked, motion, Runner.DeltaTime);

            velocity = next;
            kcc.Move(next);
            blocked = ZeroGMotion.Blocked(next, kcc.RealVelocity);
        }

        private void UpdateLook(NetworkInputData input)
        {
            kcc.AddLookRotation(new Vector2(-input.LookRotation.y, input.LookRotation.x), minimumPitch, maximumPitch);
        }

    }
}
