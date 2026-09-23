using Fusion;

namespace TrustNoOne.Missions
{
    /// <summary>
    /// [역할] "누가 무엇을 했다"는 사실 하나. 게임 시스템(상호작용, 이동, 아이템 …)이 발행하고
    ///        MissionEventRouter 가 목표들에게 전달한다.
    ///
    /// [왜 값 타입(readonly struct)인가] 이벤트는 자주 발생하고 호스트 안에서만 오간다. 힙 할당 없이 전달한다.
    ///
    /// [보안 규칙] Actor 는 반드시 호스트가 신뢰할 수 있는 값(RpcInfo.Source 등)으로 채울 것.
    ///  클라이언트가 파라미터로 보낸 "나는 누구다"를 그대로 넣으면 남의 이름으로 미션을 진행시킬 수 있다.
    /// </summary>
    public readonly struct MissionEvent
    {
        public readonly MissionEventType Type;

        /// <summary>행동을 한 플레이어.</summary>
        public readonly PlayerRef Actor;

        /// <summary>행동의 대상 (오브젝트 ID, 방/지역 ID, 대상 플레이어 등). 의미 없으면 0.</summary>
        public readonly int TargetId;

        /// <summary>한 번에 몇 회로 칠 것인가 (기본 1, 최소 1).</summary>
        public readonly int Amount;

        public MissionEvent(MissionEventType type, PlayerRef actor, int targetId = 0, int amount = 1)
        {
            Type = type;
            Actor = actor;
            TargetId = targetId;
            Amount = amount < 1 ? 1 : amount;
        }
    }
}
