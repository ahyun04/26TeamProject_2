using Fusion;

namespace TrustNoOne.Missions
{
    /// <summary>
    /// [역할] 단체 미션 하나의 "공개" 진행 상태. MissionManager 의 [Networked] 배열에 담겨 모든 클라이언트에 복제된다.
    ///
    /// [왜 단체 미션만 여기에 담는가] [Networked] 는 필드 단위 가시성이 없어 모든 클라이언트에 복제된다.
    ///  개인 미션/행동 목표/살인마 미션은 비공개여야 하므로 절대 여기 담지 않는다 (호스트가 소유자에게만 RPC 로 전달).
    ///
    /// [DefIndex] MissionPool 안의 인덱스 (모든 클라이언트가 같은 Pool 을 가진다는 전제).
    ///
    /// [Deadline] 제한 시간 마감 시각 (호스트 Runner.SimulationTime 기준 초). 없으면 0.
    ///  클라이언트는 Deadline - Runner.SimulationTime 으로 남은 시간을 표시한다 (stage1 명세 2-4).
    ///
    /// [필드 타입] 기존 INetworkStruct 들과 같은 int/enum/float 조합만 쓴다.
    /// </summary>
    public struct TeamMissionState : INetworkStruct
    {
        public int DefIndex;
        public int Progress;
        public int Required;
        public ObjectiveStatus Status;
        public float Deadline;
    }
}
