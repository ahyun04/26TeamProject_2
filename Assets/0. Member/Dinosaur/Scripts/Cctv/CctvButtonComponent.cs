using Fusion;
using UnityEngine;

[RequireComponent(typeof(BoxCollider))]
public class CctvButtonComponent : MonoBehaviour, ITargetable
{
    [SerializeField] private Cctv console; //호스트에 버튼 조작을 요청할 관제대
    [SerializeField] private int doorNumber; //0은 화면 전환, 1~6은 방화문 번호
    [SerializeField, Range(-1, 1)] private int cameraDirection; //이전 또는 다음 감시 영상
    [SerializeField] private CctvDoorComponent controlledDoor; //이 버튼이 조작하는 문
    [SerializeField] private GameObject aimOutline; //본인이 조준할 때만 표시할 버튼 테두리

    private BoxCollider hitBox; //모델 피벗이 아닌 실제 버튼의 충돌 영역

    public NetworkObject TargetObject => console != null ? console.Object : null;
    internal CctvDoorComponent door => controlledDoor;
    internal int direction => cameraDirection;
    internal Vector3 aimPoint => hitBox.bounds.center;

    private void Awake() //버튼 충돌 영역 캐시
    {
        hitBox = GetComponent<BoxCollider>();
    }
    internal string interactionHint => doorNumber == 0
        ? cameraDirection < 0 ? "[좌클릭] 이전 CCTV 화면" : "[좌클릭] 다음 CCTV 화면"
        : $"[좌클릭] {doorNumber}번 방화문 {(door != null && door.isClosed ? "열기" : "닫기")}";

    internal bool canUseLocally() => console != null && console.canUseLocally(this); //조준 안내 표시 조건

    internal bool tryPress() => console != null && console.requestPress(this); //플레이어의 좌클릭 요청을 관제대로 전달

    internal void setHighlighted(bool highlighted) //로컬 조준 표시 전환
    {
        if (aimOutline != null) aimOutline.SetActive(highlighted);
    }
}
