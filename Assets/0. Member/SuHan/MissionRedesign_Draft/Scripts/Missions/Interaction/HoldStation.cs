using Fusion;
using UnityEngine;

namespace TrustNoOne.Missions
{
    /// <summary>
    /// [역할] "F 를 holdDuration 초 동안 누르고 있으면 완료"되는 미션 오브젝트 (기존 MissionInteractable 의 HoldTimer 동작을 옮김).
    ///  - F 누름 → TryBeginOperation (권한 검사는 베이스) / F 뗌 → 본인일 때만 취소 → 진행도 0 (처음부터, 2a 명세 P1)
    ///  - 진행률은 호스트가 누적하고 동기화한다 → HoldProgress01 (홀드 게이지 HUD 가 읽음)
    ///
    /// [확장 지점 — 2a 명세 2-1] 밸브·안테나처럼 "누르는 동안 진행"하는 미니게임이 상속해서 바꾼다.
    ///  - HoldDuration     : 목표 유지 시간 (밸브·안테나는 무작위 목표 각도 / 회전 속도)
    ///  - OnHoldReached    : 목표 시간에 도달했을 때 할 일. 기본은 완료(CompleteBy). 안테나는 "준비 완료"로만 두고 고정 버튼을 기다린다.
    ///  - CancelOnRelease  : F 를 떼면 취소할지. 기본 true. 안테나는 준비 완료 뒤 false (떼고 고정 버튼으로 가야 하므로).
    ///  훅을 override 할 때는 base 를 호출할 것.
    ///
    /// [재요청 방어] MissionStation.TryBeginOperation 은 이미 사용 중인 본인이 다시 요청하면 OnOperationStarted 를 또 부른다.
    ///  그러면 준비 완료된 안테나에서 F 를 다시 누를 때 진행도가 0 이 된다. 그래서 사용 중인 본인의 F 누름은 무시한다.
    ///  손을 떼면 취소되는 보통 홀드 스테이션은 누를 때 항상 Operator 가 비어 있으므로 영향이 없다.
    ///
    /// [부품 번호 — 13a 명세 PL9, 공통 틀 변경] 장치 안의 여러 자리를 따로 누르고 있는 미니게임(배관 — 새는 곳 6자리)을 위해
    ///  "지금 누르고 있는 부품 번호"(HoldPart)를 둔다. 부품(StationHoldPart)이 BeginHoldPart(번호)로 요청하고, 하위 클래스는
    ///  CanBeginHoldPart 로 그 부품을 받을지 정한다. 장치 자신의 F(BeginHold)는 번호 −1 — 밸브 · 안테나는 지금과 똑같이 동작한다.
    ///  뗌은 지금 누르고 있는 부품의 것만 받는다 (다른 부품의 늦은 뗌 요청이 새 조작을 끊지 않게).
    /// </summary>
    public class HoldStation : MissionStation, IHoldInteractable
    {
        [Header("F 홀드")]
        [Tooltip("F 를 유지해야 하는 시간(초).")]
        [Min(0.05f)]
        [SerializeField] private float holdDuration = 3f;

        [Networked] private float HoldElapsed { get; set; }

        /// <summary>목표 유지 시간에 도달했는가. 도달 후에는 더 누적하지 않는다 (안테나 "준비 완료" 상태를 HUD·UI 가 읽음).</summary>
        [Networked] public NetworkBool HoldReached { get; private set; }

        // 누르고 있는 부품 번호 + 1 — [Networked] 정수의 처음 값 0 이 "장치 자신(−1)"이 되게 한 칸 밀어 둔다
        [Networked] private int HoldPartPlusOne { get; set; }

        /// <summary>지금 누르고 있는 부품 번호 (−1 = 장치 자신 또는 아무도 안 누름). 모든 PC 에서 읽는다 (배관 — 어느 자리에 테이프를 붙이는 중인지).</summary>
        public int HoldPart => HoldPartPlusOne - 1;

        /// <summary>완료까지 필요한 유지 시간. 하위 클래스가 랜덤 값 등으로 바꿀 수 있다.</summary>
        protected virtual float HoldDuration => holdDuration;

        /// <summary>F 를 떼면 사용을 취소할지 (기본: 취소 → 처음부터).</summary>
        protected virtual bool CancelOnRelease => true;

