using Fusion;
using UnityEngine;

namespace TrustNoOne.Missions
{
    /// <summary>미션 오브젝트가 완료된 뒤 어떻게 되는가 (stage1 명세 S3).</summary>
    public enum CompletionPolicy
    {
        /// <summary>모두에게 잠긴다 (단체 미션용. 예: 발전기 「수리 완료」). 미션 초기화 알림이 오면 풀린다.</summary>
        Lock = 0,

        /// <summary>바로 원래 상태로 돌아간다 (개인 미션용). 같은 미니게임을 받은 다른 사람도 직접 해야 하고, 누가 끝냈는지 드러나지 않는다.</summary>
        ResetForNext = 1,
    }

    /// <summary>
    /// [역할] 씬의 모든 미션 오브젝트(미니게임)가 상속하는 공통 베이스. "규칙"만 책임지고 입력 방식은 모른다.
    ///  - 권한: 생존·행동 가능 / 범위 / 잠김 / 다른 사람이 사용 중 / 이 행동을 필요로 하는 내 미션 (전부 호스트 판정)
    ///  - 취소: 사용 중 매 틱 생존·범위 재검사 (기획서 공통 취소 조건)
    ///  - 완료: 완료 이벤트 발행 + 완료 후 동작(Lock / ResetForNext)
    ///  - 초기화: 제한 시간 실패(MissionManager.OnMissionReset)와 ResetForNext 가 같은 경로(ResetStation)를 쓴다
    ///
    /// [근거] stage1 명세 S2(조작은 그대로, 규칙만 공통), S3(개인용은 완료 후 초기화), S4(공통 베이스 + 하위 클래스),
    ///  S6(제한 시간 판정은 Core, 오브젝트는 알림에 반응만). 옛 MissionMiniGameBase 에서 미니게임마다 흩어져 있던
    ///  권한 검사(CanPlayerInteract 호출)를 이 한 곳으로 모았다.
    ///
    /// [하위 클래스 규칙] Spawned / FixedUpdateNetwork / Render 를 직접 override 하지 말고 훅(OnStationSpawned,
    ///  OnHostTick, OnStationRender …)을 쓴다. 그래야 권한·취소 검사를 건너뛸 수 없다.
    ///
    /// [완료·초기화 연출] 상태는 [Networked] 로 복제되므로 늦게 들어온 플레이어도 맞는 모습을 본다 (design-spec D12).
    ///  호스트는 변경 콜백 대신 직접 연출 훅을 부르고, 콜백에서는 호스트를 건너뛰어 두 번 불리지 않게 한다.
    /// </summary>
    public abstract class MissionStation : NetworkBehaviour, ITargetable
    {
        [Header("연결 (비워두면 씬에서 자동 검색)")]
        [SerializeField] private MissionManager manager;
        [SerializeField] private MonoBehaviour gateSource;

        [Header("완료 시 발행할 사실")]
        [Tooltip("이 오브젝트를 완료하면 MissionEvent 로 알릴 행동 종류. 예) GeneratorRepaired")]
        [SerializeField] private MissionEventType completionEvent = MissionEventType.None;

        [Tooltip("MissionEvent.TargetId 로 실린다 (어느 오브젝트였는지 구분이 필요할 때).")]
        [SerializeField] private int objectId;

        [Header("공통 규칙")]
        [Tooltip("호스트가 거리 검증에 쓰는 최대 거리. 플레이어 감지 거리(PlayerTargetDetector)보다 약간 크게 잡는다.")]
        [Min(0.1f)]
        [SerializeField] private float interactRange = 3f;

        [Tooltip("거리 기준점. 비우면 이 오브젝트의 Transform.")]
        [SerializeField] private Transform rangeCenter;

        [Tooltip("Lock: 완료되면 모두에게 잠김(단체) / ResetForNext: 완료되면 원래대로(개인)")]
        [SerializeField] private CompletionPolicy completionPolicy = CompletionPolicy.Lock;

