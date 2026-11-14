using System.Collections.Generic;
using Fusion;
using LockdownProtocol.Lobby;
using TrustNoOne.Missions;
using UnityEngine;

namespace TrustNoOne.Gimmicks
{
    /// <summary>
    /// [역할] 살인마 전용 암전의 "계획"을 정하고 나눈다. 씬에 NetworkObject 와 함께 1개 둔다 (같은 오브젝트에 KillerBlackoutView).
    ///  - 호스트: 미션 배정이 끝난 순간(= 판 시작, MissionManager.OnMissionsAssigned)에 살인마 목록을 적고,
    ///    BlackoutSchedule.Pick 으로 이번 판 계획(시작 · 길이)을 뽑아 [Networked] 에 쓴다
    ///  - 모든 PC: 같은 [Networked] 값과 같은 시각(Runner.SimulationTime)으로 Evaluate 를 계산한다 → 화면은 KillerBlackoutView 가 자기 것만 가린다
    ///  - 모든 PC (16c): 매 프레임 판 시작을 팀원 GameTimer 의 시작과 비교해 TimerComparison 에 두고, 처음 한 번 로그를 남긴다
    ///    (16a 위험 1 — 실제 게임 흐름에서 두 시계가 같은 순간에 시작하는지 기록으로 확인)
    /// [이유] 접근법 1 (사용자 결정) — 플레이어 프리팹(팀원)을 고치지 않고, 늦게 들어온 PC 도 [Networked] 로 같은 값을 받는다.
    ///  RPC 알림은 놓친 PC 가 어긋난다 (KB9). 서버 위치 · 이동 · 공격 · 상호작용 판정은 건드리지 않는다 (기획서 "서버 위치 · 이동 판정은 그대로").
    /// [근거] 16a 명세 KB9 · KB10 · KB11 · KB12 · KB13 · KB17 · KB21 · KB23, 16c 명세 KB50 · KB51
    /// </summary>
    public class KillerBlackoutManager : NetworkBehaviour
    {
        // 살인마 목록 칸 수 = 방 최대 인원 (KB13 — 팀원 Cctv 의 이용자 목록도 같은 값으로 잡는다)
        private const int MaxKillers = RoomManager.AbsoluteMaxPlayers;

        [Header("판 길이")]
        [Tooltip("씬에 GameTimer 가 없을 때 쓰는 판 길이(초). 있으면 GameTimer 의 전체 시간을 쓴다 (KB12 — GamePlay · 4~12 씬 값 500)")]
        [SerializeField] private float fallbackRoundLength = BlackoutSchedule.DefaultRoundLength;

        [Header("암전 시간 (기획서 — KB17)")]
        [Tooltip("판 시작 후 이 시간(초) 동안은 암전 없음 — 기획서 \"2분 동안은 없음\". 혼자 테스트 씬은 메뉴가 10 으로 둔다 (KB20)")]
        [SerializeField] private float firstDelay = BlackoutSchedule.DefaultFirstDelay;
        [Tooltip("지속 시간 최소(초) — 기획서 10~15초")]
        [SerializeField] private float minDuration = BlackoutSchedule.DefaultMinDuration;
        [Tooltip("지속 시간 최대(초) — 기획서 10~15초")]
        [SerializeField] private float maxDuration = BlackoutSchedule.DefaultMaxDuration;
        [Tooltip("발동 순간부터 어두워지는 시간(초). 지속 시간에 포함된다 (KB6). 0 = 바로 깜깜")]
        [SerializeField] private float fadeSeconds = BlackoutSchedule.DefaultFadeSeconds;

        [Header("테스트")]
        [Tooltip("(테스트) RestartForTest 를 부르면 지금부터 이 시간(초) 뒤에 다시 시작한다 (KB21 — 디버그 패널 F4)")]
        [SerializeField] private float testRestartDelay = 3f;

        // ───────────── 공개 상태 ([Networked], 전원에게 복제 — KB13) ─────────────

        /// <summary>판 시작 시각 (Runner.SimulationTime). 배정 끝 알림을 받은 순간 (KB10)</summary>
        [Networked] public float RoundStartTime { get; private set; }

        /// <summary>배정 끝 알림을 받았나 — 거짓이면 아직 판 전 (계획 "없음"과 구분)</summary>
        [Networked] public NetworkBool RoundStarted { get; private set; }

        /// <summary>이번 판 계획이 있나. 거짓이면 이번 판 암전 없음 (판이 너무 짧음 — KB3)</summary>
        [Networked] public NetworkBool Planned { get; private set; }

        /// <summary>판 시작 후 몇 초에 시작하나</summary>
        [Networked] public float StartOffset { get; private set; }

        /// <summary>지속 시간(초)</summary>
        [Networked] public float Duration { get; private set; }

        /// <summary>살인마 수 (Killers 앞에서부터 이만큼)</summary>
        [Networked] public int KillerCount { get; private set; }

        /// <summary>배정 순간의 살인마 목록 — 판 도중 바뀌지 않는다 (위험 5)</summary>
        [Networked, Capacity(MaxKillers)]
        public NetworkArray<PlayerRef> Killers => default;

