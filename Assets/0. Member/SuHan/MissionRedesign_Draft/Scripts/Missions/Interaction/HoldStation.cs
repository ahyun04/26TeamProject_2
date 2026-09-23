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

        // PlayerInteraction 이 F 를 누를 때/뗄 때 호출 (IHoldInteractable). 클라이언트는 "요청"만 한다.
        public void BeginHold()
        {
            if (Object == null || !Object.IsValid || Completed)
                return;

            RPC_BeginHold();
        }

        public void EndHold()
        {
            if (Object == null || !Object.IsValid)
                return;

            RPC_EndHold();
        }

        [Rpc(RpcSources.All, RpcTargets.StateAuthority, HostMode = RpcHostMode.SourceIsHostPlayer)]
        private void RPC_BeginHold(RpcInfo info = default)
        {
            // 재요청 방어 (클래스 주석 참고)
            if (Operator == info.Source)
                return;

            TryBeginOperation(info.Source);
        }

        [Rpc(RpcSources.All, RpcTargets.StateAuthority, HostMode = RpcHostMode.SourceIsHostPlayer)]
        private void RPC_EndHold(RpcInfo info = default)
        {
            // 남이 내 세션을 끊지 못하게, 현재 사용자 본인의 요청일 때만 받는다.
            if (Operator == info.Source && CancelOnRelease)
                CancelOperation();
        }

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
        }
    }
}