        [Header("연출 (선택)")]
        [Tooltip("이 미션이 내 목표가 아니거나 잠겼으면 꺼지는 콜라이더들 (기획서: 해당 미션 없으면 표시하지 않음). 규칙이 아니라 연출이다.")]
        [SerializeField] private Collider[] interactionColliders;

        /// <summary>지금 이 오브젝트를 사용 중인 플레이어 (없으면 None). 한 번에 한 명 (design-spec D15).</summary>
        [Networked] public PlayerRef Operator { get; private set; }

        /// <summary>Lock 정책으로 잠긴 상태.</summary>
        [Networked, OnChangedRender(nameof(OnCompletedChanged))]
        public NetworkBool Completed { get; private set; }

        /// <summary>초기화할 때마다 1 씩 오른다. 값이 바뀌면 모든 피어가 초기화 연출을 한다.</summary>
        [Networked, OnChangedRender(nameof(OnResetVersionChanged))]
        private int ResetVersion { get; set; }

        private IMissionActorGate gate;
        private bool subscribedToReset;
        private bool? collidersEnabled;

        public NetworkObject TargetObject => Object;
        public MissionEventType CompletionEvent => completionEvent;
        public CompletionPolicy Policy => completionPolicy;

        /// <summary>내가 지금 이 오브젝트를 사용 중인가 (진행 표시를 나에게만 보여줄 때).</summary>
        public bool IsOperatedByLocalPlayer => Object != null && Object.IsValid && Operator == Runner.LocalPlayer;

        // ═════════════════════════════════════════════════════════════
        //  수명 주기
        // ═════════════════════════════════════════════════════════════

        public override void Spawned()
        {
            EnsureManager();

            if (HasStateAuthority)
            {
                gate = gateSource as IMissionActorGate;

                if (gate == null)
                    gate = FindFirstObjectByType<PlayerHealthActorGate>();

                if (gate == null)
                    Debug.LogError($"[MissionStation] {name}: IMissionActorGate 가 없습니다. 씬에 PlayerHealthActorGate 를 배치하세요.");
            }

            OnStationSpawned();

            // 늦게 들어와서 이미 잠긴 오브젝트를 만난 경우: 변경 콜백이 오지 않을 수 있으므로 직접 맞춘다.
            if (Completed)
                OnCompletedVisual();
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            if (subscribedToReset && manager != null)
                manager.OnMissionReset -= HandleMissionReset;

            subscribedToReset = false;

            if (hasState && !Operator.IsNone)
                gate?.SetBusy(Operator, false);
        }

        /// <summary>
        /// MissionManager 를 찾고, 호스트라면 미션 초기화 알림을 구독한다.
        /// 스폰 순서에 따라 Spawned 시점에 매니저가 아직 없을 수 있어서 매 틱 다시 시도한다 (찾은 뒤에는 비용 없음).
        /// </summary>
        private void EnsureManager()
        {
            if (manager == null)
                manager = FindFirstObjectByType<MissionManager>();

            if (manager == null || subscribedToReset || !HasStateAuthority)
                return;

            manager.OnMissionReset += HandleMissionReset;
            subscribedToReset = true;
        }

        // ═════════════════════════════════════════════════════════════
        //  부품 입력 (StationButton 등이 호출)
        // ═════════════════════════════════════════════════════════════

        /// <summary>부품이 눌렸다고 호스트에 알린다. 수행자는 RpcInfo.Source 로 정해진다 (사칭 방지).</summary>
        public void PressPart(int partIndex)
        {
            if (Object == null || !Object.IsValid)
                return;

            RPC_PressPart(partIndex);
        }

        /// <summary>SourceIsHostPlayer: 호스트 본인이 눌러도 Source 가 호스트 플레이어로 채워진다 (옛 ValveMission 에서 검증된 패턴).</summary>
        [Rpc(RpcSources.All, RpcTargets.StateAuthority, HostMode = RpcHostMode.SourceIsHostPlayer)]
        private void RPC_PressPart(int partIndex, RpcInfo info = default)
        {
            if (!info.Source.IsNone)
                OnPartPressed(info.Source, partIndex);
        }

