using UnityEngine;


/// <summary>
/// 변하지 않는 고정 미션 데이터값
/// </summary>
[CreateAssetMenu(fileName = "Mission", menuName = "Game/Mission Data")]
public class MissionData : ScriptableObject
{
    // 기본 정보
    [SerializeField] private int missionId;
    [SerializeField] string missionName;
    [SerializeField] MissionKind missionKind;
    

    // 역할별 설명
    [TextArea]
    [SerializeField] string citizenDescription;

    [TextArea]
    [SerializeField] string killerDescription;


    // 완료 조건
    [SerializeField, Min(1)]
    private int targetCount = 1;


    public int MissionId => missionId;
    public MissionKind MissionKind => missionKind;
    public string MissionName => missionName;
    public int TargetCount => targetCount;


    public string GetDescription(PlayerRole role)
    {
        return role == PlayerRole.Citizen
            ? citizenDescription
            : killerDescription;
    }
}