        // 16c KB51: 타이머는 시작했는데 판 시작 알림이 이만큼(초) 넘게 없으면 경고 — 두 값이 손님 PC 에 따로 도착할 수 있는 여유
        private const float MissingRoundWarnSeconds = 2f;

        private MissionManager missionManager;

        // 16c KB51: 판 시작 비교 (모든 PC) — GameTimer 는 한 번만 찾는다 (없으면 "없음"으로 기억)
        private GameTimer gameTimer;
        private bool gameTimerSearched;
        private bool timerComparisonLogged;
        private bool missingRoundWarned;
        private float timerStartedSeenAt = -1f;

        /// <summary>씬에 GameTimer 가 있나 (혼자 테스트 씬에는 없다 — 디버그 패널 "타이머: 씬에 없음")</summary>
        public bool HasGameTimer => gameTimer != null;

        /// <summary>16c KB51 · KB52: 지금 프레임의 판 시작 비교 — 판 시작 알림과 타이머 시작이 둘 다 있을 때만 값이 있다</summary>
        public BlackoutTimerComparison? TimerComparison { get; private set; }

        /// <summary>발동 순간부터 어두워지는 시간(초) — 모든 PC 가 같은 씬 값을 쓴다</summary>
        public float FadeSeconds => fadeSeconds;

        /// <summary>지금 계획 (없으면 BlackoutPlan.None)</summary>
        public BlackoutPlan Plan => Planned ? new BlackoutPlan(StartOffset, Duration) : BlackoutPlan.None;

        /// <summary>판 시작 후 지난 초 (판 시작 전이면 0)</summary>
        public float Elapsed => RoundStarted ? (float)Runner.SimulationTime - RoundStartTime : 0f;

        /// <summary>지금 순간의 상태 (판 시작 전이면 단계 없음 — RoundStarted 로 구분한다)</summary>
        public BlackoutState Evaluate()
        {
            return RoundStarted
                ? BlackoutSchedule.Evaluate(Plan, Elapsed, fadeSeconds)
                : new BlackoutState(BlackoutPhase.None, 0f, 0f, 0f);
        }

        /// <summary>이 플레이어가 배정 순간의 살인마 목록에 있나 (KB14 — 죽음 · 탈출은 화면 쪽이 매 프레임 따로 본다)</summary>
        public bool IsKiller(PlayerRef player)
        {
            int count = Mathf.Min(KillerCount, MaxKillers);

            for (int i = 0; i < count; i++)
            {
                if (Killers[i] == player)
                    return true;
            }

            return false;
        }

        // ═════════════════════════════════════════════════════════════
        //  수명 주기 (호스트만 배정 알림을 듣는다)
        // ═════════════════════════════════════════════════════════════

        public override void Spawned()
        {
            if (!HasStateAuthority)
                return;

            // 미션 관리자도 씬에 놓인 오브젝트라 처음부터 찾을 수 있다. 배정(InitializeMissions)은 그 관리자가 준비된 뒤에 불린다
            missionManager = FindFirstObjectByType<MissionManager>();

            if (missionManager == null)
            {
                Debug.LogWarning("[KillerBlackoutManager] 씬에 MissionManager 가 없어 판 시작을 알 수 없습니다 — 이번 판 암전 없음");
                return;
            }

            missionManager.OnMissionsAssigned += OnMissionsAssigned;
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            if (missionManager != null)
                missionManager.OnMissionsAssigned -= OnMissionsAssigned;
        }

        public override void Render()
        {
            CompareWithGameTimer();
        }

        // ═════════════════════════════════════════════════════════════
        //  판 시작 비교 (모든 PC — 16c KB51)
        // ═════════════════════════════════════════════════════════════

        /// <summary>
        /// 판 시작(배정 알림)과 팀원 GameTimer 의 시작을 같은 PC 의 같은 시각으로 비교한다. 같은 틱에 정해지므로 차이는 0 이어야 한다 (16c 명세 1-2).
        /// 허용 오차 = 1틱 (Runner.DeltaTime). 처음 비교한 순간 로그 한 번 — 같으면 일반 로그, 어긋나면 경고.
        /// 타이머가 끝난 뒤(남은 0)에는 값을 바꾸지 않는다 — 마지막 비교를 그대로 둔다.
        /// </summary>
        private void CompareWithGameTimer()
        {
            if (!gameTimerSearched)
            {
                gameTimer = FindFirstObjectByType<GameTimer>();
                gameTimerSearched = true;
            }

            if (gameTimer == null || gameTimer.Object == null || !gameTimer.Object.IsValid || !gameTimer.IsStarted)
            {
                TimerComparison = null;
                return;
            }

            if (!RoundStarted)
            {
                TimerComparison = null;
                WarnIfRoundNeverStarted();
                return;
            }

            if (gameTimer.IsTimeUp)
                return;

            BlackoutTimerComparison comparison = BlackoutTimerCheck.Compare(Elapsed, gameTimer.TotalDurationSeconds, gameTimer.RemainingSeconds, Runner.DeltaTime);
            TimerComparison = comparison;

            if (timerComparisonLogged)
                return;

            timerComparisonLogged = true;
            string text = $"[KillerBlackoutManager] 판 시작 비교 — 암전 기준 {comparison.BlackoutElapsed:0.000}초 · 타이머 기준 {comparison.TimerElapsed:0.000}초 · 차이 {comparison.Gap:0.000}초 " +
                          (comparison.Matches ? $"(같음 — 1틱 {comparison.Tolerance:0.000}초 이내)" : $"(어긋남! — 1틱 {comparison.Tolerance:0.000}초 넘음)");

            if (comparison.Matches)
                Debug.Log(text);
            else
                Debug.LogWarning(text);
        }

