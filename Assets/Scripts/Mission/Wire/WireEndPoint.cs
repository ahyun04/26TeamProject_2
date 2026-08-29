using Fusion;
using UnityEngine;


/// <summary>
/// 전선을 연결하는 왼쪽의 도착 지점(WireEnd)을 담당
/// 직접 연결 상태를 변경하지 않고, 자신의 색상/인덱스/연결 위치 정보를 WireStartPoint에 제공한다
/// ITargetable을 구현하여 PlayerTargetDetector가 드래그 종료 대상을 감지할 수 있게 한다
/// </summary>
public class WireEndPoint : MonoBehaviour, ITargetable
{
    [SerializeField] private WiringMission mission;
    [SerializeField] private int index;
    [SerializeField] private WireColor wireColor;
    [SerializeField] private Transform anchor;

    public NetworkObject TargetObject => mission != null ? mission.Object : null;
    public WiringMission Mission => mission;
    public int Index => index;
    public WireColor Color => wireColor;
    public Transform Anchor => anchor != null ? anchor : transform;

    private void Awake()
    {
        if (mission == null)
            mission = GetComponentInParent<WiringMission>();
    }
}