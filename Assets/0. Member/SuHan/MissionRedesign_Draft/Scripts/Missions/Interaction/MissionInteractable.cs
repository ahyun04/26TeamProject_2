using Fusion;
using UnityEngine;

namespace TrustNoOne.Missions
{
    /// <summary>미션 오브젝트가 "언제 완료로 볼 것인가".</summary>
    public enum InteractionCompletionMode
    {
        /// <summary>F 를 holdDuration 초 동안 유지하면 완료 (별도 코드 불필요).</summary>
        HoldTimer = 0,

        /// <summary>하위 클래스가 자기 로직(밸브 회전, 전선 연결 …)으로 끝나는 시점에 CompleteInteraction 을 호출한다.</summary>
        Custom = 1,
    }

    /// <summary>
    /// [역할] 씬에 놓이는 모든 미션 오브젝트의 공통 상호작용 규격.
    ///        "누가·언제·어떤 조건에서 이 오브젝트를 쓸 수 있고, 끝나면 무슨 사실을 알리는가"를 한 곳에서 책임진다.
    ///
    /// [기획서 근거]
    ///  - 다이어그램 2/7: 접근 → [F] 표시 → F 입력 → 진행 바 → 상호작용 유지 → 완료 → 서버 판정 → 진행도 반영
    ///  - "모든 미션 오브젝트는 기본적으로 동일한 상호작용 구조를 사용한다"
    ///  - 입력 가능 조건 표 / 상호작용 취소 조건 표
    ///
    /// [플레이어 쪽 코드를 바꾸지 않는 이유]
    ///  기존 PlayerInteraction 은 F 홀드를 IHoldInteractable 로, 바라보는 대상을 ITargetable 로 이미 일반화해서 다룬다.
    ///  이 클래스가 두 인터페이스를 구현하면 플레이어 코드는 한 줄도 수정할 필요가 없다.
    ///
    /// [흐름]
    ///  클라이언트: BeginHold → RPC_RequestBegin  (수행자는 파라미터가 아니라 RpcInfo.Source — 사칭 방지)
    ///  호스트   : TryBeginSession 에서 검사 (상태/거리/소진/사용자/활성 목표) → Interactor 확정
    ///             FixedUpdateNetwork 에서 매 틱 CanContinue·거리 재검사 → 실패 시 취소, HoldTimer 면 진행률 증가 → 완료
    ///             완료 → sink.Publish(MissionEvent) → (singleUse 면) Consumed
    ///  클라이언트: EndHold → RPC_RequestCancel / Render 에서 진행 바·완료 연출
    ///
    /// [완료 연출을 RPC 가 아니라 [Networked] Consumed 로 하는 이유]
    ///  RPC 는 나중에 들어온 플레이어에게 재전송되지 않는다. 상태값은 복제되므로 늦게 온 사람도 이미 소진된 오브젝트를 정확히 본다.
    ///
    /// [한 번에 한 명] Interactor 가 하나다. 기획서에 동시 사용 규칙이 없어 기존 밸브와 같은 배타 방식으로 정했다.
    ///
    /// [하위 클래스용 API — 6종 미니게임 이식 시]
    ///  protected TryBeginSession / CompleteInteraction / CancelSession / OnHostTick / OnCompletedHost / OnConsumedVisual
    ///  Custom 모드에서 드래그 등 다른 입력이 필요하면 자기 RPC 안에서 TryBeginSession(info.Source) 을 호출하면 된다.
    /// </summary>
    public class MissionInteractable : NetworkBehaviour, ITargetable, IHoldInteractable
    {
        [Header("연결 (비워두면 씬에서 자동 검색)")]
        [SerializeField] private MissionManager manager;
        [SerializeField] private MonoBehaviour gateSource;

        [Header("완료 시 발행할 사실")]
        [Tooltip("이 오브젝트를 다 쓰면 MissionEvent 로 알릴 행동 종류. 예) GeneratorRepaired")]
        [SerializeField] private MissionEventType completionEvent = MissionEventType.None;

        [Tooltip("MissionEvent.TargetId 로 실린다 (어느 오브젝트였는지 구분이 필요할 때).")]
        [SerializeField] private int objectId;

        [Header("상호작용 규칙")]
        [SerializeField] private InteractionCompletionMode mode = InteractionCompletionMode.HoldTimer;

        [Tooltip("HoldTimer 모드에서 F 를 유지해야 하는 시간(초).")]
        [Min(0.05f)]
        [SerializeField] private float holdDuration = 3f;

        [Tooltip("호스트가 거리 검증에 쓰는 최대 거리. 플레이어 감지 거리(PlayerTargetDetector)보다 약간 크게 잡는다.")]
        [Min(0.1f)]
        [SerializeField] private float interactRange = 3f;

        [Tooltip("거리 기준점. 비우면 이 오브젝트의 Transform.")]
        [SerializeField] private Transform rangeCenter;

