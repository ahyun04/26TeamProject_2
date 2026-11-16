using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Events;
using UnityEngine.UI;

public class PlayerCustomizationUIComponent : MonoBehaviour
{
    [SerializeField] private GameObject[] selectionMarks; //현재 색상의 체크 표시
    [SerializeField] private Button[] colorButtons; //기존 팔레트 버튼
    [SerializeField] private TMP_Text statusText; //선택 제한과 대기 상태 안내
    [SerializeField] private RawImage previewImage; //선택 외형의 캐릭터 미리보기
    [SerializeField] private GameObject previewModelPrefab; //원본 캐릭터 모델
    [SerializeField] private RuntimeAnimatorController previewAnimator; //캐릭터 정지 자세
    [SerializeField] private Button[] categoryButtons; //색깔, 얼굴, 머리, 몸 순서의 탭
    [SerializeField] private RectTransform[] categoryChevrons; //열린 카테고리의 화살표
    [SerializeField] private Sprite activeCategorySprite; //열린 카테고리의 파란 배경
    [SerializeField] private Sprite inactiveCategorySprite; //접힌 카테고리의 배경
    [SerializeField] private ScrollRect categoryScroll; //카테고리와 펼친 목록의 세로 스크롤
    [SerializeField] private LayoutElement itemTray; //선택한 카테고리 아래 놓이는 아이템 영역
    [SerializeField, Min(0f)] private float colorTrayHeight = 218f; //색상 두 줄에 필요한 높이
    [SerializeField, Min(0f)] private float accessoryTrayHeight = 184f; //장식 한 줄에 필요한 높이
    [SerializeField] private ScrollRect itemScroll; //가로로 이동하는 꾸미기 목록
    [SerializeField] private RectTransform colorContent; //기존 색상 선택 영역
    [SerializeField] private RectTransform accessoryContent; //현재 장식 목록의 선택 영역
    [SerializeField] private Button accessoryButtonPrefab; //이름과 그림을 표시할 장식 선택 버튼
    [SerializeField] private Button rotatePreviewButton; //정면과 후면 미리보기 전환
    [SerializeField, Min(1f)] private float previewDragDegreesPerWidth = 360f; //미리보기 너비만큼 드래그할 때 회전하는 각도
    [SerializeField] private Button applyButton; //변경한 설정 저장
    [SerializeField] private Button cancelButton; //미적용 변경 취소
    [SerializeField] private Button defaultsButton; //꾸미기와 설정 기본값 복원
    [SerializeField] private Button closeButton; //미적용 변경을 취소하고 닫기

    private readonly List<(int id, Button button, GameObject mark)> accessoryOptions = new(); //현재 카테고리의 장식과 체크 표시
    private readonly List<TMP_Text> colorLabels = new(); //사용 중인 색상 표시
    private PlayerAppearanceSlot selectedSlot; //현재 선택한 카테고리
    private PlayerAppearanceSettingsComponent settings; //편집 중인 외형 데이터
    private GameObject previewModel; //같은 탭 안에서 유지하는 미리보기 모델
    private PlayerAppearanceVisualComponent previewVisual; //실제 캐릭터와 공유하는 외형 표시
    private Camera previewCamera; //미리보기 전용 카메라
    private Light previewLight; //캐릭터 미리보기 조명
    private Light previewFillLight; //반대편 그림자를 부드럽게 하는 조명
    private RenderTexture previewTexture; //미리보기 표시용 렌더 결과
    private Vector3 previewCenter; //정면과 후면에서 같은 구도의 기준점
    private float previewDistance; //캐릭터와 미리보기 카메라 사이 거리
    private float previewYaw; //캐릭터를 바라보는 수평 각도
    private bool canEdit; //선택이 허용된 마지막 상태
    private int occupiedColors; //다른 참가자가 사용 중인 마지막 색상 목록
    private PlayerAppearanceSelection? displayed; //마지막으로 렌더링한 전체 외형
    private bool usedFallback; //입장 때 저장한 색상 대신 다른 색상이 배정된 상태
    private string actionMessage; //기존 설정창의 적용 결과와 기본값 안내