        // ═════════════════════════════════════════════════════════════
        //  호스트: 사용 세션
        // ═════════════════════════════════════════════════════════════

        /// <summary>
        /// 사용 시작 검사 + 확정 (호스트 전용). 하나라도 실패하면 조용히 거부한다.
        /// 기획서 "입력 가능 조건": 생존 / 상호작용 가능 / 범위 내 / 다른 행동 중 아님 / 탈출 상태 아님.
        /// 같은 사람이 다시 요청하면 세션을 새로 시작한다 (OnOperationStarted 가 다시 불림).
        /// </summary>
        protected bool TryBeginOperation(PlayerRef actor)
        {
            EnsureManager();

            if (!HasStateAuthority || actor.IsNone || gate == null || manager == null)
                return false;

            if (Completed)
                return false;

            bool alreadyMine = Operator == actor;

            if (!Operator.IsNone && !alreadyMine)
                return false;

            // 새 사용자는 "시작 가능"(다른 미션 중이 아님 포함), 재요청은 "계속 가능" 기준으로 본다.
            // (재요청자는 이미 busy 로 표시돼 있어 CanStart 를 쓰면 자기 자신 때문에 거부된다)
            bool stateOk = alreadyMine ? gate.CanContinue(Runner, actor) : gate.CanStart(Runner, actor);

            if (!stateOk || !IsInRange(actor))
                return false;

            // 이 행동을 진행시킬 활성 목표가 없으면 거부 — 클라이언트 연출(콜라이더 끄기)과 별개로 호스트가 실제 규칙을 강제한다.
            if (!manager.HasActiveObjective(actor, completionEvent))
                return false;

            if (!alreadyMine)
                gate.SetBusy(actor, true);

            Operator = actor;
            OnOperationStarted(actor);
            return true;
        }

        /// <summary>사용을 취소한다 (범위 이탈·행동 불가·입력 취소). 하위 클래스가 진행 상태를 되돌릴 수 있게 훅을 부른다.</summary>
        protected void CancelOperation()
        {
            if (!HasStateAuthority || Operator.IsNone)
                return;

            PlayerRef actor = Operator;
            ReleaseOperator();
            OnOperationCanceled(actor);
        }

        /// <summary>
        /// 완료 확정 (호스트 전용): 사실을 알리고 완료 후 동작을 적용한다.
        /// Lock → 모두에게 잠금 / ResetForNext → 바로 원래대로 (개인 미션, 명세 S3)
        /// </summary>
        protected void CompleteBy(PlayerRef actor)
        {
            if (!HasStateAuthority || Completed || actor.IsNone)
                return;

            EnsureManager();
            manager?.Publish(new MissionEvent(completionEvent, actor, objectId));

            if (!Operator.IsNone)
                ReleaseOperator();

            if (completionPolicy == CompletionPolicy.Lock)
            {
                Completed = true;
                OnCompletedVisual();
            }
            else
            {
                ResetStation();
            }
        }

        /// <summary>원래 상태로 되돌린다 (호스트 전용). 사용 중이면 먼저 취소하고, 잠금을 풀고, 하위 클래스 상태를 되돌린다.</summary>
        protected void ResetStation()
        {
            if (!HasStateAuthority)
                return;

            if (!Operator.IsNone)
                CancelOperation();

            Completed = false;
            OnResetHost();
            ResetVersion++;
            OnResetVisual();
        }

        private void ReleaseOperator()
        {
            gate?.SetBusy(Operator, false);
            Operator = PlayerRef.None;
        }

        /// <summary>제한 시간 초과 알림. 내 완료 이벤트를 쓰는 미션이 초기화됐으면 나도 원래대로 (명세 3-3).</summary>
        private void HandleMissionReset(MissionEventType eventType)
        {
            if (eventType == completionEvent)
                ResetStation();
        }

