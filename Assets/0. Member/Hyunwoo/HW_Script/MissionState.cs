using Fusion;

/// <summary>
/// 실제로 호스트에게 전달할 네트워크 데이터
/// </summary>
public struct MissionState : INetworkStruct
{
    public int MissionId;
    public PlayerRef Owner;
    public int CurrentCount;
    public NetworkBool IsPersonal;
    public NetworkBool IsCompleted;
}