        [Tooltip("true 면 한 번 완료되면 소진되어 다시 쓸 수 없다 (발전기 1대 = 1회).")]
        [SerializeField] private bool singleUse = true;

        [Header("연출 (선택)")]
        [Tooltip("이 미션이 내 목표에 해당하지 않으면 꺼지는 콜라이더들 (기획서: 해당 미션 없으면 표시하지 않음).")]
        [SerializeField] private Collider[] interactionColliders;

        // ───────────── 동기화 상태 ─────────────

        /// <summary>지금 이 오브젝트를 사용 중인 플레이어 (없으면 None).</summary>
        [Networked] public PlayerRef Interactor { get; private set; }

        /// <summary>HoldTimer 모드에서 유지한 시간(초).</summary>
        [Networked] private float HoldElapsed { get; set; }

        /// <summary>소진 여부. 변경 시 클라이언트에서 완료 연출 훅이 호출된다.</summary>
        [Networked, OnChangedRender(nameof(OnConsumedChanged))]
        public NetworkBool Consumed { get; private set; }

        // ───────────── 로컬 참조 ─────────────

        private IMissionEventSink sink;
        private IMissionActorGate gate;
        private bool? collidersEnabled;

        public NetworkObject TargetObject => Object;

        /// <summary>진행 바용 0~1 값. 소진되었으면 1.</summary>
        public float HoldProgress01
        {
            get
            {
                if (Consumed)
                    return 1f;

                if (mode != InteractionCompletionMode.HoldTimer || Interactor.IsNone)
                    return 0f;

                return Mathf.Clamp01(HoldElapsed / holdDuration);
            }
        }

        /// <summary>내가 지금 이 오브젝트를 사용 중인가 (진행 바를 나에게만 보여줄 때).</summary>
        public bool IsUsedByLocalPlayer => Object != null && Object.IsValid && Interactor == Runner.LocalPlayer;

        // ═════════════════════════════════════════════════════════════
        //  수명 주기
        // ═════════════════════════════════════════════════════════════

        public override void Spawned()
        {
            if (manager == null)
                manager = FindFirstObjectByType<MissionManager>();

            sink = manager;

            if (HasStateAuthority)
            {
                gate = gateSource as IMissionActorGate;

                if (gate == null)
                    gate = FindFirstObjectByType<PlayerHealthActorGate>();

                if (gate == null)
                    Debug.LogError($"[MissionInteractable] {name}: IMissionActorGate 가 없습니다. 씬에 PlayerHealthActorGate 를 배치하세요.");

                if (sink == null)
                    Debug.LogError($"[MissionInteractable] {name}: MissionManager 를 찾을 수 없습니다.");
            }

            // 늦게 들어와서 이미 소진된 오브젝트를 만난 경우: 변경 콜백이 오지 않을 수 있으므로 직접 연출을 맞춘다.
            if (Consumed)
                OnConsumedVisual();
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            if (hasState && !Interactor.IsNone)
                gate?.SetBusy(Interactor, false);
        }

        // ═════════════════════════════════════════════════════════════
        //  플레이어 입력 진입점 (PlayerInteraction 이 호출 — IHoldInteractable)
        // ═════════════════════════════════════════════════════════════

        public void BeginHold()
        {
            // 클라이언트는 "요청"만 한다. 실제 허용 여부는 호스트가 판단한다.
            if (Consumed)
                return;

            RPC_RequestBegin();
        }

        public void EndHold()
        {
            RPC_RequestCancel();
        }

        /// <summary>
        /// HostMode = SourceIsHostPlayer: 호스트 플레이어 본인이 호출해도 Source 가 호스트 플레이어로 채워진다
        /// (기존 ValveMission 에서 검증된 패턴). 이게 없으면 호스트가 누른 F 가 Source=None 으로 들어온다.
        /// </summary>
        [Rpc(RpcSources.All, RpcTargets.StateAuthority, HostMode = RpcHostMode.SourceIsHostPlayer)]
        private void RPC_RequestBegin(RpcInfo info = default)
        {
            TryBeginSession(info.Source);
        }

        [Rpc(RpcSources.All, RpcTargets.StateAuthority, HostMode = RpcHostMode.SourceIsHostPlayer)]
        private void RPC_RequestCancel(RpcInfo info = default)
        {
            // 남이 내 세션을 끊지 못하게, 현재 사용자 본인의 요청일 때만 받는다.
            if (Interactor == info.Source)
                CancelSession();
        }

        // ═════════════════════════════════════════════════════════════
        //  호스트: 세션 관리
        // ═════════════════════════════════════════════════════════════

