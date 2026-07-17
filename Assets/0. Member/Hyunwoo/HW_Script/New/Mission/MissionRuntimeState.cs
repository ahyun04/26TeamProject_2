using Fusion;

/// <summary>
/// 배정된 플레이어, 현재 진행도, 목표 진행도 담당 스크립트
/// </summary>
public struct MissionRuntimeState : INetworkStruct
{
    public int MissionId;

    public MissionKind MissionKind; // 단체, 시민, 살인자 역할 구분

    public PlayerRef Owner; // 개인 미션 배정받는 플레이어

    public int Progress; // 현재 진행도      

    public int TargetCount; // 완료에 필요한 진행도     

    // 현재 진행도 확인
    public NetworkBool IsCompleted => Progress >= TargetCount;
}
