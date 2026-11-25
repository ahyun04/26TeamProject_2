using System.Collections.Generic;
using Fusion;
using TrustNoOne.Missions;
using UnityEngine;

namespace TrustNoOne.Gimmicks
{
    /// <summary>
    /// [역할] 무중력 사건의 "계획"을 정하고 나눈다. 씬에 NetworkObject 와 함께 1개 둔다 (같은 오브젝트에 화면 부품 ZeroGEventView).
    ///  - 호스트: 미션 배정이 끝난 순간(= 판 시작, MissionManager.OnMissionsAssigned)에 판 시작 시각만 적고, 다음 틱에
    ///    암전 관리자의 계획을 읽어 ZeroGSchedule.Pick 으로 경고 시작 시각을 뽑아 [Networked] 에 쓴다 (구간 길이도 함께)
    ///  - 모든 PC: 같은 [Networked] 값과 같은 틱 시각(Runner.SimulationTime)으로 Evaluate 를 계산한다
    ///    → 이동 부품(ZeroGMovement)이 그 틱의 단계로 평소 ↔ 무중력 이동을 바꾸고, 화면 부품이 경고 · 게이지를 그린다
    /// [이유] 접근법 1 (사용자 결정) — 플레이어를 다시 만들지 않고, 늦게 들어온 PC 도 [Networked] 로 같은 값을 받는다 (16a 암전 관리자와 같은 방식).
    ///  두 관리자가 배정 알림을 받는 순서는 정해져 있지 않아, 뽑기는 알림 다음 틱에 한다 (사건은 가장 빨라도 120초 뒤라 한 틱 늦어도 됨).
    ///  (17c) 뒤집힌 각도 · 떠오르기 속도도 같은 계획 · 같은 시각으로 계산한다 (ZeroGFlip) — 새로 나누는 값은 떠오르는 높이 하나
    ///  (17d 테스트 후) 떠오르는 높이는 사람마다 재고(ZeroGMovement) 이 값은 못 찾을 때만 · 되돌기 동안 중력을 켜라고 알림 (FallsNow — 17d 명세 9장 ZG92 · ZG93)
    /// [근거] 17b 명세 ZG44 · ZG46 · ZG50 · ZG53 · ZG54 · ZG55 · ZG63, 17c 명세 ZG68 · ZG71 · ZG76
    /// </summary>
    public class ZeroGEventManager : NetworkBehaviour
    {
        [Header("판 길이")]
        [Tooltip("씬에 GameTimer 가 없을 때 쓰는 판 길이(초). 있으면 GameTimer 의 전체 시간 (16a KB12 와 같음 — GamePlay · 4~12 씬 값 500)")]
        [SerializeField] private float fallbackRoundLength = BlackoutSchedule.DefaultRoundLength;

        [Header("무중력 시간 (17b 명세 ZG53 — 판 시작 때 [Networked] 로 나눔)")]
        [Tooltip("판 시작 후 이 시간(초) 동안은 무중력 없음 — 질문 2 \"120초 뒤 랜덤한 때\"")]
        [SerializeField] private float firstDelay = ZeroGSchedule.DefaultFirstDelay;
        [Tooltip("경고 문구 + 카운트다운 길이(초) — 피드백 \"5초 뒤에 무중력 상태에 들어갑니다\"")]
        [SerializeField] private float warningSeconds = ZeroGSchedule.DefaultWarningSeconds;
        [Tooltip("무중력 길이(초) = 게이지 길이 (떠오르기 포함) — 질문 2. 테스트에서 줄여 보려면 이 값을 바꾼 뒤 F2")]
        [SerializeField] private float zeroGSeconds = ZeroGSchedule.DefaultZeroGSeconds;
        [Tooltip("떠오르기(초) — 무중력 길이 안. 이 동안 떠오르며 몸을 180° 돌린다 (17c ZG68 — 2초는 Claude 제안)")]
        [SerializeField] private float riseSeconds = ZeroGSchedule.DefaultRiseSeconds;
        [Tooltip("되돌기(초) — 무중력 길이의 끝. 중력이 켜져 떨어지면서 몸을 되돌리고, 끝나는 순간(게이지 0) 평소 이동 (17d 테스트 후 ZG93 — 기본 1초)")]
        [SerializeField] private float returnSeconds = ZeroGSchedule.DefaultReturnSeconds;
        [Tooltip("천장 · 바닥을 못 찾을 때 떠오르는 높이(m) — 17c 질문 2 A. 보통은 사람마다 재서 방 높이 가운데로 (17d 테스트 후 ZG91 · ZG92)")]
        [SerializeField] private float riseHeight = ZeroGFlip.DefaultRiseHeight;
        [Tooltip("살인마 암전과 앞뒤로 떨어질 거리(초) — 질문 4")]
        [SerializeField] private float blackoutGap = ZeroGSchedule.DefaultBlackoutGap;

