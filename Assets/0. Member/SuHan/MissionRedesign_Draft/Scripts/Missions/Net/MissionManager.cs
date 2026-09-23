using System;
using System.Collections.Generic;
using Fusion;
using UnityEngine;

namespace TrustNoOne.Missions
{
    /// <summary>
    /// [역할] 미션 시스템의 "네트워크 진입점". 판정 로직(Core)을 조립하고, 동기화·RPC·완료 이벤트를 담당한다.
    ///        씬에 NetworkObject 와 함께 1개 배치한다. (기존 MissionSystem 의 후속)
    ///
    /// [기획서 근거] 다이어그램 1/6: 역할 배정 → MissionManager 초기화 → 미션 생성 → 정보 전달 → 클라이언트 UI 표시
    ///  → 진행도 판정 → 동기화 → 완료 판정 → UI 갱신 → 게임 종료 → 전체 공개.
    ///  서버/클라이언트 역할 분리표(Pool 관리·생성·충돌 검사·판정은 서버, 표시는 클라이언트).
    ///
    /// [책임의 경계]
    ///  - 이 클래스는 "판정하지 않는다". 판정은 Core(MissionEventRouter / IMissionObjective / MissionOutcome)가 한다.
    ///  - 이 클래스는 "승패를 결정하지 않는다". 사실(단체 완료? 내 개인 미션 완료? 행동 목표 성공?)만 알려주고,
    ///    승패는 GameEndSystem 이 결정한다.
    ///
    /// [데이터 공개 범위 — 핵심 설계 결정]
    ///  Fusion 의 [Networked] 는 필드 단위 가시성이 없어 모든 클라이언트에 복제된다.
    ///  그래서 데이터를 둘로 나눈다:
    ///   ① 공개(전원에게 복제): 단체 미션 진행도, 시민 전체 합산 진행도(숫자 2개), 단체 완료, 탈출 해금
    ///   ② 비공개(소유자에게만 RPC): 개인 미션, 개인 행동 목표, 살인마 미션
    ///  기존 MissionSystem.Missions 는 개인 미션까지 [Networked] 배열에 넣어서 이 요구를 지키지 못했다.
    ///
    /// [이식 호환] InitializeMissions(citizens, killers) 와 IsReady 를 기존 MissionSystem 과 같은 이름·시그니처로 제공한다.
    ///  → RoleAssignment 는 타입 이름만 MissionManager 로 바꾸면 된다.
    ///
    /// [한계 — Host 모드] 호스트 프로세스는 모든 목표 데이터를 메모리에 가지고 있다. 호스트 본인이 클라이언트를 변조하면
    ///  볼 수 있다. Host 모드의 구조적 한계이며 이번 범위에서 해결하지 않는다.
    /// </summary>
    public class MissionManager : NetworkBehaviour, IMissionEventSink
    {
        /// <summary>
        /// 공개 배열 용량. 기획서 예시(Pool 7개 중 4개)보다 여유를 둔 값.
        /// 초과하는 단체 미션은 호스트 판정은 되지만 클라이언트에 표시되지 않으므로 오류 로그를 남긴다.
        /// </summary>
        private const int MaxTeamMissions = 8;

        [Header("데이터")]
        [SerializeField] private MissionPool pool;

        [Header("탈출 해금 규칙")]
        [Tooltip("이 ID의 단체 미션이 완료되면 탈출 해금. 비우거나 이번 판에 뽑히지 않았으면 '단체 미션 전부 완료'로 대체.")]
        [SerializeField] private string escapeMissionId = "TM005";

        // ───────────── 공개 상태 ([Networked], 전원에게 복제) ─────────────

        [Networked, Capacity(MaxTeamMissions)]
        public NetworkArray<TeamMissionState> TeamMissions => default;

        [Networked] public int TeamMissionCount { get; private set; }

        /// <summary>시민 전체 합산 진행도(HUD 진행바). 개인별 값이 아니라 합계 두 숫자만 공개한다.</summary>
        [Networked] public int CitizenProgressCurrent { get; private set; }
        [Networked] public int CitizenProgressRequired { get; private set; }

