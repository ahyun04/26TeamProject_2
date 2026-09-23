using Fusion;
using UnityEngine;

namespace TrustNoOne.Missions
{
    /// <summary>
    /// [역할] "F 를 holdDuration 초 동안 누르고 있으면 완료"되는 미션 오브젝트 (기존 MissionInteractable 의 HoldTimer 동작을 옮김).
    ///  - F 누름 → TryBeginOperation (권한 검사는 베이스) / F 뗌 → 본인일 때만 취소
    ///  - 진행률은 호스트가 누적하고 동기화한다 → HoldProgress01 (홀드 게이지 HUD 가 읽음)
    ///
    /// [확장] 밸브·안테나 회전처럼 "누르는 동안 진행"하는 미니게임은 이 클래스를 상속해서
    ///  HoldDuration(목표 시간)과 OnStationRender(회전 연출)만 바꾼다. 훅을 override 할 때는 base 를 호출할 것.
    /// </summary>
    public class HoldStation : MissionStation, IHoldInteractable
    {
        [Header("F 홀드")]
        [Tooltip("F 를 유지해야 하는 시간(초).")]
        [Min(0.05f)]
        [SerializeField] private float holdDuration = 3f;

        [Networked] private float HoldElapsed { get; set; }

        /// <summary>완료까지 필요한 유지 시간. 하위 클래스가 랜덤 값 등으로 바꿀 수 있다.</summary>
        protected virtual float HoldDuration => holdDuration;

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

                return Mathf.Clamp01(HoldElapsed / Mathf.Max(0.05f, HoldDuration));
            }
        }

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
            TryBeginOperation(info.Source);
        }

        [Rpc(RpcSources.All, RpcTargets.StateAuthority, HostMode = RpcHostMode.SourceIsHostPlayer)]
        private void RPC_EndHold(RpcInfo info = default)
        {
            // 남이 내 세션을 끊지 못하게, 현재 사용자 본인의 요청일 때만 받는다.
            if (Operator == info.Source)
                CancelOperation();
        }

        protected override void OnOperationStarted(PlayerRef actor)
        {
            HoldElapsed = 0f;
        }

        protected override void OnHostTick(PlayerRef actor)
        {
            HoldElapsed += Runner.DeltaTime;

            if (HoldElapsed >= HoldDuration)
                CompleteBy(actor);
        }

        protected override void OnOperationCanceled(PlayerRef actor)
        {
            HoldElapsed = 0f;
        }

        protected override void OnResetHost()
        {
            HoldElapsed = 0f;
        }
    }
}