        // ───────────── 공개 상태 ([Networked], 전원에게 복제) ─────────────

        /// <summary>판 시작 시각 (Runner.SimulationTime) — 배정 끝 알림을 받은 순간</summary>
        [Networked] public float RoundStartTime { get; private set; }

        /// <summary>배정 끝 알림을 받았나 — 거짓이면 아직 판 전</summary>
        [Networked] public NetworkBool RoundStarted { get; private set; }

        /// <summary>호스트: 다음 틱에 뽑을 차례 (ZG55)</summary>
        [Networked] private NetworkBool PickPending { get; set; }

        /// <summary>이번 판 계획이 있나 (없으면 판이 짧거나 암전을 피할 자리가 없음)</summary>
        [Networked] public NetworkBool Planned { get; private set; }

        /// <summary>판 시작 후 몇 초에 경고가 시작되나</summary>
        [Networked] public float StartOffset { get; private set; }

        // 구간 길이 — 호스트가 Play 중 Inspector 로 바꿔도 모든 PC 가 같은 값을 쓰게 계획과 함께 나눈다 (ZG53)
        [Networked] public float WarningSeconds { get; private set; }
        [Networked] public float ZeroGSeconds { get; private set; }
        [Networked] public float RiseSeconds { get; private set; }
        [Networked] public float ReturnSeconds { get; private set; }

        /// <summary>(17c) 떠오르는 높이(m) — 계획과 함께 나눈다 (ZG76 — 구간 길이와 같은 까닭)</summary>
        [Networked] public float RiseHeight { get; private set; }

        private MissionManager missionManager;

        /// <summary>씬의 사건 관리자 (없으면 null — 그 씬은 무중력 없음, ZG58)</summary>
        public static ZeroGEventManager Instance { get; private set; }

        /// <summary>
        /// 이 러너에서 지금 틱에 무중력 이동을 쓰나 (ZeroGMovement 가 매 틱 맨 앞에서 묻는다 — ZG57).
        /// 관리자가 없거나 다른 러너의 것이면 거짓 (평소 이동).
        /// </summary>
        public static bool UsesZeroGMovementNow(NetworkRunner runner)
        {
            ZeroGEventManager manager = Instance;

            if (manager == null || manager.Object == null || !manager.Object.IsValid || manager.Runner != runner)
                return false;

            return ZeroGSchedule.UsesZeroGMovement(manager.Evaluate().Phase);
        }

        /// <summary>(17c) 이 러너의 지금 틱 뒤집힌 각도(도) — 조작 돌리기용 (ZeroGMovement — 되감아도 같은 틱이면 같은 값, ZG71 · ZG72)</summary>
        public static float RollDegreesNow(NetworkRunner runner)
        {
            return runner != null ? RollDegreesAt(runner, (float)runner.SimulationTime) : 0f;
        }

        /// <summary>(17c) 주어진 Runner 시각의 뒤집힌 각도(도) — 화면 · 몸 돌리기용, 그릴 때는 LocalRenderTime · RemoteRenderTime (ZG71)</summary>
        public static float RollDegreesAt(NetworkRunner runner, float time)
        {
            ZeroGEventManager manager = Find(runner);

            if (manager == null || !manager.RoundStarted)
                return 0f;

            return ZeroGFlip.RollDegrees(ZeroGSchedule.Evaluate(manager.Plan, time - manager.RoundStartTime));
        }

        /// <summary>
        /// (17c) 이번 틱에 위로 더할 떠오르기 속도(m/s) — ZeroGMovement 가 관성과 따로 더한다 (ZG68 · ZG76).
        /// (17d 테스트 후) 높이는 그 사람이 들어갈 때 잰 값 (방 높이 가운데까지 — ZG91)
        /// </summary>
        public static float RiseSpeedNow(NetworkRunner runner, float height)
        {
            ZeroGEventManager manager = Find(runner);

            if (manager == null || !manager.RoundStarted)
                return 0f;

            return ZeroGFlip.RiseSpeed(manager.Plan, manager.Elapsed, runner.DeltaTime, height);
        }

        /// <summary>(17d 테스트 후) 천장 · 바닥을 못 찾을 때 떠오르는 높이 — 관리자 [Networked] RiseHeight, 관리자가 없으면 기본 1 m (ZG92)</summary>
        public static float RiseHeightNow(NetworkRunner runner)
        {
            ZeroGEventManager manager = Find(runner);
            return manager != null ? manager.RiseHeight : ZeroGFlip.DefaultRiseHeight;
        }

