using Fusion;

public enum MissionStatus
{
    InProgress,
    Completed,
    Failed
}


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

    // 현재 미션 상태
    public MissionStatus Status;

    public bool IsCompleted => Status == MissionStatus.Completed;
    public bool IsFailed => Status == MissionStatus.Failed;


    public MissionState(int missionId, PlayerRef owner, MissionRoleTarget roleTarget, MissionType missionType, int requiredCount)
    {
        MissionId = missionId;
        Owner = owner;
        RoleTarget = roleTarget;
        MissionType = missionType;
        Progress = 0;
        RequiredCount = requiredCount;
        Status = MissionStatus.InProgress;
    }


    /// <summary>
    /// 미션 진행도를 1 증가
    /// 이미 성공하거나 실패한 미션은 변경하지 않는다
    /// </summary>
    public void AddProgress()
    {
        if (Status != MissionStatus.InProgress)
            return;

        Progress++;

        if (Progress >= RequiredCount)
        {
            Progress = RequiredCount;
            Status = MissionStatus.Completed;
        }
    }


    /// <summary>
    /// 미션을 실패 상태로 변경
    /// 한 번 실패하면 다시 진행하거나 완료할 수 없다
    /// </summary>
    public void Fail()
    {
        if (Status != MissionStatus.InProgress)
            return;

        Status = MissionStatus.Failed;
    }
}