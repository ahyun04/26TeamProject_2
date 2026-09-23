namespace TrustNoOne.Missions
{
    /// <summary>
    /// [역할] 목표 하나의 상태.
    ///
    /// [이름을 MissionStatus 로 하지 않은 이유]
    ///  기존 코드에 전역 MissionStatus 가 있다. 전역 네임스페이스 멤버는 using 으로 가져온 이름보다 우선하므로,
    ///  전역 스코프 파일에서 `using TrustNoOne.Missions;` 를 쓰면 조용히 옛 타입으로 해석될 수 있다.
    ///  이식 기간 중 그런 사고를 막기 위해 이름 자체를 다르게 했다.
    /// </summary>
    public enum ObjectiveStatus
    {
        InProgress = 0,
        Completed = 1,

        /// <summary>한 번 실패하면 다시 진행하거나 완료할 수 없다.</summary>
        Failed = 2,
    }
}
