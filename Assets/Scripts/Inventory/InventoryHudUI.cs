using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 화면의 주머니 슬롯 2개 UI. 내 PlayerItemController를 스스로 찾아서 슬롯 아이콘과 선택 강조를 표시한다.
/// 아이템 소모·삭제로 슬롯이 비는 경우도 놓치지 않도록 이벤트 대신 매 프레임 값을 읽어 갱신한다.
/// 내 캐릭터가 없거나(관전자) 죽었으면 숨긴다.
/// 이 스크립트는 항상 켜 둔 오브젝트에 붙이고, 실제로 보이는 UI는 panel 아래에 둔다.
/// </summary>
public class InventoryHudUI : MonoBehaviour
{
    [System.Serializable]
    private class SlotView
    {
        public Image frame; //슬롯 테두리(선택 시 색이 바뀜)
        public Image icon;  //슬롯에 든 아이템 아이콘
    }

    [SerializeField] private GameObject panel;          //슬롯 UI 전체 (사망·관전 시 숨김)
    [SerializeField] private SlotView[] slots = new SlotView[2];
    [SerializeField] private Color selectedColor = new Color32(0xFF, 0xD5, 0x4F, 0xFF); //선택 슬롯 테두리
    [SerializeField] private Color normalColor = new Color32(0xFF, 0xFF, 0xFF, 0x70);   //선택 안 된 슬롯 테두리
    [SerializeField, Min(0.1f)] private float iconScale = 2f;                            //슬롯 크기는 그대로, 아이템 아이콘만 확대하는 배율
    [SerializeField] private bool flipIconHorizontal;                                   //켜면 아이콘을 좌우로 뒤집는다 (/ → \)
    [SerializeField] private float iconRotationZ = -135f;                                     //아이콘 회전 각도 (도). 양수=반시계, 음수=시계 방향. 기본 -135 = 원래 '손잡이 오른쪽 위'를 '손잡이 아래, 입구 위'로
    [SerializeField, Min(0.1f)] private float findInterval = 0.5f;                       //내 캐릭터 탐색 간격

    private PlayerItemController controller;
    private PlayerHealth health;
    private float nextFindTime;

    private void Update()
    {
        if (!isControllerValid())
        {
            controller = null;
            health = null;
            setPanelVisible(false);
            tryFindController();
            return;
        }

        bool show = health == null || !health.IsDead;
        setPanelVisible(show);
        if (show) refreshSlots();
    }

    private bool isControllerValid()
    {
        return controller != null && controller.Object != null && controller.Object.IsValid;
    }

    private void tryFindController() //내 캐릭터(입력 권한이 있는 플레이어)의 아이템 컨트롤러를 찾는다
    {
        if (Time.unscaledTime < nextFindTime) return;
        nextFindTime = Time.unscaledTime + findInterval;

        foreach (PlayerItemController candidate in FindObjectsByType<PlayerItemController>(FindObjectsSortMode.None))
        {
            if (candidate.Object == null || !candidate.Object.IsValid || !candidate.HasInputAuthority) continue;

            controller = candidate;
            health = candidate.GetComponent<PlayerHealth>();
            return;
        }
    }

    private void refreshSlots()
    {
        int selected = controller.selectedSlot;

        for (int i = 0; i < slots.Length; i++)
        {
            SlotView view = slots[i];
            if (view == null) continue;

            ItemData data = controller.getSlotItemData(i);
            //ItemData에 Icon이 있으면 그것을, 없으면 아이템 모델을 작게 찍은 미리보기를 쓴다
            Sprite sprite = data != null ? ItemIconProvider.Get(data, controller.getSlotItemModel(i)) : null;

            if (view.icon != null)
            {
                view.icon.sprite = sprite;
                view.icon.enabled = sprite != null;
                view.icon.rectTransform.localScale = new Vector3(flipIconHorizontal ? -iconScale : iconScale, iconScale, 1f);
                view.icon.rectTransform.localRotation = Quaternion.Euler(0f, 0f, iconRotationZ);
            }

            if (view.frame != null)
                view.frame.color = i == selected ? selectedColor : normalColor;
        }
    }

    private void setPanelVisible(bool visible)
    {
        if (panel != null && panel.activeSelf != visible)
            panel.SetActive(visible);
    }
}