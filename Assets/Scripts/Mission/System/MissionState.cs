using Fusion;

public struct MissionState : INetworkStruct
{
    public int MissionId;

    // 개인 미션 주인
    // Shared 미션은 PlayerRef.None
    public PlayerRef Owner;

    public MissionRoleTarget RoleTarget;
    public MissionType MissionType;

    public int Progress;
    public int RequiredCount;

    public NetworkBool IsCompleted;


    public MissionState(int missionId, PlayerRef owner, MissionRoleTarget roleTarget, MissionType missionType, int requiredCount)
    {
        MissionId = missionId;
        Owner = owner;
        RoleTarget = roleTarget;
        MissionType = missionType;
        Progress = 0;
        RequiredCount = requiredCount;
        IsCompleted = false;
    }


    /// <summary>
    /// 미션 진행도를 1 증가
    /// </summary>
    public void AddProgress()
    {
        if (IsCompleted)
            return;

        Progress++;

        if (Progress >= RequiredCount)
        {
            Progress = RequiredCount;
            IsCompleted = true;
        }
    }
}