    private void Awake() //공용 프리팹의 선택 입력 연결
    {
        accessoryButtonPrefab.gameObject.SetActive(false);
        for (int i = 0; i < categoryButtons.Length; i++)
        {
            PlayerAppearanceSlot slot = (PlayerAppearanceSlot)i; //각 카테고리 버튼의 부위
            categoryButtons[i].onClick.AddListener(() => showCategory(slot));
        }
        for (int i = 0; i < colorButtons.Length; i++)
        {
            int index = i; //선택할 색깔 인덱스
            colorButtons[i].onClick.AddListener(() => PlayerAppearance.select(PlayerAppearanceSlot.Color, index));
            colorLabels.Add(colorButtons[i].transform.Find("Color name").GetComponent<TMP_Text>());
        }
        rotatePreviewButton.onClick.AddListener(rotatePreview);
        EventTrigger previewEvents = rotatePreviewButton.gameObject.AddComponent<EventTrigger>(); //미리보기 위에서만 회전 입력 받기
        var beginDrag = new EventTrigger.Entry { eventID = EventTriggerType.BeginDrag };
        beginDrag.callback.AddListener(data => ((PointerEventData)data).eligibleForClick = false);
        previewEvents.triggers.Add(beginDrag);
        var drag = new EventTrigger.Entry { eventID = EventTriggerType.Drag };
        drag.callback.AddListener(data => dragPreview((PointerEventData)data));
        previewEvents.triggers.Add(drag);
    }

    internal void bindActions(UnityAction apply, UnityAction close, UnityAction restoreDefaults) //설정창 진입점의 저장과 취소 처리 재사용
    {
        applyButton.onClick.AddListener(apply);
        cancelButton.onClick.AddListener(close);
        defaultsButton.onClick.AddListener(restoreDefaults);
        closeButton.onClick.AddListener(close);
    }

    internal void setActionsInteractable(bool interactable) //적용 응답 대기 중 꾸미기 버튼 잠금
    {
        applyButton.interactable = interactable;
        cancelButton.interactable = interactable;
        defaultsButton.interactable = interactable;
        closeButton.interactable = interactable;
    }

    internal void setActionMessage(string message) //공용 설정 저장 결과를 하단에 표시
    {
        actionMessage = message;
        if (isActiveAndEnabled) refreshStatus();
    }

    private void OnEnable() //현재 외형에 맞춰 미리보기를 다시 표시
    {
        settings = PlayerAppearance.settings;
        if (settings != null) settings.changed += refresh;
        createPreview();
        showCategory(selectedSlot);
    }

    private void Update() //방 시작과 다른 참가자의 색상 변경 표시
    {
        if (canEdit != (PlayerAppearance.canCustomize && !PlayerAppearance.isApplying) ||
            occupiedColors != PlayerAppearance.getOccupiedColors() || usedFallback != PlayerAppearance.hasFallbackColor)
            refresh();
    }

    private void showCategory(PlayerAppearanceSlot slot) //미리보기와 편집값을 유지한 채 목록만 변경
    {
        selectedSlot = slot;
        int categoryIndex = (int)slot; //목록을 펼칠 카테고리의 순서
        bool showColors = slot == PlayerAppearanceSlot.Color; //팔레트 또는 장식 목록 표시
        itemTray.transform.SetAsLastSibling();
        itemTray.transform.SetSiblingIndex(categoryButtons[categoryIndex].transform.GetSiblingIndex() + 1);
        itemTray.preferredHeight = showColors ? colorTrayHeight : accessoryTrayHeight;
        colorContent.gameObject.SetActive(showColors);
        accessoryContent.gameObject.SetActive(!showColors);
        itemScroll.content = showColors ? colorContent : accessoryContent;
        if (!showColors) fillAccessories(slot);
        for (int i = 0; i < categoryButtons.Length; i++)
        {
            bool expanded = i == categoryIndex; //카테고리 헤더의 활성 상태
            categoryButtons[i].image.sprite = expanded ? activeCategorySprite : inactiveCategorySprite;
            categoryChevrons[i].localRotation = expanded ? Quaternion.Euler(0f, 0f, 180f) : Quaternion.identity;
        }
        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(categoryScroll.content);
        LayoutRebuilder.ForceRebuildLayoutImmediate(itemScroll.content);
        categoryScroll.StopMovement();
        categoryScroll.verticalNormalizedPosition = 1f;
        itemScroll.StopMovement();
        itemScroll.horizontalNormalizedPosition = 0f;
        scrollToSelection();
        setPreviewYaw(slot == PlayerAppearanceSlot.Body ? 215f : 0f);
        refresh();
    }