        /// <summary>(17d 테스트 후) 이 러너의 지금 틱에 무중력 이동 중 중력을 켜나 — 되돌기 1초 (ZeroGMovement — 되감아도 같은 틱이면 같은 답, ZG93 · ZG94)</summary>
        public static bool FallsNow(NetworkRunner runner)
        {
            ZeroGEventManager manager = Find(runner);
            return manager != null && ZeroGSchedule.UsesGravity(manager.Evaluate().Phase);
        }

        /// <summary>
        /// (17d) 이 러너의 지금 틱이 무중력 단계(완전히 뒤집힌 2 ~ 59초 — 17d 테스트 후 되돌기 1초)인가 — 발밑 닿은 시간은 이때만 센다
        /// (ZeroGMovement — 되감아도 같은 틱이면 같은 답, 17d ZG83 · 질문 2 A). 떠오르기 · 되돌기는 거짓
        /// </summary>
        public static bool FloatingNow(NetworkRunner runner)
        {
            ZeroGEventManager manager = Find(runner);
            return manager != null && manager.Evaluate().Phase == ZeroGPhase.Floating;
        }

        /// <summary>이 러너의 살아 있는 관리자 (없거나 다른 러너의 것이면 null)</summary>
        private static ZeroGEventManager Find(NetworkRunner runner)
        {
            ZeroGEventManager manager = Instance;
            return manager != null && manager.Object != null && manager.Object.IsValid && manager.Runner == runner ? manager : null;
        }

        /// <summary>지금 계획 (없으면 ZeroGPlan.None)</summary>
        public ZeroGPlan Plan => Planned
            ? new ZeroGPlan(StartOffset, new ZeroGTiming(WarningSeconds, ZeroGSeconds, RiseSeconds, ReturnSeconds))
            : ZeroGPlan.None;

        /// <summary>판 시작 후 지난 초 (판 시작 전이면 0)</summary>
        public float Elapsed => RoundStarted ? (float)Runner.SimulationTime - RoundStartTime : 0f;

        /// <summary>지금 틱의 상태 (판 시작 전 · 계획 없음이면 단계 없음)</summary>
        public ZeroGState Evaluate()
        {
            if (Object == null || !Object.IsValid || !RoundStarted)
                return ZeroGState.Of(ZeroGPhase.None);

            return ZeroGSchedule.Evaluate(Plan, Elapsed);
        }

        // ═════════════════════════════════════════════════════════════
        //  수명 주기
        // ═════════════════════════════════════════════════════════════

        public override void Spawned()
        {
            Instance = this;

            if (!HasStateAuthority)
                return;

            // 미션 관리자도 씬에 놓인 오브젝트라 처음부터 찾을 수 있다 (암전 관리자와 같음)
            missionManager = FindFirstObjectByType<MissionManager>();

            if (missionManager == null)
            {
                Debug.LogWarning("[ZeroGEventManager] 씬에 MissionManager 가 없어 판 시작을 알 수 없습니다 — 이번 판 무중력 없음");
                return;
            }

            missionManager.OnMissionsAssigned += OnMissionsAssigned;
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            if (missionManager != null)
                missionManager.OnMissionsAssigned -= OnMissionsAssigned;

            if (Instance == this)
                Instance = null;
        }

        public override void FixedUpdateNetwork()
        {
            if (HasStateAuthority && PickPending)
            {
                PickPending = false;
                PickPlan();
            }
        }

        // ═════════════════════════════════════════════════════════════
        //  계획 (호스트)
        // ═════════════════════════════════════════════════════════════

        /// <summary>호스트: 미션 배정이 끝난 순간 = 판 시작. 시각만 적고 뽑기는 다음 틱 (ZG55)</summary>
        private void OnMissionsAssigned(IReadOnlyList<PlayerRef> citizens, IReadOnlyList<PlayerRef> killers)
        {
            RoundStartTime = (float)Runner.SimulationTime;
            RoundStarted = true;
            Planned = false;
            PickPending = true;
        }

        private void PickPlan()
        {
            float roundLength = GetRoundLength();
            BlackoutPlan blackout = ReadBlackoutPlan(out string blackoutNote);
            ZeroGTiming timing = CurrentTiming();
            ZeroGPlan plan = ZeroGSchedule.Pick(roundLength, firstDelay, timing, blackout, blackoutGap, Random.value);
            ApplyPlan(plan);

            if (plan.Planned)
                Debug.Log($"[ZeroGEventManager] 계획 — 경고 시작 {plan.StartOffset:0.0}초 · 무중력 {plan.Timing.ZeroGSeconds:0.#}초 " +
                          $"(경고 {plan.Timing.WarningSeconds:0.#} · 떠오르기 {plan.Timing.RiseSeconds:0.#} · 되돌기 {plan.Timing.ReturnSeconds:0.#} · 못 찾을 때 높이 {RiseHeight:0.#} m) · 판 길이 {roundLength:0}초 · {blackoutNote}");
            else
                Debug.Log($"[ZeroGEventManager] 이번 판 무중력 없음 — 판 길이 {roundLength:0}초 · 첫 대기 {firstDelay:0}초 · 사건 전체 {timing.Span:0}초 · {blackoutNote}");
        }