        /// <summary>중복 초기화 방지.</summary>
        [Networked] public NetworkBool Initialized { get; private set; }

        /// <summary>단체 미션 전부 완료 여부. 늦게 들어온 클라이언트도 상태를 그대로 받는다.</summary>
        [Networked] public NetworkBool TeamCompleted { get; private set; }

        /// <summary>탈출 조건 해금 여부 (탈출구 오브젝트가 읽는다).</summary>
        [Networked] public NetworkBool EscapeUnlocked { get; private set; }

        // ───────────── 이벤트 (호스트에서 발생, GameEndSystem 등이 구독) ─────────────

        /// <summary>단체 미션이 전부 완료된 순간 1회.</summary>
        public event Action OnTeamMissionsCompleted;

        /// <summary>어떤 시민의 개인 미션이 전부 완료된 순간 1회 (시민 개인 승리 판정의 재료).</summary>
        public event Action<PlayerRef> OnPersonalMissionsCompleted;

        /// <summary>탈출 조건이 해금된 순간 1회.</summary>
        public event Action OnEscapeUnlocked;

        /// <summary>
        /// 제한 시간 초과로 어떤 미션이 처음부터 다시 시작될 때 (호스트). 인자 = 그 미션의 Trigger 행동.
        /// 같은 행동을 완료 이벤트로 쓰는 미션 오브젝트(MissionStation)가 구독해서 원래 상태로 돌아간다.
        /// (예: 발전기 2분 체인 실패 → GeneratorRepaired → 발전기 3대 「수리 완료」 해제)
        /// </summary>
        public event Action<MissionEventType> OnMissionReset;

        // ───────────── 클라이언트 캐시 (모든 피어) ─────────────

        /// <summary>UI 가 읽는 로컬 캐시. 이 PC 가 화면에 그려도 되는 정보만 들어 있다.</summary>
        public MissionClientState Client { get; private set; }

        // ───────────── 호스트 전용 상태 ─────────────

        private MissionEventRouter router;
        private readonly List<IMissionObjective> teamObjectives = new List<IMissionObjective>();
        private readonly Dictionary<IMissionObjective, int> slotByObjective = new Dictionary<IMissionObjective, int>();
        private readonly HashSet<PlayerRef> teamParticipants = new HashSet<PlayerRef>();
        private readonly HashSet<PlayerRef> personalDone = new HashSet<PlayerRef>();
        private bool revealSent;

        // Render 에서 매 프레임 재사용 (GC 방지)
        private readonly TeamMissionState[] publicBuffer = new TeamMissionState[MaxTeamMissions];

        /// <summary>
        /// Custom 목표를 등록하는 곳. InitializeMissions 이전에 등록해야 한다. (호스트에서만 non-null)
        /// 예) missionManager.Factory.Register("WalkTogether", (def, owner, participants) => new WalkTogetherObjective(...));
        /// </summary>
        public ObjectiveFactory Factory { get; private set; }

        /// <summary>RoleAssignment 가 "미션 시스템이 준비됐나" 확인하는 값 (기존 MissionSystem.IsReady 와 동일 의미).</summary>
        public bool IsReady => router != null;

        // ═════════════════════════════════════════════════════════════
        //  수명 주기
        // ═════════════════════════════════════════════════════════════

        public override void Spawned()
        {
            if (pool == null)
            {
                Debug.LogError("[MissionManager] MissionPool 이 연결되지 않았습니다.");
                return;
            }

            Client = new MissionClientState(pool, Runner.LocalPlayer);

            if (HasStateAuthority)
            {
                router = new MissionEventRouter();
                router.ObjectiveChanged += OnObjectiveChanged;
                router.ObjectiveReset += OnObjectiveReset;
                Factory = new ObjectiveFactory();
                return;
            }

            // RPC 는 나중에 들어온 플레이어에게 재전송되지 않는다.
            // 그래서 늦게 들어온 클라이언트가 직접 "내 몫을 다시 보내달라"고 요청한다.
            RPC_RequestMySnapshot();
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            if (!hasState || router == null)
                return;

            router.ObjectiveChanged -= OnObjectiveChanged;
            router.ObjectiveReset -= OnObjectiveReset;
        }

