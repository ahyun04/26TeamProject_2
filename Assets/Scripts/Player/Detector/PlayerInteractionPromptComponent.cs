using TMPro;
using LockdownProtocol.Lobby;
using UnityEngine;

[DefaultExecutionOrder(200)]
public class PlayerInteractionPromptComponent : MonoBehaviour
{
    [SerializeField] private RectTransform promptPanel; //아이템 옆에 표시할 안내 영역
    [SerializeField] private TMP_Text promptText; //획득 키와 소지 공간 안내
    [SerializeField] private Vector2 screenOffset = new Vector2(24f, 18f); //아이템 위쪽 화면 여백

    private Transform listener; //현재 본인 시점
    private Camera playerCamera; //화면 좌표 계산에 사용할 카메라
    private PlayerTargetDetector detector; //기존 조준 대상 탐지
    private PlayerItemController items; //본인의 소지 상태
    private PlayerInteraction interaction; //진행 중인 상호작용
    private PlayerHealth health; //사망과 게임 종료 제한

    private void Awake() //초기 표시 숨김
    {
        promptPanel.gameObject.SetActive(false);
    }

    private void LateUpdate() //카메라와 탐지 갱신 후 아이템 안내 위치 반영
    {
        Transform current = PlayerCameraController.LocalListenerTransform; //현재 로컬 카메라
        if (listener != current)
        {
            listener = current;
            playerCamera = current != null ? current.GetComponent<Camera>() : null;
            detector = current != null ? current.GetComponentInParent<PlayerTargetDetector>() : null;
            items = current != null ? current.GetComponentInParent<PlayerItemController>() : null;
            interaction = current != null ? current.GetComponentInParent<PlayerInteraction>() : null;
            health = current != null ? current.GetComponentInParent<PlayerHealth>() : null;
        }
        ItemBase item = detector != null ? detector.CurrentTarget as ItemBase : null; //지금 조준한 획득 대상
        bool starting = RoomManager.Instance != null && RoomManager.Instance.CurrentRoomState == RoomManager.RoomState.Starting; //게임 시작 전환 제한
        bool visible = playerCamera != null && health != null && health.CanAct && items != null &&
            item != null && item.Object != null && item.Object.IsValid && !item.IsHeld &&
            !starting && !items.isPickupPending && !GameAudio.blocksPlayerInput && !SessionDisconnectUIComponent.IsOpen &&
            Cursor.lockState == CursorLockMode.Locked && (interaction == null || !interaction.isInteracting);
        if (!visible) { promptPanel.gameObject.SetActive(false); return; }

        Collider itemCollider = item.GetComponentInChildren<Collider>(); //아이템의 화면 표시 기준
        Vector3 position = itemCollider != null ? itemCollider.bounds.center + Vector3.up * itemCollider.bounds.extents.y : item.transform.position; //아이템 위쪽 위치
        Vector3 screen = playerCamera.WorldToScreenPoint(position); //실제 카메라 기준 화면 좌표
        if (screen.z <= 0f) { promptPanel.gameObject.SetActive(false); return; }
        promptText.text = items.CurrentItemObject != null ? "주머니가 가득 찼습니다" : "[마우스 좌클릭] 잡기";
        RectTransform parent = (RectTransform)promptPanel.parent; //안내창을 배치할 캔버스 영역
        RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, (Vector2)screen + screenOffset, null, out Vector2 point);
        Vector2 halfSize = promptPanel.sizeDelta * 0.5f; //화면 바깥으로 나가지 않도록 할 안내 크기
        point.x = Mathf.Clamp(point.x, parent.rect.xMin + halfSize.x, parent.rect.xMax - halfSize.x);
        point.y = Mathf.Clamp(point.y, parent.rect.yMin + halfSize.y, parent.rect.yMax - halfSize.y);
        promptPanel.anchoredPosition = point;
        promptPanel.gameObject.SetActive(true);
    }

    private void OnDisable() //씬과 플레이어 표시 종료 시 안내 정리
    {
        if (promptPanel != null) promptPanel.gameObject.SetActive(false);
    }
}