        /// <summary>
        /// (테스트 — ZG63, 테스트 글자 F2) 호스트: 지금 경고를 시작한다 (길이는 지금 Inspector 값, 암전 피하기 없음).
        /// 판 시작 전이면 지금을 판 시작으로 둔다. 사건 중(경고 ~ 돌아오기)이면 하지 않는다.
        /// </summary>
        public bool StartNowForTest(out string message)
        {
            if (!HasStateAuthority)
            {
                message = "호스트만 바로 시작할 수 있습니다";
                return false;
            }

            ZeroGPhase phase = Evaluate().Phase;

            if (phase != ZeroGPhase.None && phase != ZeroGPhase.Waiting && phase != ZeroGPhase.Ended)
            {
                message = $"이미 사건 중 ({phase}) — 끝난 뒤 다시";
                return false;
            }

            if (!RoundStarted)
            {
                RoundStartTime = (float)Runner.SimulationTime;
                RoundStarted = true;
            }

            PickPending = false;
            ZeroGTiming timing = CurrentTiming();
            float now = Elapsed;
            // 같은 규칙(Pick)을 쓴다 — 판 길이를 "지금 + 사건 전체"로, 첫 대기를 지금으로 주면 시작 = 지금
            ZeroGPlan plan = ZeroGSchedule.Pick(now + timing.Span, now, timing, BlackoutPlan.None, 0f, 0f);
            ApplyPlan(plan);

            message = plan.Planned
                ? $"(테스트) 바로 시작 — 경고 {plan.Timing.WarningSeconds:0.#}초 뒤 무중력 {plan.Timing.ZeroGSeconds:0.#}초"
                : "(테스트) 무중력 길이가 0 이라 시작하지 못함";
            Debug.Log($"[ZeroGEventManager] {message}");
            return plan.Planned;
        }

        private void ApplyPlan(ZeroGPlan plan)
        {
            Planned = plan.Planned;
            StartOffset = plan.StartOffset;
            WarningSeconds = plan.Timing.WarningSeconds;
            ZeroGSeconds = plan.Timing.ZeroGSeconds;
            RiseSeconds = plan.Timing.RiseSeconds;
            ReturnSeconds = plan.Timing.ReturnSeconds;
            RiseHeight = riseHeight > 0f ? riseHeight : 0f;   // 17c: NaN · 음수 → 0 (뜨지 않음)
        }

        private ZeroGTiming CurrentTiming()
        {
            return ZeroGSchedule.Normalize(new ZeroGTiming(warningSeconds, zeroGSeconds, riseSeconds, returnSeconds));
        }

        /// <summary>
        /// 암전 관리자의 계획을 이 관리자의 판 시작 기준으로 옮겨 읽는다 (ZG55). 없거나 아직 판 전이면 없음.
        /// 두 관리자는 같은 배정 알림에서 판 시작을 적으므로 보통 차이 0 — 그래도 시각 차이만큼 옮긴다.
        /// </summary>
        private BlackoutPlan ReadBlackoutPlan(out string note)
        {
            KillerBlackoutManager blackout = FindFirstObjectByType<KillerBlackoutManager>();

            if (blackout == null || blackout.Object == null || !blackout.Object.IsValid)
            {
                note = "암전 관리자 없음 (피하기 없이)";
                return BlackoutPlan.None;
            }

            if (!blackout.RoundStarted || !blackout.Planned)
            {
                note = blackout.RoundStarted ? "이번 판 암전 없음" : "암전 관리자가 판 시작을 모름 (피하기 없이)";
                return BlackoutPlan.None;
            }

            float shift = blackout.RoundStartTime - RoundStartTime;
            note = $"암전 {blackout.StartOffset + shift:0.0}초부터 {blackout.Duration:0.0}초 → 앞뒤 {blackoutGap:0}초 피함";
            return new BlackoutPlan(blackout.StartOffset + shift, blackout.Duration);
        }

        /// <summary>판 길이: 씬에 GameTimer 가 있으면 그 전체 시간, 없으면 Inspector 값 (16a KB12 와 같음)</summary>
        private float GetRoundLength()
        {
            GameTimer timer = FindFirstObjectByType<GameTimer>();
            return timer != null ? timer.TotalDurationSeconds : fallbackRoundLength;
        }
    }
}