    private void scrollToSelection() //현재 선택한 항목을 가로 목록에서 화면에 표시
    {
        int selectedId = settings != null ? settings.selected.get(selectedSlot) : 0; //선택 항목의 ID
        Button selectedButton = selectedSlot == PlayerAppearanceSlot.Color ? colorButtons[selectedId] :
            accessoryOptions.Find(option => option.id == selectedId).button; //현재 선택한 색상 또는 장식 버튼
        float visibleWidth = itemScroll.viewport.rect.width; //목록에서 한 번에 보이는 너비
        float hiddenWidth = itemScroll.content.rect.width - visibleWidth; //가로로 이동 가능한 너비
        if (selectedButton == null || hiddenWidth <= 0f) return;

        RectTransform selectedRect = (RectTransform)selectedButton.transform; //선택 칸의 레이아웃 위치
        float left = selectedRect.anchoredPosition.x - selectedRect.rect.width * selectedRect.pivot.x; //컨텐츠 안에서 선택 칸의 왼쪽
        float right = left + selectedRect.rect.width; //컨텐츠 안에서 선택 칸의 오른쪽
        float offset = itemScroll.horizontalNormalizedPosition * hiddenWidth; //현재 목록의 왼쪽 위치
        if (left < offset) offset = left;
        else if (right > offset + visibleWidth) offset = right - visibleWidth;
        itemScroll.horizontalNormalizedPosition = Mathf.Clamp01(offset / hiddenWidth);
    }

    private void fillAccessories(PlayerAppearanceSlot slot) //선택 슬롯과 리소스가 연결된 장식만 노출
    {
        foreach (var option in accessoryOptions)
        {
            option.button.gameObject.SetActive(false);
            Destroy(option.button.gameObject);
        }
        accessoryOptions.Clear();
        addAccessoryButton(slot, 0, "없음", null);
        PlayerCosmeticsCatalog catalog = (settings != null ? settings : PlayerAppearance.settings)?.catalog; //플레이어와 공용 장식 데이터
        if (catalog == null || catalog.items == null || !catalog.hasResources(slot)) return;
        foreach (PlayerCosmeticsCatalog.Entry entry in catalog.items)
            if (entry != null && entry.slot == slot && entry.isAvailable)
                addAccessoryButton(slot, entry.id, getAccessoryLabel(slot, entry.displayName),
                    entry.thumbnail != null ? entry.thumbnail : entry.sticker);
    }

    private static string getAccessoryLabel(PlayerAppearanceSlot slot, string displayName) //머리 탭에서 구분되는 장식 이름만 표시
    {
        const string headbandSuffix = " 머리띠"; //같은 부위 장식에 반복되는 단어
        if (slot == PlayerAppearanceSlot.Head && !string.IsNullOrEmpty(displayName) &&
            displayName.EndsWith(headbandSuffix, StringComparison.Ordinal))
            return displayName.Substring(0, displayName.Length - headbandSuffix.Length);
        return displayName;
    }

    private void addAccessoryButton(PlayerAppearanceSlot slot, int id, string name, Sprite sprite) //공용 버튼에 이름과 그림 연결
    {
        Button button = Instantiate(accessoryButtonPrefab, accessoryContent); //장식 카드
        button.transform.Find("Name").GetComponent<TMP_Text>().text = name;
        Image icon = button.transform.Find("Icon").GetComponent<Image>(); //장식 원본 미리보기 그림
        if (sprite != null) icon.sprite = sprite;
        button.onClick.AddListener(() => PlayerAppearance.select(slot, id));
        button.gameObject.SetActive(true);
        accessoryOptions.Add((id, button, button.transform.Find("Selected").gameObject));
    }