        /// <summary>
        /// 상호작용 시작 검사 + 확정 (호스트 전용). 하나라도 실패하면 조용히 거부한다.
        /// 기획서 "입력 가능 조건": 생존 / 상호작용 가능 / 범위 내 / 다른 행동 중 아님 / 탈출 상태 아님.
        /// </summary>
        protected bool TryBeginSession(PlayerRef actor)
        {
            if (!HasStateAuthority || actor.IsNone || gate == null || sink == null)
                return false;

            if (Consumed)
                return false;

            // 이미 다른 사람이 쓰는 중이면 거부. 같은 사람의 재요청은 허용(세션 재시작).
            bool alreadyMine = Interactor == actor;

            if (!Interactor.IsNone && !alreadyMine)
                return false;

            // 새 사용자는 "시작 가능"(다른 미션 중이 아님 포함), 재요청은 "계속 가능" 기준으로 본다.
            // (재요청자는 이미 busy 로 표시돼 있어 CanStart 를 쓰면 자기 자신 때문에 거부된다)
            bool stateOk = alreadyMine ? gate.CanContinue(Runner, actor) : gate.CanStart(Runner, actor);

            if (!stateOk)
                return false;

            if (!IsInRange(actor))
                return false;

            // 이 행동을 진행시킬 활성 목표가 없으면 거부 — 클라이언트 표시(연출)와 별개로 호스트가 실제 규칙을 강제한다.
            if (!sink.HasActiveObjective(actor, completionEvent))
                return false;

            if (!alreadyMine)
                gate.SetBusy(actor, true);

            Interactor = actor;
            HoldElapsed = 0f;
            return true;
        }

        /// <summary>세션을 끝낸다 (취소/완료 공통). 사용자와 진행률을 초기화하고 "사용 중" 표시를 푼다.</summary>
        protected void CancelSession()
        {
            if (!HasStateAuthority || Interactor.IsNone)
                return;

            gate?.SetBusy(Interactor, false);

            Interactor = PlayerRef.None;
            HoldElapsed = 0f;
        }

        /// <summary>
        /// 상호작용 완료 확정 (호스트 전용): 사실을 알리고, singleUse 면 소진 처리한다.
        /// Custom 모드 하위 클래스는 자기 로직이 끝났을 때 이걸 호출한다.
        /// </summary>
        protected void CompleteInteraction(PlayerRef actor)
        {
            if (!HasStateAuthority || Consumed || actor.IsNone)
                return;

            sink?.Publish(new MissionEvent(completionEvent, actor, objectId));

            CancelSession();

            if (singleUse)
            {
                Consumed = true;
                OnConsumedVisual();   // 호스트는 변경 콜백을 받지 않으므로 직접 호출
            }

            OnCompletedHost(actor);
        }

        public override void FixedUpdateNetwork()
        {
            if (!HasStateAuthority || Interactor.IsNone)
                return;

            PlayerRef actor = Interactor;

            // 취소 조건: 범위 이탈 / 피격 / 사망 / 탈출 / 게임 종료 / 접속 종료
            if (gate == null || !gate.CanContinue(Runner, actor) || !IsInRange(actor))
            {
                CancelSession();
                return;
            }

            if (mode == InteractionCompletionMode.HoldTimer)
            {
                HoldElapsed += Runner.DeltaTime;

                if (HoldElapsed >= holdDuration)
                {
                    CompleteInteraction(actor);
                    return;
                }
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

        /// <summary>
        /// 이 미션이 내 목표에 해당하지 않으면 콜라이더를 꺼서 감지 자체를 막는다 (기획서 UI 목업: 해당 미션 없으면 표시하지 않음).
        /// 이건 연출일 뿐 규칙이 아니다 — 실제 허용 여부는 호스트의 HasActiveObjective 가 다시 검사한다.
        /// 상태가 바뀔 때만 콜라이더를 만진다.
        /// </summary>
        public override void Render()
        {
            if (manager == null || manager.Client == null || interactionColliders == null || interactionColliders.Length == 0)
                return;

            if (!manager.Initialized)
                return;

            bool shouldEnable = !Consumed && manager.Client.IsEventRelevant(completionEvent);

            if (collidersEnabled == shouldEnable)
                return;

            collidersEnabled = shouldEnable;

            foreach (Collider interactionCollider in interactionColliders)
            {
                if (interactionCollider != null)
                    interactionCollider.enabled = shouldEnable;
            }
        }

        private void OnConsumedChanged()
        {
            if (Consumed && !HasStateAuthority)
                OnConsumedVisual();
        }

        // ═════════════════════════════════════════════════════════════
        //  하위 클래스 훅 (기본은 아무것도 하지 않음)
        // ═════════════════════════════════════════════════════════════

        /// <summary>세션 진행 중 매 틱 호스트에서 호출 (Custom 모드의 진행 로직용).</summary>
        protected virtual void OnHostTick(PlayerRef actor) { }

        /// <summary>완료가 확정된 직후 호스트에서 1회 호출.</summary>
        protected virtual void OnCompletedHost(PlayerRef actor) { }

        /// <summary>소진되었을 때 모든 피어에서 1회 호출 (문 열림/불 켜짐 같은 연출). 늦게 접속한 피어도 호출된다.</summary>
        protected virtual void OnConsumedVisual() { }
    }
}
