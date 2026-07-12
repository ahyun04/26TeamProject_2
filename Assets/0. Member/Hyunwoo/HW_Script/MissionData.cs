using UnityEngine;

[CreateAssetMenu(menuName = "Mission/Mission Data")]
public class MissionData : ScriptableObject
{
    [SerializeField] private int missionId;
    [SerializeField] private string missionName;
    [SerializeField] private string description;
    [SerializeField] private int targetCount = 1;

    public int MissionId => missionId;
    public string MissionName => missionName;
    public string Description => description;
    public int TargetCount => targetCount;
}