        /// <summary>
        /// 공개 상태([Networked])를 로컬 캐시로 옮긴다. 값이 같으면 캐시가 아무것도 하지 않으므로 매 프레임 호출해도 안전하다.
        /// ChangeDetector 대신 단순 비교를 택한 이유: 배열이 최대 8칸이라 비용이 미미하고, 검증되지 않은 API 의존을 늘리지 않기 위해서.
        /// </summary>
        /// <summary>호스트: 제한 시간 판정을 위해 매 틱 현재 시뮬레이션 시각을 Core 에 넘긴다.</summary>
        public override void FixedUpdateNetwork()
        {
            if (CanQuery)
                router.Tick(Runner.SimulationTime);
        }

        public override void Render()
        {
            if (Client == null || !Initialized)
                return;

            int count = Mathf.Min(TeamMissionCount, MaxTeamMissions);

            for (int i = 0; i < count; i++)
                publicBuffer[i] = TeamMissions[i];

            Client.ApplyPublic(
                publicBuffer, count, CitizenProgressCurrent, CitizenProgressRequired, TeamCompleted, EscapeUnlocked);
        }

        // ═════════════════════════════════════════════════════════════
        //  초기화 / 초기화 해제 (호스트)
        // ═════════════════════════════════════════════════════════════

        /// <summary>
        /// 역할 배정이 끝난 뒤 호스트가 한 번 호출한다. (기존 MissionSystem.InitializeMissions 와 같은 시그니처)
        /// 생성 → 라우터 등록 → 공개/개인 동기화 순서로 진행한다.
        /// </summary>
        public void InitializeMissions(IReadOnlyList<PlayerRef> citizens, IReadOnlyList<PlayerRef> killers)
        {
            if (!HasStateAuthority || Initialized || router == null)
                return;

            MissionGenerator generator = new MissionGenerator(pool, Factory, new System.Random());
            MissionPlan plan = generator.Generate(citizens, killers);

            foreach (string warning in plan.Warnings)
                Debug.LogWarning($"[MissionManager] {warning}");

            router.Reset(plan.Objectives);

            // 슬롯 = plan.Objectives 의 인덱스. 개인 데이터 동기화의 키로 쓴다.
            slotByObjective.Clear();
            teamObjectives.Clear();
            teamParticipants.Clear();
            personalDone.Clear();
            revealSent = false;

            foreach (PlayerRef citizen in citizens)
                teamParticipants.Add(citizen);

            for (int slot = 0; slot < plan.Objectives.Count; slot++)
            {
                IMissionObjective objective = plan.Objectives[slot];
                slotByObjective[objective] = slot;

                if (objective.Definition.Category == MissionCategory.Team)
                    teamObjectives.Add(objective);
            }

            if (teamObjectives.Count > MaxTeamMissions)
            {
                Debug.LogError(
                    $"[MissionManager] 단체 미션 {teamObjectives.Count}개가 공개 배열 용량({MaxTeamMissions})을 넘어 " +
                    "일부가 클라이언트에 표시되지 않습니다. MaxTeamMissions 를 늘리거나 Pool 의 단체 미션 수를 줄이세요.");
            }

            // 공개 상태
            TeamMissionCount = Mathf.Min(teamObjectives.Count, MaxTeamMissions);

            for (int i = 0; i < TeamMissionCount; i++)
                TeamMissions.Set(i, ToTeamState(teamObjectives[i]));

            // 비공개 상태: 참여 여부와 개인 목표를 소유자에게만 전달
            foreach (PlayerRef citizen in citizens)
                SendParticipation(citizen, true);

            foreach (PlayerRef killer in killers)
                SendParticipation(killer, false);

            foreach (IMissionObjective objective in plan.Objectives)
            {
                if (!objective.Owner.IsNone)
                    SendPrivate(objective);
            }

            RecomputeAggregates(fireEvents: false);

            Initialized = true;

            Debug.Log($"[MissionManager] 미션 초기화 완료: 목표 {plan.Objectives.Count}개 (단체 {teamObjectives.Count})");
        }