        /// <summary>16c KB51: 타이머는 시작했는데 판 시작 알림이 2초 넘게 없으면 경고 한 번 (MissionManager 배정이 중간에 돌아간 판 — 16c 명세 1-2)</summary>
        private void WarnIfRoundNeverStarted()
        {
            if (missingRoundWarned)
                return;

            if (timerStartedSeenAt < 0f)
                timerStartedSeenAt = Time.unscaledTime;

            if (Time.unscaledTime - timerStartedSeenAt <= MissingRoundWarnSeconds)
                return;

            missingRoundWarned = true;
            Debug.LogWarning("[KillerBlackoutManager] 타이머는 시작됐는데 판 시작 알림이 없음 — 이번 판 암전 없음 (MissionManager 의 미션 배정이 중간에 돌아갔는지 확인)");
        }

        // ═════════════════════════════════════════════════════════════
        //  계획 (호스트)
        // ═════════════════════════════════════════════════════════════

        /// <summary>호스트: 미션 배정이 끝난 순간 = 판 시작 (KB10). 살인마 목록을 적고 이번 판 계획을 뽑는다.</summary>
        private void OnMissionsAssigned(IReadOnlyList<PlayerRef> citizens, IReadOnlyList<PlayerRef> killers)
        {
            int count = 0;

            foreach (PlayerRef killer in killers)
            {
                if (count >= MaxKillers)
                {
                    Debug.LogError($"[KillerBlackoutManager] 살인마 {killers.Count}명이 목록 칸 수({MaxKillers})를 넘어 일부는 암전되지 않습니다.");
                    break;
                }

                Killers.Set(count++, killer);
            }

            KillerCount = count;
            RoundStartTime = (float)Runner.SimulationTime;
            RoundStarted = true;

            float roundLength = GetRoundLength();
            BlackoutPlan plan = BlackoutSchedule.Pick(roundLength, firstDelay, minDuration, maxDuration, Random.value, Random.value);
            ApplyPlan(plan);

            if (plan.Planned)
                Debug.Log($"[KillerBlackoutManager] 계획 — 시작 {plan.StartOffset:0.0}초 · 길이 {plan.Duration:0.0}초 · 살인마 {KillerCount}명 (판 길이 {roundLength:0}초)");
            else
                Debug.Log($"[KillerBlackoutManager] 이번 판 암전 없음 — 판 길이 {roundLength:0}초 · 첫 대기 {firstDelay:0}초 · 최대 길이 {maxDuration:0}초");
        }

        /// <summary>
        /// (테스트 — KB21, 디버그 패널 F4) 호스트: 계획을 "지금부터 testRestartDelay 초 뒤 시작"으로 다시 잡고 길이도 다시 뽑는다.
        /// 같은 규칙(Pick)을 쓴다 — 판 길이를 "지금 + 대기 + 최대 길이"로 주고 시작 무작위 값을 0 으로 두면 시작 = 지금 + 대기.
        /// 판 시작 전이면 아무것도 하지 않는다.
        /// </summary>
        public bool RestartForTest()
        {
            if (!HasStateAuthority || !RoundStarted)
                return false;

            float startAt = Elapsed + Mathf.Max(0f, testRestartDelay);
            float longEnough = startAt + Mathf.Max(minDuration, maxDuration);
            BlackoutPlan plan = BlackoutSchedule.Pick(longEnough, startAt, minDuration, maxDuration, Random.value, 0f);
            ApplyPlan(plan);

            Debug.Log($"[KillerBlackoutManager] (테스트) 다시 잡음 — 시작 {plan.StartOffset:0.0}초 (지금 +{testRestartDelay:0.#}초) · 길이 {plan.Duration:0.0}초 · 살인마 {KillerCount}명");
            return plan.Planned;
        }

        private void ApplyPlan(BlackoutPlan plan)
        {
            Planned = plan.Planned;
            StartOffset = plan.StartOffset;
            Duration = plan.Duration;
        }

        /// <summary>판 길이: 씬에 GameTimer 가 있으면 그 전체 시간, 없으면 Inspector 값 (KB12 — 혼자 테스트 씬에는 GameTimer 가 없다)</summary>
        private float GetRoundLength()
        {
            GameTimer timer = FindFirstObjectByType<GameTimer>();
            return timer != null ? timer.TotalDurationSeconds : fallbackRoundLength;
        }
    }
}
