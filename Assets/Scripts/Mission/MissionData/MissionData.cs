using UnityEngine;

public enum MissionRoleTarget
{
    All,
    Citizen,
    Killer
}

public enum MissionType
{
    Shared,
    Personal,
    PersonalAction
}

/// <summary>
/// 미션의 정의를 설명하는 원본 데이터 
/// </summary>
[CreateAssetMenu(menuName = "Mission/Mission Data")]
public class MissionData : ScriptableObject
{
    [SerializeField] private int id;                    
    [SerializeField] private string Name;               
    [SerializeField] MissionRoleTarget roleTarget;
    [SerializeField] MissionType missionType;
    [SerializeField] private int requiredCount = 1;

  

    public int Id => id;    // 어떤 미션인가?

    public MissionRoleTarget RoleTarget => roleTarget;  // 누가 하는가?

    public MissionType MissionType => missionType;  // 어떤 종류인가?

    public int RequiredCount => requiredCount;  // 몇 번 해야 완료인가?
}

