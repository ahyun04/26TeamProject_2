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
    /// [이유] (17b) 무중력은 판 중 사건이다 — 변형에서 팀원 이동 부품(PlayerMovement)을 켜 두고, 매 틱 맨 앞에서 사건 관리자에 "지금 무중력 이동인가"를
    ///  물어 팀원 부품의 켜짐을 맞춘다: 평소 = 켬(팀원 부품이 이동 · 시점 · 스태미나를 맡음, 이 부품은 아무것도 안 함) · 무중력 = 끔(이 부품이 움직임).
    ///  실행 순서 −10 = 팀원 부품(0)보다 먼저 → 바꾼 틱에 바로 반영. 매 틱 다시 맞추므로 되감아 다시 계산하는 틱도 그 틱의 단계를 따른다.
    ///  들어가는 틱에 움직이던 속도를 이어받고 중력 0, 나오는 틱에 속도 0 · 원래 중력(Physics.gravity) — 들어갔나 여부는 [Networked].
    ///  무중력 동안 팀원 이동 부품이 하던 예외(죽음 · 판 시작 대기 · 기절 · 관전자 게임 종료)는 그대로 따른다.
    ///  Space(점프)는 쓰지 않는다 (기획서 조작표에 없음). Shift 는 위쪽 이동이라 달리기가 없다 → 스태미나에 "달리지 않음"을 알린다.
    ///  (17c) 뒤집기: 마우스 · A/D · Shift/Ctrl 을 그 틱의 뒤집힌 각도만큼 돌리고(내 화면 기준), 떠오르기 동안 위로 1 m 를 관성과 따로 더한다.
    ///  각도 · 속도는 사건 일정에서 나온다 (새 [Networked] 없음). 화면 · 몸 돌리기용 각도는 RenderRollDegrees
    ///  (17d) 발밑 면: 무중력 단계에 캡슐을 화면 아래로 훑어 발밑 면에 닿은 시간을 세고([Networked] floorTouch), 3초가 넘으면
    ///  화면 위로 민다 — 떠오르기처럼 관성과 따로 더하고, 1 m 넘게 떨어져 놓는 틱에 밀던 속도를 관성에 넘긴다 (ZeroGFloat.StepFloor).
    ///  닿음 글자는 화면 기준. 흔들림(그리기)이 읽는 FloatsNow · BobPhase
    ///  (17d 테스트 후) 들어가는 틱에 캡슐을 위 · 아래로 훑어 방 높이 가운데까지 올라갈 거리를 [Networked] riseHeight 에 적고,
    ///  되돌기(59 ~ 60초) 동안은 중력을 켜고 위아래 이동을 빼 화면이 돌면서 떨어진다 (17d 명세 9장 ZG91 ~ ZG94).
    ///  무중력 이동 중에는 SimpleKCC 땅 붙이기를 끈다 — 뒤집혀 머리 쪽이 바닥에 닿으면 땅으로 보고 끌어내려 Ctrl 로 못 떨어지던 문제 (ZG101)
    /// [근거] 17a 명세 ZG13 · ZG14 · ZG15 · ZG21, 17b 명세 ZG50 · ZG57 · ZG58 (17a ZG8 "팀원 이동 부품 꺼 둠" 바뀜, 진단 ZG64 는 ZG66 에서 지움),
    ///  17c 명세 ZG68 · ZG71 · ZG72 · ZG76, 17d 명세 ZG81 · ZG83 ~ ZG86, 기획서 3 · 4 · 5 · 7장
    /// </summary>
    [DefaultExecutionOrder(-10)]
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

        /// <summary>17b: 무중력 이동에 들어가 있나 — 들어감 · 나옴을 한 번씩만 하려고 (되감아도 같은 틱에 — ZG57)</summary>
        [Networked] private NetworkBool zeroGActive { get; set; }

        /// <summary>(17d) 발밑 면(화면 아래)에 닿아 있는 시간(초) — 3초가 넘으면 화면 위로 민다. 밀기 속도는 여기서 나온다 (ZG83 · ZG84)</summary>
        [Networked] private float floorTouch { get; set; }

        /// <summary>(17d 테스트 후) 떠오르기 동안 올라갈 거리(m) — 들어가는 틱에 재서 방 높이 가운데까지 (ZG91 · ZG92). 사람마다 다르고 되감아도 같음</summary>
        [Networked] private float riseHeight { get; set; }

        /// <summary>(17d) 발밑 훑기 캡슐을 줄이는 양(m) — 면에 붙어 있어도 훑기 시작에 겹치지 않고 잡히게 (Claude 제안 — 명세 6장 위험 3)</summary>
        private const float FloorProbeShrink = 0.02f;

        private readonly RaycastHit[] floorHits = new RaycastHit[16];   // (17d 테스트 후) 8 → 16 — 위 · 아래 20 m 를 훑으면 여러 개가 맞을 수 있어서 (ZG92)
        private float floorDistance = float.PositiveInfinity;

        private PlayerMovement teamMovement;   // 17b: 평소 이동을 맡는 팀원 부품 — 켜짐만 바꾼다 (ZG57)

        /// <summary>(17d 테스트 후) 평소 땅 붙이기 거리 — 생길 때 SimpleKCC 설정에서 읽어 둔다 (팀원 프리팹 값, 기본 0.25 m — ZG101)</summary>
        private float teamSnapDistance;

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

        /// <summary>마지막 틱에 무엇에 막혔는지: 없음 · 벽 · 천장 · 바닥 — (17d) 내 화면 기준 (뒤집히면 원래 천장 = 바닥, ZG86)</summary>
        public string ContactLabel => Object != null && Object.IsValid
            ? ZeroGMotion.DescribeContact(blocked, ZeroGFlip.ScreenUp(transform.right, Vector3.up, RenderRollDegrees()))
            : "없음";

        /// <summary>(17d) 발밑 면에 닿은 시간(초 — 최대 4) · 그 밀기 속도(m/s) — 테스트 글자용 (ZG87)</summary>
        public float FloorTouchSeconds => Object != null && Object.IsValid ? floorTouch : 0f;
        public float FloorPushSpeed => ZeroGFloat.PushSpeed(FloorTouchSeconds);

        /// <summary>(17d) 마지막으로 계산한 틱에 잰 발밑 면 거리(m) — 재지 않았거나 1 m 남짓 안에 없으면 무한 (테스트 글자용)</summary>
        public float FloorDistance => floorDistance;

        /// <summary>(17d 테스트 후) 이번 무중력에서 떠오를 거리(m) — 테스트 글자용 (ZG96)</summary>
        public float RiseHeightUsed => Object != null && Object.IsValid ? riseHeight : 0f;

        /// <summary>(17d) 흔들림을 보일까 — 무중력 이동 중이고 살아 있을 때 ([Networked] 값이라 다른 사람 화면에서도 같음 — ZG81 · ZG82)</summary>
        public bool FloatsNow => ZeroGActive && (health == null || !health.IsDead);

        /// <summary>(17d) 이 사람의 흔들림 시작점 0 ~ 1 (NetworkId 에서 — 모든 PC 가 같은 값, 사람마다 다름 · ZG81)</summary>
        public float BobPhase => Object != null && Object.IsValid ? ZeroGFloat.PhaseFor(Object.Id.Raw) : 0f;

        public ZeroGMovementSettings Settings => settings;

        /// <summary>17b: 지금 무중력 이동 중인가 ([Networked] — 다른 사람 화면에서도 같음. 날기 동작 · 테스트 글자가 읽는다)</summary>
        public bool ZeroGActive => Object != null && Object.IsValid && zeroGActive;

        /// <summary>
        /// (17c) 화면 · 몸 돌리기용 뒤집힌 각도(도) — 무중력 이동 중이고 살아 있을 때만 (ZG71 · ZG74).
        /// 그리는 시각: 본인 · 호스트 = LocalRenderTime, 다른 사람 복제본 = RemoteRenderTime (그 사람의 보간된 위치와 같은 시각 — 떠오르기와 뒤집기가 같이 보이게)
        /// </summary>
        public float RenderRollDegrees()
        {
            if (!ZeroGActive || (health != null && health.IsDead))
                return 0f;

            float time = HasInputAuthority || HasStateAuthority ? (float)Runner.LocalRenderTime : (float)Runner.RemoteRenderTime;
            return ZeroGEventManager.RollDegreesAt(Runner, time);
        }

        public override void Spawned()
        {
            kcc = GetComponent<SimpleKCC>();
            teamSnapDistance = kcc.Settings != null ? kcc.Settings.SnapDistance : 0f;   // 17d 테스트 후 (ZG101)
            health = GetComponent<PlayerHealth>();
            stamina = GetComponent<PlayerStamina>();
            teamMovement = GetComponent<PlayerMovement>();

            if (health == null)
                gameEndSystem = FindFirstObjectByType<GameEndSystem>();

            // 17b: 중력은 무중력에 들어가는 틱에 0 으로 (평소에는 팀원 이동 부품이 SimpleKCC 기본 중력으로 움직인다 — ZG57)

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

            // 17b: 이 틱이 무중력 이동인가 — 사건 관리자의 단계 (모든 PC 가 같은 틱 시각으로 같은 답 — ZG57). 관리자가 없는 씬은 늘 평소 (ZG58)
            bool zeroG = ZeroGEventManager.UsesZeroGMovementNow(Runner);
            SetTeamMovement(!zeroG);   // 평소 = 켬 → 이 틱에 팀원 부품이 돈다 (실행 순서 −10 이라 그보다 먼저 맞춤)
            SetGroundSnap(!zeroG);     // 17d 테스트 후: 무중력 이동 중 땅 붙이기 끔 (ZG101)

            if (!zeroG)
            {
                Halted = false;

                if (simulates && zeroGActive)
                    Leave();

                return;   // 평소: 이동 · 시점 · 스태미나는 팀원 몫
            }

            if (simulates && !zeroGActive)
                Enter();

            // 무중력: 매 틱 중력 0 (한 번 넣은 값이 다른 곳에서 바뀌어도 다시 맞춘다).
            // 17d 테스트 후: 되돌기(59 ~ 60초)만 원래 중력 — 화면이 돌면서 떨어진다 (ZG93 · ZG94)
            bool falling = ZeroGEventManager.FallsNow(Runner);

            if (simulates)
                kcc.SetGravity(falling ? Physics.gravity.y : 0f);   // SimpleKCC 중력은 위아래 크기 하나 (float)

            if (health != null && health.IsDead)
            {
                stamina?.updateStamina(default, false);

                if (simulates)
                {
                    velocity = Vector3.zero;
                    blocked = Vector3.zero;
                    floorTouch = 0f;   // 17d: 죽으면 세지 않음 (ZG85)
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

                    // 17d: 기절 중에도 발밑 면을 세고 민다 (환경이 하는 일 — ZG85). 관성이 0 이라 놓는 틱에 넘길 속도는 버린다 (명세 6장 위험 5)
                    Vector3 stunUp = ScreenUpNow();
                    ZeroGFloorStep stunFloor = StepFloor(stunUp, false);
                    kcc.Move(Vector3.up * ZeroGEventManager.RiseSpeedNow(Runner, riseHeight) + stunUp * stunFloor.Push);   // 17c: 기절 중에도 떠오른다 (ZG76). 되돌기면 중력으로 떨어짐 (ZG94)
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
            bool holdDown = false;
            float up = 0f;

            if (hasInput)
                UpdateLook(input);

            // 17c: 내 화면 기준 축 — 뒤집히면 A/D 반대 · Shift = 머리 쪽 (ZG72). 시점을 돌린 뒤에 잡는다.
            // 17d: 발밑 밀기도 같은 화면 위 축으로 (ZG84) — 입력이 빠진 틱에도 필요해서 밖으로 뺐다
            Vector3 flatRight = kcc.TransformRotation * Vector3.right;
            float roll = ZeroGEventManager.RollDegreesNow(Runner);
            Vector3 screenUp = ZeroGFlip.ScreenUp(flatRight, Vector3.up, roll);

            if (hasInput)
            {
                halt = input.Buttons.IsSet(HaltButton);
                holdDown = input.IsPressed(InputButton.Walk);   // 17d: Ctrl(발 쪽 이동)을 누르는 동안 발밑을 세지 않음 (ZG85)
                up = falling ? 0f : (input.IsPressed(InputButton.Sprint) ? 1f : 0f) - (holdDown ? 1f : 0f);   // 17d 테스트 후: 되돌기 동안 Shift/Ctrl 무시 (ZG94)
                Vector2 move = Vector2.ClampMagnitude(input.MoveDirection, 1f);
                wish = ZeroGMotion.WishDirection(kcc.LookDirection, ZeroGFlip.ScreenRight(flatRight, Vector3.up, roll),
                    screenUp, move.y, move.x, up);

                if (falling)
                    wish = ZeroGMotion.Horizontal(wish);   // 17d 테스트 후: 위를 보고 W 를 눌러도 날지 않음 — 위아래는 중력만 (ZG94)
            }

            Halted = halt;

            // 17d: 발밑 면 (ZG83 ~ ZG85) — Ctrl · 멈춤이면 0. 밀기를 놓는 틱에는 밀던 속도를 관성에 넘긴 뒤 이번 틱 계산
            ZeroGFloorStep floor = StepFloor(screenUp, halt || holdDown);

            ZeroGMotionSettings motion = settings != null ? settings.ToMotion() : ZeroGMotionSettings.Default;
            Vector3 next = ZeroGMotion.Step(velocity + screenUp * floor.Handover, wish, halt, blocked, motion, Runner.DeltaTime);

            if (falling)
                next = ZeroGMotion.Horizontal(next);   // 17d 테스트 후: 관성의 위아래도 빼고 중력에 맡김 (ZG94)

            velocity = next;

            // 17c: 떠오르기 속도는 관성(velocity)에 쌓지 않고 이번 이동에만 더한다 → 떠오르기가 끝나면 위로 흐르지 않음.
            // 천장 · 물건에 막히면 SimpleKCC 가 그 자리에서 멈춘다 (ZG68). 17d: 발밑 밀기도 같은 까닭으로 따로 (관성 감속이 더 커서 — ZG84)
            Vector3 moved = next + Vector3.up * ZeroGEventManager.RiseSpeedNow(Runner, riseHeight) + screenUp * floor.Push;   // 17d 테스트 후: 사람마다 잰 높이 (ZG91)
            kcc.Move(moved);
            // 17d 테스트 후: 되돌기 동안 중력으로 떨어지는 속도는 막힌 것이 아니다 → 수평끼리만 비교 (ZG94)
            blocked = falling
                ? ZeroGMotion.Blocked(ZeroGMotion.Horizontal(moved), ZeroGMotion.Horizontal(kcc.RealVelocity))
                : ZeroGMotion.Blocked(moved, kcc.RealVelocity);
        }

        // ═════════════════════════════════════════════════════════════
        //  17d 발밑 면 (ZG83 ~ ZG85)
        // ═════════════════════════════════════════════════════════════

        /// <summary>이번 틱의 화면 위 축 (기절 중처럼 입력을 읽지 않는 틱용)</summary>
        private Vector3 ScreenUpNow()
        {
            return ZeroGFlip.ScreenUp(kcc.TransformRotation * Vector3.right, Vector3.up, ZeroGEventManager.RollDegreesNow(Runner));
        }

        /// <summary>
        /// 이번 틱 발밑 면: 무중력 단계이고 Ctrl · 멈춤이 아닐 때만 캡슐을 화면 아래로 훑어 거리를 잰다 →
        /// ZeroGFloat.StepFloor → [Networked] 닿은 시간에 쓰고 밀기 · 넘길 속도를 돌려준다
        /// </summary>
        private ZeroGFloorStep StepFloor(Vector3 screenUp, bool holdDown)
        {
            bool floating = ZeroGEventManager.FloatingNow(Runner);
            floorDistance = floating && !holdDown ? ProbeFloor(-screenUp) : float.PositiveInfinity;

            ZeroGFloorStep step = ZeroGFloat.StepFloor(floorTouch, floating, holdDown, floorDistance, Runner.DeltaTime);
            floorTouch = step.Touch;
            return step;
        }

        /// <summary>
        /// 발밑 면까지 거리(m): 캡슐을 down 쪽으로 1 m 남짓 훑는다 (ProbeSurface — 화면 위를 45° 안으로 보는 면만).
        /// ⚠️ 다른 사람 캡슐도 같은 층이면 면으로 잡힌다 (명세 6장 위험 2)
        /// </summary>
        private float ProbeFloor(Vector3 down)
        {
            return ProbeSurface(down, ZeroGFloat.ReleaseDistance + 0.05f);
        }

        /// <summary>
        /// (17d 테스트 후 — ZG91 · ZG92) 무중력에 들어가는 틱: 캡슐을 아래 · 위로 20 m 까지 훑어 방 높이 가운데까지 올라갈 거리.
        /// 위나 아래를 못 찾으면 관리자의 떠오르는 높이 (1 m — 17c ZG76). ⚠️ 머리 위 물건 · 다른 사람 캡슐도 면으로 잡힌다 (명세 9장 위험 7 · 8)
        /// </summary>
        private float MeasureRise()
        {
            float below = ProbeSurface(Vector3.down, ZeroGFlip.CenterProbeRange);
            float above = ProbeSurface(Vector3.up, ZeroGFlip.CenterProbeRange);
            return ZeroGFlip.CenterRise(below, above, ZeroGEventManager.RiseHeightNow(Runner));
        }

        /// <summary>
        /// 캡슐에서 direction 쪽 면까지 거리(m): SimpleKCC 캡슐을 반지름 2 cm 줄여 reach 남짓 훑는다 (그 러너의 물리 장면 · SimpleKCC 가 부딪히는 층).
        /// 내 몸 · 트리거는 빼고, 훑는 쪽과 마주 보는 45° 안 면만 (벽은 빼고). 없으면 무한. (17d 테스트 후: 발밑 훑기를 방향 · 거리로 넓힘)
        /// </summary>
        private float ProbeSurface(Vector3 direction, float reach)
        {
            if (direction.sqrMagnitude < 1e-6f)
                return float.PositiveInfinity;

            direction = direction.normalized;
            float radius = kcc.Settings.Radius;
            float height = Mathf.Max(kcc.Settings.Height, radius * 2f);
            Vector3 bottom = kcc.Position + Vector3.up * radius;            // 아래 반구 가운데 (발 위 반지름)
            Vector3 top = kcc.Position + Vector3.up * (height - radius);    // 위 반구 가운데
            float castRadius = Mathf.Max(0.01f, radius - FloorProbeShrink);

            int count = Runner.GetPhysicsScene().CapsuleCast(bottom, top, castRadius, direction, floorHits,
                reach + FloorProbeShrink, kcc.Settings.CollisionLayerMask, QueryTriggerInteraction.Ignore);

            float best = float.PositiveInfinity;

            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = floorHits[i];

                if (hit.collider == null || hit.collider.transform.IsChildOf(transform))
                    continue;

                if (!ZeroGFloat.IsFloorSurface(hit.normal, -direction))
                    continue;

                best = Mathf.Min(best, Mathf.Max(0f, hit.distance - FloorProbeShrink));   // 줄인 만큼 빼면 실제 캡슐에서 면까지
            }

            return best;
        }

        private void UpdateLook(NetworkInputData input)
        {
            // 17c: 마우스 움직임을 그 틱의 뒤집힌 각도만큼 돌린다 — 180° 이면 좌우 · 위아래 모두 반대 (내 화면 기준, ZG72). 위아래 한계 ±80 그대로
            Vector2 look = ZeroGFlip.RotateLook(input.LookRotation, ZeroGEventManager.RollDegreesNow(Runner));
            kcc.AddLookRotation(new Vector2(-look.y, look.x), minimumPitch, maximumPitch);
        }

        // ═════════════════════════════════════════════════════════════
        //  17b 평소 ↔ 무중력 바꾸기 (ZG57)
        // ═════════════════════════════════════════════════════════════

        /// <summary>
        /// (17d 테스트 후 — ZG101) SimpleKCC 땅 붙이기: 평소 = 팀원 프리팹 값, 무중력 이동 중 = 0 (끔).
        /// SimpleKCC 는 이동할 때마다 땅에서 SnapDistance 안이면 SnapSpeed(4 m/s) 로 다시 붙인다 (dll 의 GroundSnapProcessor) →
        /// 뒤집혀 머리 쪽(캡슐 아래 끝)이 원래 바닥에 닿으면 Ctrl(세계 위 2 m/s)을 끌어내리고, 그 차이를 막힌 속도로 읽어 위 속도가 0.1 m/s 근처에 머물렀다
        /// (진단 로그 — 명세 9-6). 설정은 되감기지 않아 팀원 부품 켜짐처럼 매 틱 그 틱의 단계로 맞춘다. 실행 중 부품 값만 바꾼다 (에셋은 그대로)
        /// </summary>
        private void SetGroundSnap(bool on)
        {
            if (kcc.Settings == null)
                return;

            float distance = on ? teamSnapDistance : 0f;

            if (kcc.Settings.SnapDistance != distance)
                kcc.Settings.SnapDistance = distance;
        }

        private void SetTeamMovement(bool on)
        {
            // 켜짐 · 꺼짐은 되감기지 않는다 → 바꿀 때만이 아니라 매 틱 그 틱의 단계로 맞춘다 (명세 6장 위험 2)
            if (teamMovement != null && teamMovement.enabled != on)
                teamMovement.enabled = on;
        }

        /// <summary>무중력에 들어가는 틱: 움직이던 속도를 이어받고(최대 속도를 넘는 부분은 이번 틱 Step 이 자른다) 중력 0</summary>
        private void Enter()
        {
            zeroGActive = true;
            floorTouch = 0f;   // 17d
            riseHeight = MeasureRise();   // 17d 테스트 후: 방 높이 가운데까지 (ZG91)
            Vector3 real = kcc.RealVelocity;
            velocity = IsFinite(real) ? real : Vector3.zero;   // 기획서 4장 관성 — 이어받기는 Claude 제안 (ZG57)
            blocked = Vector3.zero;
            kcc.SetGravity(0f);   // SimpleKCC 중력은 위아래 크기 하나 (float)
        }

        /// <summary>무중력에서 나오는 틱: 속도 0 · 원래 중력 → 다음 틱부터 팀원 이동 부품이 중력으로 떨어뜨린다</summary>
        private void Leave()
        {
            zeroGActive = false;
            velocity = Vector3.zero;
            blocked = Vector3.zero;
            floorTouch = 0f;   // 17d
            riseHeight = 0f;   // 17d 테스트 후
            // 원래 중력 = SimpleKCC 가 생길 때 넣는 Physics.gravity (dll 확인 — 17b 명세 1-1).
            // ⚠️ 팀원이 SetGravity 로 다른 중력을 쓰게 되면 이 값도 맞춰야 한다 (명세 6장 위험 4)
            kcc.SetGravity(Physics.gravity.y);
        }

        private static bool IsFinite(Vector3 v)
        {
            return !(float.IsNaN(v.x) || float.IsNaN(v.y) || float.IsNaN(v.z) || float.IsInfinity(v.x) || float.IsInfinity(v.y) || float.IsInfinity(v.z));
        }

    }
}