        public override void FixedUpdateNetwork()
        {
            if (!HasStateAuthority)
                return;

            EnsureManager();

            if (Operator.IsNone)
                return;

            PlayerRef actor = Operator;

            // 취소 조건: 범위 이탈 / 사망 / 탈출 / 게임 종료 / 접속 종료
            if (gate == null || !gate.CanContinue(Runner, actor) || !IsInRange(actor))
            {
                CancelOperation();
                return;
            }

            OnHostTick(actor);
        }

        private bool IsInRange(PlayerRef actor)
        {
            if (!Runner.TryGetPlayerObject(actor, out NetworkObject playerObject) || playerObject == null)
                return false;

            Transform center = rangeCenter != null ? rangeCenter : transform;

            return (playerObject.transform.position - center.position).sqrMagnitude <= interactRange * interactRange;
        }

        // ═════════════════════════════════════════════════════════════
        //  클라이언트 연출
        // ═════════════════════════════════════════════════════════════

        public override void Render()
        {
            UpdateInteractionColliders();
            OnStationRender();
        }

        /// <summary>
        /// 내 미션이 아니거나 잠겼으면 콜라이더를 꺼서 감지 자체를 막는다 (기획서 UI 목업: 해당 미션 없으면 표시하지 않음).
        /// 연출일 뿐 규칙이 아니다 — 실제 허용 여부는 호스트의 HasActiveObjective 가 다시 검사한다. 상태가 바뀔 때만 만진다.
        /// </summary>
        private void UpdateInteractionColliders()
        {
            EnsureManager();

            if (interactionColliders == null || interactionColliders.Length == 0)
                return;

            if (manager == null || manager.Object == null || !manager.Object.IsValid || manager.Client == null || !manager.Initialized)
                return;

            bool shouldEnable = !Completed && manager.Client.IsEventRelevant(completionEvent);

            if (collidersEnabled == shouldEnable)
                return;

            collidersEnabled = shouldEnable;

            foreach (Collider interactionCollider in interactionColliders)
            {
                if (interactionCollider != null)
                    interactionCollider.enabled = shouldEnable;
            }
        }

        private void OnCompletedChanged()
        {
            if (!HasStateAuthority && Completed)
                OnCompletedVisual();
        }

        private void OnResetVersionChanged()
        {
            if (!HasStateAuthority)
                OnResetVisual();
        }

        // ═════════════════════════════════════════════════════════════
        //  하위 클래스 훅 (기본은 아무것도 하지 않음)
        // ═════════════════════════════════════════════════════════════

        /// <summary>모든 피어: 스폰 직후 (UI 초기 표시 등).</summary>
        protected virtual void OnStationSpawned() { }

        /// <summary>호스트: 사용이 시작됐을 때 (진행 상태 초기화).</summary>
        protected virtual void OnOperationStarted(PlayerRef actor) { }

        /// <summary>호스트: 사용 중 매 틱 (취소 검사를 통과한 뒤에만 불림).</summary>
        protected virtual void OnHostTick(PlayerRef actor) { }

        /// <summary>호스트: 사용이 취소됐을 때 (진행 상태 되돌리기).</summary>
        protected virtual void OnOperationCanceled(PlayerRef actor) { }

        /// <summary>호스트: 부품(버튼·레버)이 눌렸을 때. 권한 검사는 자동으로 하지 않는다 — 하위 클래스가 TryBeginOperation 또는 Operator == actor 로 판단.</summary>
        protected virtual void OnPartPressed(PlayerRef actor, int partIndex) { }

        /// <summary>호스트: 원래 상태로 되돌릴 때 자기 [Networked] 상태를 초기화한다.</summary>
        protected virtual void OnResetHost() { }

        /// <summary>모든 피어: 잠겼을 때 연출 (늦게 접속한 피어도 불림).</summary>
        protected virtual void OnCompletedVisual() { }

        /// <summary>모든 피어: 초기화됐을 때 연출.</summary>
        protected virtual void OnResetVisual() { }

        /// <summary>모든 피어: 매 프레임 표시 갱신 (게이지·회전 등).</summary>
        protected virtual void OnStationRender() { }
    }
}