    private void refresh() //선택 강조, 점유 상태, 캐릭터 미리보기 갱신
    {
        canEdit = PlayerAppearance.canCustomize && !PlayerAppearance.isApplying;
        occupiedColors = PlayerAppearance.getOccupiedColors();
        usedFallback = PlayerAppearance.hasFallbackColor;
        PlayerAppearanceSelection selection = settings != null ? settings.selected : default; //모든 슬롯의 임시 외형
        for (int i = 0; i < colorButtons.Length; i++)
        {
            bool occupied = (occupiedColors & (1 << i)) != 0; //다른 참가자가 이미 선택한 색상
            bool selected = selection.color == i; //현재 미리보기의 색상
            colorButtons[i].image.color = PlayerAppearance.getColor(i);
            colorButtons[i].interactable = canEdit && !occupied;
            colorLabels[i].text = occupied ? "사용 중" : string.Empty;
            if (selectionMarks != null && i < selectionMarks.Length && selectionMarks[i] != null) selectionMarks[i].SetActive(selected);
        }
        foreach (var option in accessoryOptions)
        {
            option.button.interactable = canEdit;
            option.mark.SetActive(selection.get(selectedSlot) == option.id);
        }
        refreshStatus();
        if (previewVisual == null || (displayed.HasValue && displayed.Value.Equals(selection))) return;
        if (displayed.HasValue && displayed.Value.get(selectedSlot) != selection.get(selectedSlot)) scrollToSelection();
        displayed = selection;
        previewVisual.setAppearance(selection, PlayerAppearance.getColor(selection.color));
        previewCamera.Render();
    }

    private void refreshStatus() //선택 제한이나 저장 결과가 있을 때만 안내 표시
    {
        if (!string.IsNullOrEmpty(actionMessage)) statusText.text = actionMessage;
        else if (PlayerAppearance.isApplying) statusText.text = "외형을 적용하는 중입니다.";
        else if (!canEdit) statusText.text = "외형은 메인 메뉴, 로비 또는 대기실에서 변경할 수 있습니다.";
        else if (usedFallback && selectedSlot == PlayerAppearanceSlot.Color) statusText.text = "저장한 색상을 다른 참가자가 사용 중이라 이 방에서는 다른 색상이 배정되었습니다.";
        else if (selectedSlot == PlayerAppearanceSlot.Color && occupiedColors != 0) statusText.text = "사용 중인 색상은 다른 참가자가 선택한 색상입니다.";
        else if (selectedSlot != PlayerAppearanceSlot.Color && accessoryOptions.Count == 1) statusText.text = "표시할 수 있는 장식이 없습니다.";
        else statusText.text = string.Empty;
    }

    private void createPreview() //기존 모델·조명으로 실제 외형을 별도 렌더링
    {
        if (previewModel != null || previewModelPrefab == null || previewImage == null) return;
        previewModel = Instantiate(previewModelPrefab, new Vector3(10000f, 10000f, 10000f), Quaternion.Euler(0f, 180f, 0f));
        foreach (Transform part in previewModel.GetComponentsInChildren<Transform>(true)) part.gameObject.layer = 31;
        Animator animator = previewModel.GetComponentInChildren<Animator>(); //기존 정지 애니메이션만 사용
        if (animator != null && previewAnimator != null)
        {
            animator.runtimeAnimatorController = previewAnimator;
            animator.Play("Blend Tree", 0, 0.3f);
            animator.Update(0f);
            animator.speed = 0f;
        }
        previewVisual = previewModel.AddComponent<PlayerAppearanceVisualComponent>();
        previewVisual.initializeCosmetics(settings != null ? settings.catalog : null, previewModel.transform);
        SkinnedMeshRenderer renderer = previewModel.GetComponentInChildren<SkinnedMeshRenderer>(); //카메라 구도 기준
        Bounds bounds = renderer.bounds; //모델의 실제 크기
        if (bounds.size.y > 0f) previewModel.transform.localScale *= 1.45f / bounds.size.y;
        bounds = renderer.bounds;
        previewCenter = bounds.center + Vector3.up * 0.02f;
        previewDistance = bounds.size.y * 3f;
        GameObject cameraObject = new("Character appearance preview"); //미리보기 카메라
        previewCamera = cameraObject.AddComponent<Camera>();
        previewCamera.enabled = false;
        previewCamera.clearFlags = CameraClearFlags.SolidColor;
        previewCamera.backgroundColor = Color.clear;
        previewCamera.cullingMask = 1 << 31;
        previewCamera.orthographic = true;
        previewCamera.orthographicSize = bounds.extents.y * 1.20f;
        previewCamera.nearClipPlane = 0.01f;
        previewCamera.farClipPlane = Mathf.Max(10f, bounds.size.y * 8f);
        previewTexture = new RenderTexture(576, 692, 24, RenderTextureFormat.ARGB32);
        previewCamera.targetTexture = previewTexture;
        previewImage.texture = previewTexture;
        previewLight = createPreviewLight(cameraObject.transform, "Key light", 5.8f);
        previewFillLight = createPreviewLight(cameraObject.transform, "Fill light", 2.7f);
        setPreviewYaw(previewYaw);
    }