        /// <summary>진행 바용 0~1 값. 잠겼으면 1, 아무도 안 쓰면 0.</summary>
        public float HoldProgress01
        {
            get
            {
                if (Object == null || !Object.IsValid)
                    return 0f;

                if (Completed)
                    return 1f;

                if (Operator.IsNone)
                    return 0f;

                return Mathf.Clamp01(HoldElapsed / SafeDuration);
            }
        }

        private float SafeDuration => Mathf.Max(0.05f, HoldDuration);

        // PlayerInteraction 이 F 를 누를 때/뗄 때 호출 (IHoldInteractable). 클라이언트는 "요청"만 한다. 장치 자신 = 부품 번호 −1
        public void BeginHold() => BeginHoldPart(-1);

        public void EndHold() => EndHoldPart(-1);

        /// <summary>부품(StationHoldPart)이 F 누름을 번호와 함께 요청한다 (13a PL9). 잠겼으면 보내지 않는다.</summary>
        public void BeginHoldPart(int part)
        {
            if (Object == null || !Object.IsValid || Completed)
                return;

            RPC_BeginHold(part);
        }

        /// <summary>부품이 F 뗌을 번호와 함께 알린다.</summary>
        public void EndHoldPart(int part)
        {
            if (Object == null || !Object.IsValid)
                return;

            RPC_EndHold(part);
        }

        [Rpc(RpcSources.All, RpcTargets.StateAuthority, HostMode = RpcHostMode.SourceIsHostPlayer)]
        private void RPC_BeginHold(int part, RpcInfo info = default)
        {
            // 재요청 방어 (클래스 주석 참고)
            if (Operator == info.Source)
                return;

            // 하위 클래스가 그 부품을 받지 않으면 조용히 거부 (예: 배관 — 안 새는 자리 · 테이프 없음)
            if (!CanBeginHoldPart(info.Source, part))
                return;

            // 시작에 성공한 뒤 번호를 적는다 — 시작 훅(OnOperationStarted → ClearHold)이 번호를 먼저 지우기 때문
            if (TryBeginOperation(info.Source))
                HoldPartPlusOne = part + 1;
        }

        [Rpc(RpcSources.All, RpcTargets.StateAuthority, HostMode = RpcHostMode.SourceIsHostPlayer)]
        private void RPC_EndHold(int part, RpcInfo info = default)
        {
            // 남이 내 세션을 끊지 못하게, 현재 사용자 본인의 요청일 때만 받는다. 지금 누르는 부품의 뗌만 받는다 (PL9)
            if (Operator == info.Source && CancelOnRelease && part == HoldPart)
                CancelOperation();
        }

        /// <summary>
        /// 호스트: 이 부품의 F 누름을 받을지 (13a PL9). 기본은 모두 받음 — 장치 자신(−1)만 쓰는 밸브 · 안테나는 바꿀 필요가 없다.
        /// 공통 자격(이번 판 미션 · 행동 가능 · 범위)은 이 뒤의 TryBeginOperation 이 따로 검사한다.
        /// </summary>
        protected virtual bool CanBeginHoldPart(PlayerRef actor, int part) => true;

        /// <summary>목표 시간에 도달했을 때 (호스트). 기본은 완료. 하위 클래스가 "준비 완료"처럼 다른 동작으로 바꿀 수 있다.</summary>
        protected virtual void OnHoldReached(PlayerRef actor)
        {
            CompleteBy(actor);
        }

        protected override void OnOperationStarted(PlayerRef actor)
        {
            base.OnOperationStarted(actor);
            ClearHold();
        }

        protected override void OnHostTick(PlayerRef actor)
        {
            base.OnHostTick(actor);

            if (HoldReached)
                return;

            float duration = SafeDuration;
            HoldElapsed = Mathf.Min(HoldElapsed + Runner.DeltaTime, duration);

            if (HoldElapsed < duration)
                return;

            HoldReached = true;
            OnHoldReached(actor);
        }

        protected override void OnOperationCanceled(PlayerRef actor)
        {
            base.OnOperationCanceled(actor);
            ClearHold();
        }

        protected override void OnResetHost()
        {
            base.OnResetHost();
            ClearHold();
        }

        private void ClearHold()
        {
            HoldElapsed = 0f;
            HoldReached = false;
            HoldPartPlusOne = 0;
        }
    }
}