        /// <summary>새 판을 시작하기 위해 모든 상태를 비운다 (호스트).</summary>
        public void ResetMissions()
        {
            if (!HasStateAuthority || router == null)
                return;

            router.Reset(Array.Empty<IMissionObjective>());
            slotByObjective.Clear();
            teamObjectives.Clear();
            teamParticipants.Clear();
            personalDone.Clear();
            revealSent = false;

            for (int i = 0; i < MaxTeamMissions; i++)
                TeamMissions.Set(i, default);

            TeamMissionCount = 0;
            CitizenProgressCurrent = 0;
            CitizenProgressRequired = 0;
            TeamCompleted = false;
            EscapeUnlocked = false;
            Initialized = false;

            Client?.ApplyClear();
            RPC_ClearClients();
        }

        // ═════════════════════════════════════════════════════════════
        //  이벤트 입력 (IMissionEventSink)
        // ═════════════════════════════════════════════════════════════

        /// <summary>미션 오브젝트/플레이어 스크립트가 "이런 행동이 일어났다"를 알린다 (호스트 전용).</summary>
        public void Publish(in MissionEvent e)
        {
            if (!HasStateAuthority || router == null || !Initialized)
                return;

            router.Publish(e, Runner.SimulationTime);
        }

        public bool HasActiveObjective(PlayerRef actor, MissionEventType eventType)
        {
            return HasStateAuthority && router != null && Initialized && router.HasActiveObjective(actor, eventType);
        }

        // ═════════════════════════════════════════════════════════════
        //  GameEndSystem 이 묻는 "사실" (호스트 전용) — 승패 결정은 하지 않는다
        // ═════════════════════════════════════════════════════════════

        /// <summary>단체 미션이 전부 완료되었는가 (네트워크로 복제되므로 클라이언트에서도 읽을 수 있다).</summary>
        public bool IsTeamMissionsCompleted => TeamCompleted;

        /// <summary>그 시민의 개인 미션이 전부 완료되었는가 (호스트 전용 — 개인 정보라 클라이언트는 알 수 없다).</summary>
        public bool IsPersonalMissionsCompleted(PlayerRef player)
        {
            return CanQuery && MissionOutcome.IsPersonalCompleted(router.Objectives, player);
        }

        /// <summary>
        /// 그 플레이어의 행동 목표가 성공했는가 (호스트 전용).
        /// 종료 판정형 목표는 FinalizePlayer 이후에 확정된다.
        /// </summary>
        public bool IsActionGoalSuccess(PlayerRef player)
        {
            return CanQuery && MissionOutcome.IsActionGoalSuccess(router.Objectives, player);
        }

        /// <summary>
        /// 그 플레이어의 행동이 끝났음을 알린다 (탈출/사망/시간 종료 시점에 GameEndSystem 이 호출).
        /// "한 번도 달리지 않기" 같은 종료 판정형 행동 목표가 여기서 확정된다. 여러 번 호출해도 안전하다.
        /// </summary>
        public void FinalizePlayer(PlayerRef player)
        {
            if (CanQuery)
                router.FinalizePlayer(player);
        }

        /// <summary>목표를 가진 모든 플레이어를 확정한다 (시간 종료 등 게임 전체가 끝날 때).</summary>
        public void FinalizeAllPlayers()
        {
            if (CanQuery)
                router.FinalizeAll();
        }

