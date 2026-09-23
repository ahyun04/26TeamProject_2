using Fusion;

namespace TrustNoOne.Missions
{
    /// <summary>
    /// [역할] "미션 이벤트를 받아주는 창구". 미션 오브젝트와 플레이어 스크립트(달리기, 아이템 전달, 방 방문 …)가
    ///        MissionManager 클래스를 직접 알지 않고 이 인터페이스만 보고 이벤트를 발행한다.
    ///
    /// [왜 인터페이스인가] 발행하는 쪽의 의존이 "MissionManager 전체"가 아니라 "Publish 하나"로 줄어든다.
    ///  → 미션 코어를 바꿔도 발행하는 쪽은 그대로이고, 테스트에서는 가짜 구현으로 갈아 끼울 수 있다.
    ///
    /// [호스트 전용] 두 메서드 모두 StateAuthority(호스트)에서만 의미가 있다.
    /// </summary>
    public interface IMissionEventSink
    {
        /// <summary>"actor 가 이런 행동을 했다"를 알린다. actor 는 호스트가 신뢰하는 값(RpcInfo.Source 등)이어야 한다.</summary>
        void Publish(in MissionEvent e);

        /// <summary>actor 가 eventType 행동을 했을 때 진행될 활성 목표가 있는가 (상호작용 허용 판단용).</summary>
        bool HasActiveObjective(PlayerRef actor, MissionEventType eventType);
    }
}