    private static Light createPreviewLight(Transform parent, string lightName, float intensity) //캐릭터에만 영향을 주는 미리보기 조명 생성
    {
        var light = new GameObject(lightName).AddComponent<Light>(); //카메라와 함께 해제되는 조명
        light.transform.SetParent(parent);
        light.type = LightType.Point;
        light.cullingMask = 1 << 31;
        light.intensity = intensity;
        light.range = 7f;
        light.shadows = LightShadows.None;
        return light;
    }

    private void rotatePreview() //클릭과 키보드 선택으로 반대편 확인
    {
        setPreviewYaw(previewYaw + 180f);
    }

    private void dragPreview(PointerEventData pointer) //마우스를 수평으로 움직인 만큼 캐릭터를 둘러봄
    {
        RectTransform rect = (RectTransform)rotatePreviewButton.transform;
        if (pointer.button != PointerEventData.InputButton.Left || rect.rect.width <= 0f ||
            !RectTransformUtility.ScreenPointToLocalPointInRectangle(rect, pointer.position, pointer.pressEventCamera, out Vector2 current) ||
            !RectTransformUtility.ScreenPointToLocalPointInRectangle(rect, pointer.position - pointer.delta, pointer.pressEventCamera, out Vector2 previous))
            return;
        setPreviewYaw(previewYaw - (current.x - previous.x) / rect.rect.width * previewDragDegreesPerWidth);
    }

    private void setPreviewYaw(float degrees) //캐릭터 기준으로 미리보기 카메라와 조명 회전
    {
        previewYaw = Mathf.Repeat(degrees, 360f);
        if (previewCamera == null) return;
        Quaternion orbit = Quaternion.Euler(0f, previewYaw, 0f);
        previewCamera.transform.position = previewCenter + orbit * (Vector3.back * previewDistance);
        previewCamera.transform.LookAt(previewCenter);
        previewLight.transform.position = previewCenter + orbit * new Vector3(-1f, 1.5f, -2f);
        previewFillLight.transform.position = previewCenter + orbit * new Vector3(1.3f, 0.45f, -1.7f);
        previewCamera.Render();
    }

    private void OnDisable() //페이지 전환 시 미리보기 리소스 해제
    {
        if (settings != null) settings.changed -= refresh;
        if (previewImage != null) previewImage.texture = null;
        if (previewCamera != null) previewCamera.targetTexture = null;
        if (previewModel != null) { previewModel.SetActive(false); Destroy(previewModel); }
        if (previewCamera != null) { previewCamera.gameObject.SetActive(false); Destroy(previewCamera.gameObject); }
        if (previewTexture != null) { previewTexture.Release(); Destroy(previewTexture); }
        previewModel = null;
        previewVisual = null;
        previewCamera = null;
        previewLight = null;
        previewFillLight = null;
        previewTexture = null;
        displayed = null;
    }
}