        /// <summary>
        /// 게임 종료 시 전체 미션 공개. (기획서: 게임 종료 시 전체 미션 결과 표시)
        /// 먼저 모든 플레이어를 확정해서 화면에 나오는 결과가 최종값이 되게 한 뒤, 플레이어 몫을 하나씩 RPC 로 방송한다.
        /// 한 번에 모아 보내지 않는 이유: RPC 는 크기 제한이 있어 모두 담으면 초과할 수 있다.
        /// 단체 미션은 이미 공개 상태이므로 여기서 보내지 않는다.
        /// 한 번만 실행된다.
        /// </summary>
        public void RevealAllMissions()
        {
            if (!CanQuery || revealSent)
                return;

            revealSent = true;
            router.FinalizeAll();

            foreach (IMissionObjective objective in router.Objectives)
            {
                if (objective.Owner.IsNone)
                    continue;

                int slot = slotByObjective[objective];
                int defIndex = pool.IndexOf(objective.Definition);

                Client.ApplyReveal(objective.Owner, slot, defIndex, objective.Progress, objective.Required, objective.Status);

                RPC_Reveal(
                    objective.Owner.RawEncoded, slot, defIndex, objective.Progress, objective.Required, (int)objective.Status);
            }
        }

        private bool CanQuery => HasStateAuthority && router != null && Initialized;

        // ═════════════════════════════════════════════════════════════
        //  변경 통지 → 동기화 (호스트)
        // ═════════════════════════════════════════════════════════════

        /// <summary>라우터가 "이 목표의 상태가 바뀌었다"고 알렸을 때: 알맞은 범위로 동기화하고 집계를 갱신한다.</summary>
        /// <summary>
        /// 라우터가 "제한 시간 초과로 이 목표가 0 으로 돌아갔다"고 알렸을 때.
        /// 상태 동기화는 직전의 OnObjectiveChanged 가 이미 했으므로, 여기서는 미션 오브젝트들에게 초기화만 알린다.
        /// </summary>
        private void OnObjectiveReset(IMissionObjective objective)
        {
            Debug.Log($"[MissionManager] 제한 시간 초과 → 처음부터 다시: {objective.Definition.Id} {objective.Definition.DisplayName}");
            OnMissionReset?.Invoke(objective.Definition.Trigger);
        }

        private void OnObjectiveChanged(IMissionObjective objective)
        {
            if (objective.Definition.Category == MissionCategory.Team)
            {
                int index = teamObjectives.IndexOf(objective);

                if (index >= 0 && index < MaxTeamMissions)
                    TeamMissions.Set(index, ToTeamState(objective));
            }
            else if (!objective.Owner.IsNone)
            {
                SendPrivate(objective);
            }

            RecomputeAggregates(fireEvents: true);
        }

        /// <summary>
        /// 집계(합산 진행도, 단체 완료, 탈출 해금, 개인 미션 완료)를 다시 계산한다.
        /// [Networked] 값을 먼저 모두 쓰고 나서 이벤트를 발생시킨다 → 구독자가 최신 상태를 읽는다.
        /// 각 이벤트는 "처음 참이 되는 순간" 한 번만 발생한다.
        /// </summary>
        private void RecomputeAggregates(bool fireEvents)
        {
            IReadOnlyList<IMissionObjective> objectives = router.Objectives;

            MissionOutcome.GetCitizenProgress(objectives, out int current, out int required);
            CitizenProgressCurrent = current;
            CitizenProgressRequired = required;

            bool teamCompleted = MissionOutcome.IsTeamCompleted(objectives);
            bool escapeUnlocked = MissionOutcome.IsEscapeUnlocked(objectives, escapeMissionId);

            bool teamJustCompleted = teamCompleted && !TeamCompleted;
            bool escapeJustUnlocked = escapeUnlocked && !EscapeUnlocked;

            TeamCompleted = teamCompleted;
            EscapeUnlocked = escapeUnlocked;

            List<PlayerRef> newlyCompletedPersonal = null;

            foreach (IMissionObjective objective in objectives)
            {
                if (objective.Definition.Category != MissionCategory.Personal || objective.Owner.IsNone)
                    continue;

                PlayerRef owner = objective.Owner;

                if (personalDone.Contains(owner) || !MissionOutcome.IsPersonalCompleted(objectives, owner))
                    continue;

                personalDone.Add(owner);
                (newlyCompletedPersonal ??= new List<PlayerRef>()).Add(owner);
            }

            if (!fireEvents)
                return;

            if (newlyCompletedPersonal != null)
            {
                foreach (PlayerRef owner in newlyCompletedPersonal)
                    OnPersonalMissionsCompleted?.Invoke(owner);
            }

            if (teamJustCompleted)
            {
                Debug.Log("[MissionManager] 단체 미션 전체 완료");
                OnTeamMissionsCompleted?.Invoke();
            }

            if (escapeJustUnlocked)
            {
                Debug.Log("[MissionManager] 탈출 조건 해금");
                OnEscapeUnlocked?.Invoke();
            }
        }

