using Fusion;

namespace TrustNoOne.Missions
{
    /// <summary>
    /// [역할] 단체 미션 하나의 "공개" 진행 상태. MissionManager 의 [Networked] 배열에 담겨 모든 클라이언트에 복제된다.
    ///
    /// [왜 단체 미션만 여기에 담는가]
    ///  Fusion 의 [Networked] 데이터는 모든 클라이언트에 복제되고 필드 단위 가시성이 없다.
    ///  단체 진행도는 기획서상 모두에게 보이는 정보라 여기 담아도 되지만,
    ///  개인 미션/행동 목표/살인마 미션은 비공개여야 하므로 절대 여기 담지 않는다 (호스트가 소유자에게만 RPC 로 전달).
    ///
    /// [DefIndex] MissionPool 안의 인덱스. 문자열 ID 보다 패킷이 작고, 모든 클라이언트가 같은 Pool 을 가진다는 전제를 쓴다.
    ///
    /// [필드 타입을 int 로 통일한 이유] 이 프로젝트의 기존 INetworkStruct(MissionState 등)가 int/enum 조합으로 이미 검증돼 있다.
    ///  byte 로 줄이면 패킷이 조금 줄지만, 8칸 배열이라 이득이 미미하고 미검증 조합을 늘릴 이유가 없다.
    /// </summary>
    public struct TeamMissionState : INetworkStruct
    {
        public int DefIndex;
        public int Progress;
        public int Required;
        public ObjectiveStatus Status;
    }
}
