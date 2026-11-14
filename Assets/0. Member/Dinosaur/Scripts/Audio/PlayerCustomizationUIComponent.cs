using System.Collections.Generic;
using TMPro;
using UnityEngine;
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
    [SerializeField] private ScrollRect itemScroll; //가로로 이동하는 꾸미기 목록
    [SerializeField] private RectTransform colorContent; //기존 색상 선택 영역
    [SerializeField] private RectTransform accessoryContent; //현재 장식 목록의 선택 영역
    [SerializeField] private Button accessoryButtonPrefab; //이름과 그림을 표시할 장식 선택 버튼
    [SerializeField] private Button rotatePreviewButton; //정면과 후면 미리보기 전환
    [SerializeField] private TMP_Text rotatePreviewLabel; //미리보기 전환 버튼의 현재 동작

    private static readonly Color activeTabColor = new(0.14f, 0.43f, 0.68f); //선택 카테고리와 장식 테두리
    private static readonly Color inactiveTabColor = new(0.14f, 0.19f, 0.27f); //일반 선택 버튼 배경
    private static readonly string[] colorNames = { "파랑", "빨강", "초록", "노랑", "보라", "하늘", "분홍", "주황", "연두", "흰색", "회색", "갈색", "남색", "민트", "연보라" }; //팔레트 색 이름
    private readonly List<int> accessoryIds = new(); //생성한 장식 버튼의 데이터 ID
    private readonly List<Button> accessoryButtons = new(); //현재 카테고리의 장식 선택 버튼
    private readonly List<TMP_Text> colorLabels = new(); //색상 이름과 사용 중 표시
    private PlayerAppearanceSlot selectedSlot; //현재 선택한 카테고리
    private PlayerAppearanceSettingsComponent settings; //편집 중인 외형 데이터
    private GameObject previewModel; //같은 탭 안에서 유지하는 미리보기 모델
    private PlayerAppearanceVisualComponent previewVisual; //실제 캐릭터와 공유하는 외형 표시
    private Camera previewCamera; //미리보기 전용 카메라
    private Light previewLight; //캐릭터 미리보기 조명
    private RenderTexture previewTexture; //미리보기 표시용 렌더 결과
    private Vector3 previewCenter; //정면과 후면에서 같은 구도의 기준점
    private float previewDistance; //캐릭터와 미리보기 카메라 사이 거리
    private bool viewBack; //현재 미리보기가 캐릭터 뒤를 보는지 여부
    private bool canEdit; //선택이 허용된 마지막 상태
    private int occupiedColors; //다른 참가자가 사용 중인 마지막 색상 목록
    private PlayerAppearanceSelection? displayed; //마지막으로 렌더링한 전체 외형
    private bool usedFallback; //입장 때 저장한 색상 대신 다른 색상이 배정된 상태

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
        showCategory(PlayerAppearanceSlot.Color);
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
        bool showColors = slot == PlayerAppearanceSlot.Color; //팔레트 또는 장식 목록 표시
        colorContent.gameObject.SetActive(showColors);
        accessoryContent.gameObject.SetActive(!showColors);
        itemScroll.content = showColors ? colorContent : accessoryContent;
        if (!showColors) fillAccessories(slot);
        for (int i = 0; i < categoryButtons.Length; i++)
            categoryButtons[i].image.color = i == (int)slot ? activeTabColor : inactiveTabColor;
        if (!isActiveAndEnabled) return;
        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(itemScroll.content);
        itemScroll.StopMovement();
        scrollToSelection();
        setPreviewView(slot == PlayerAppearanceSlot.Body);
        refresh();
    }

    private void scrollToSelection() //현재 선택한 항목을 가로 목록에서 화면에 표시
    {
        int selectedId = settings != null ? settings.selected.get(selectedSlot) : 0; //선택 항목의 ID
        int index = selectedSlot == PlayerAppearanceSlot.Color ? selectedId : accessoryIds.IndexOf(selectedId); //선택 항목의 가로 순서
        float hiddenWidth = itemScroll.content.rect.width - itemScroll.viewport.rect.width; //목록이 이동할 수 있는 범위
        GridLayoutGroup row = itemScroll.content.GetComponent<GridLayoutGroup>(); //공용 한 줄 목록의 칸 크기
        float offset = row.padding.left + index * (row.cellSize.x + row.spacing.x) + row.cellSize.x * 0.5f
            - itemScroll.viewport.rect.width * 0.5f; //선택한 칸을 가급적 가운데에 배치
        itemScroll.horizontalNormalizedPosition = index < 0 || hiddenWidth <= 0f ? 0f : Mathf.Clamp01(offset / hiddenWidth);
    }

    private void fillAccessories(PlayerAppearanceSlot slot) //선택 슬롯과 리소스가 연결된 장식만 노출
    {
        foreach (Button button in accessoryButtons)
        {
            button.gameObject.SetActive(false);
            Destroy(button.gameObject);
        }
        accessoryButtons.Clear();
        accessoryIds.Clear();
        addAccessoryButton(slot, 0, "없음", null);
        PlayerCosmeticsCatalog catalog = (settings != null ? settings : PlayerAppearance.settings)?.catalog; //플레이어와 공용 장식 데이터
        if (catalog == null || catalog.items == null || !catalog.hasResources(slot)) return;
        foreach (PlayerCosmeticsCatalog.Entry entry in catalog.items)
            if (entry != null && entry.slot == slot && entry.isAvailable)
                addAccessoryButton(slot, entry.id, entry.displayName, entry.thumbnail != null ? entry.thumbnail : entry.sticker);
    }

    private void addAccessoryButton(PlayerAppearanceSlot slot, int id, string name, Sprite sprite) //공용 버튼에 이름과 그림 연결
    {
        Button button = Instantiate(accessoryButtonPrefab, accessoryContent); //장식 카드
        button.transform.Find("Name").GetComponent<TMP_Text>().text = name;
        Image icon = button.transform.Find("Icon").GetComponent<Image>(); //장식 원본 미리보기 그림
        icon.sprite = sprite;
        icon.enabled = sprite != null;
        button.onClick.AddListener(() => PlayerAppearance.select(slot, id));
        button.gameObject.SetActive(true);
        accessoryIds.Add(id);
        accessoryButtons.Add(button);
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
            colorLabels[i].text = occupied ? colorNames[i] + "\n사용 중" : colorNames[i];
            if (selectionMarks != null && i < selectionMarks.Length && selectionMarks[i] != null) selectionMarks[i].SetActive(selected);
            Outline outline = colorButtons[i].GetComponent<Outline>(); //선택 색상 가장자리
            if (outline != null) outline.enabled = selected;
        }
        for (int i = 0; i < accessoryButtons.Count; i++)
        {
            Button button = accessoryButtons[i]; //현재 슬롯의 장식 선택 버튼
            bool selected = selection.get(selectedSlot) == accessoryIds[i]; //선택한 장식의 강조
            button.interactable = canEdit;
            button.image.color = selected ? activeTabColor : inactiveTabColor;
            button.GetComponent<Outline>().enabled = selected;
        }
        if (statusText != null)
        {
            if (PlayerAppearance.isApplying) statusText.text = "외형을 적용하는 중입니다.";
            else if (!canEdit) statusText.text = "외형은 메인 메뉴, 로비 또는 대기실에서 변경할 수 있습니다.";
            else if (usedFallback && selectedSlot == PlayerAppearanceSlot.Color) statusText.text = "저장한 색상을 다른 참가자가 사용 중이라 이 방에서는 다른 색상이 배정되었습니다.";
            else if (selectedSlot == PlayerAppearanceSlot.Color && occupiedColors != 0) statusText.text = "사용 중인 색상은 다른 참가자가 선택한 색상입니다.";
            else if (selectedSlot != PlayerAppearanceSlot.Color && accessoryButtons.Count == 1) statusText.text = "표시할 수 있는 장식이 없습니다.";
            else statusText.text = string.Empty;
        }
        if (previewVisual == null || (displayed.HasValue && displayed.Value.Equals(selection))) return;
        if (displayed.HasValue && displayed.Value.get(selectedSlot) != selection.get(selectedSlot)) scrollToSelection();
        displayed = selection;
        previewVisual.setAppearance(selection, PlayerAppearance.getColor(selection.color));
        previewCamera.Render();
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
        previewCenter = bounds.center + Vector3.up * 0.08f;
        previewDistance = bounds.size.y * 3f;
        GameObject cameraObject = new("Character appearance preview"); //미리보기 카메라
        previewCamera = cameraObject.AddComponent<Camera>();
        previewCamera.enabled = false;
        previewCamera.clearFlags = CameraClearFlags.SolidColor;
        previewCamera.backgroundColor = new Color(0.075f, 0.105f, 0.15f);
        previewCamera.cullingMask = 1 << 31;
        previewCamera.orthographic = true;
        previewCamera.orthographicSize = bounds.extents.y * 1.20f;
        previewCamera.nearClipPlane = 0.01f;
        previewCamera.farClipPlane = Mathf.Max(10f, bounds.size.y * 8f);
        previewTexture = new RenderTexture(400, 520, 24);
        previewCamera.targetTexture = previewTexture;
        previewImage.texture = previewTexture;
        GameObject lightObject = new("Character appearance preview light"); //미리보기만 비추는 조명
        previewLight = lightObject.AddComponent<Light>();
        previewLight.type = LightType.Point;
        previewLight.cullingMask = 1 << 31;
        previewLight.intensity = 5f;
        previewLight.range = 6f;
        previewLight.shadows = LightShadows.None;
        setPreviewView(viewBack);
    }

    private void rotatePreview() //같은 캐릭터의 앞면과 뒷면 확인
    {
        setPreviewView(!viewBack);
    }

    private void setPreviewView(bool back) //등 장식 선택 시 뒷면으로 즉시 이동
    {
        viewBack = back;
        rotatePreviewLabel.text = back ? "앞면 보기" : "뒷면 보기";
        if (previewCamera == null) return;
        Vector3 direction = back ? new Vector3(0.18f, 0f, 1f).normalized : Vector3.back; //선택점 확인이 쉬운 미리보기 방향
        previewCamera.transform.position = previewCenter + direction * previewDistance;
        previewCamera.transform.LookAt(previewCenter);
        previewLight.transform.position = previewCenter + new Vector3(-1f, 1.5f, back ? 2f : -2f);
        previewCamera.Render();
    }

    private void OnDisable() //페이지 전환 시 미리보기 리소스 해제
    {
        if (settings != null) settings.changed -= refresh;
        if (previewImage != null) previewImage.texture = null;
        if (previewCamera != null) previewCamera.targetTexture = null;
        if (previewModel != null) { previewModel.SetActive(false); Destroy(previewModel); }
        if (previewCamera != null) Destroy(previewCamera.gameObject);
        if (previewLight != null) { previewLight.enabled = false; Destroy(previewLight.gameObject); }
        if (previewTexture != null) { previewTexture.Release(); Destroy(previewTexture); }
        previewModel = null;
        previewVisual = null;
        previewCamera = null;
        previewLight = null;
        previewTexture = null;
        displayed = null;
    }
}