        private TeamMissionState ToTeamState(IMissionObjective objective)
        {
            return new TeamMissionState
            {
                DefIndex = pool.IndexOf(objective.Definition),
                Progress = objective.Progress,
                Required = objective.Required,
                Status = objective.Status,
                Deadline = objective.Deadline < 0d ? 0f : (float)objective.Deadline,
            };
        }

        // ═════════════════════════════════════════════════════════════
        //  비공개 전달 (호스트 → 소유자 한 명)
        // ═════════════════════════════════════════════════════════════

        private void SendPrivate(IMissionObjective objective)
        {
            int slot = slotByObjective[objective];
            int defIndex = pool.IndexOf(objective.Definition);
            PlayerRef target = objective.Owner;

            // 호스트 본인이 대상이면 RPC 를 거치지 않고 로컬 캐시에 직접 넣는다.
            // ([RpcTarget] 이 호스트 자기 자신에게 전달되는지가 환경에 따라 불확실해서, 항상 맞는 경로를 택했다)
            if (target == Runner.LocalPlayer)
                Client.ApplyPrivate(slot, defIndex, objective.Progress, objective.Required, objective.Status);
            else
                RPC_SyncPrivate(target, slot, defIndex, objective.Progress, objective.Required, (int)objective.Status);
        }

        private void SendParticipation(PlayerRef target, bool isTeamParticipant)
        {
            if (target == Runner.LocalPlayer)
                Client.ApplyParticipation(isTeamParticipant);
            else
                RPC_SyncParticipation(target, isTeamParticipant);
        }

        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        private void RPC_SyncPrivate(
            [RpcTarget] PlayerRef target, int slot, int defIndex, int progress, int required, int status)
        {
            Client?.ApplyPrivate(slot, defIndex, progress, required, (ObjectiveStatus)status);
        }

        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        private void RPC_SyncParticipation([RpcTarget] PlayerRef target, bool isTeamParticipant)
        {
            Client?.ApplyParticipation(isTeamParticipant);
        }

        /// <summary>
        /// 늦게 들어온(또는 놓친) 클라이언트의 재요청. 요청자는 파라미터가 아니라 RpcInfo.Source 로 확인한다
        /// → 남의 몫을 요청해서 훔쳐볼 수 없다. 호스트가 아직 초기화 전이면 무시하고, 초기화 시점에 어차피 전송된다.
        /// </summary>
        [Rpc(RpcSources.All, RpcTargets.StateAuthority, HostMode = RpcHostMode.SourceIsHostPlayer)]
        private void RPC_RequestMySnapshot(RpcInfo info = default)
        {
            if (!CanQuery)
                return;

            PlayerRef requester = info.Source;

            SendParticipation(requester, teamParticipants.Contains(requester));

            foreach (IMissionObjective objective in router.Objectives)
            {
                if (objective.Owner == requester)
                    SendPrivate(objective);
            }
        }

        /// <summary>게임 종료 후 전체 공개. 호스트는 로컬 캐시에 직접 넣었으므로 대상은 호스트 외 전원(Proxies).</summary>
        [Rpc(RpcSources.StateAuthority, RpcTargets.Proxies)]
        private void RPC_Reveal(int ownerEncoded, int slot, int defIndex, int progress, int required, int status)
        {
            Client?.ApplyReveal(
                PlayerRef.FromEncoded(ownerEncoded), slot, defIndex, progress, required, (ObjectiveStatus)status);
        }

        [Rpc(RpcSources.StateAuthority, RpcTargets.Proxies)]
        private void RPC_ClearClients()
        {
            Client?.ApplyClear();
        }
    }
}
