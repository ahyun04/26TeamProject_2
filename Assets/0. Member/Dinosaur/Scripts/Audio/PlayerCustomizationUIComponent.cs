using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

public class PlayerCustomizationUIComponent : MonoBehaviour
{
    [SerializeField] private Button[] colorButtons; //공통 팔레트 선택 버튼
    [SerializeField] private TMP_Text statusText; //색상 중복과 변경 가능 상태 안내
    [SerializeField] private RawImage previewImage; //선택 색상의 캐릭터 미리보기
    [SerializeField] private GameObject previewModelPrefab; //네트워크 기능 없는 원본 캐릭터 모델
    [SerializeField] private RuntimeAnimatorController previewAnimator; //기존 캐릭터 정지 자세

    private UnityAction[] colorActions; //색상 버튼 구독 해제에 사용할 함수
    private GameObject previewModel; //현재 미리보기 모델
    private PlayerAppearanceVisualComponent previewVisual; //미리보기 기본색 담당
    private Camera previewCamera; //미리보기 전용 카메라
    private Light previewLight; //미리보기 전용 조명
    private RenderTexture previewTexture; //미리보기 표시용 렌더 결과
    private bool subscribed; //설정 변경 중복 구독 방지
    private bool lastCanEdit; //마지막 변경 허용 상태
    private int displayedIndex = -1; //마지막으로 표시한 색상

    private void Awake() //팔레트 입력 연결
    {
        colorActions = new UnityAction[colorButtons.Length];
        for (int i = 0; i < colorButtons.Length; i++)
        {
            int index = i; //각 버튼의 선택 색상
            colorActions[i] = () => PlayerAppearance.selectColor(index);
            colorButtons[i].onClick.AddListener(colorActions[i]);
        }
    }

    private void OnEnable() //설정 알림과 모델 미리보기 생성
    {
        if (!subscribed && PlayerAppearance.settings != null)
        {
            PlayerAppearance.settings.changed += refresh;
            subscribed = true;
        }
        createPreview();
        refresh();
    }

    private void Update() //게임 시작으로 변경 권한이 바뀐 경우 표시 갱신
    {
        if (lastCanEdit != PlayerAppearance.canCustomize || displayedIndex != PlayerAppearance.selectedColorIndex) refresh();
    }

    private void refresh() //선택 테두리·잠금 안내·캐릭터 색상 갱신
    {
        lastCanEdit = PlayerAppearance.canCustomize;
        displayedIndex = PlayerAppearance.selectedColorIndex;
        for (int i = 0; i < colorButtons.Length; i++)
        {
            colorButtons[i].image.color = PlayerAppearance.getColor(i);
            colorButtons[i].interactable = lastCanEdit;
            Outline outline = colorButtons[i].GetComponent<Outline>(); //선택 색상 표시
            if (outline != null) outline.enabled = i == displayedIndex;
        }
        if (statusText != null) statusText.text = lastCanEdit
            ? "다른 참가자가 사용 중인 색상은\n미사용 색상으로 자동 변경됩니다."
            : "게임 진행 중에는 색상을 변경할 수 없습니다.\n로비·대기실에서 변경해 주세요.";
        if (previewVisual != null)
        {
            previewVisual.setColor(PlayerAppearance.getColor(displayedIndex));
            previewCamera.Render();
        }
    }

    private void createPreview() //기존 모델을 플레이 공간과 분리해 렌더링
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
        SkinnedMeshRenderer renderer = previewModel.GetComponentInChildren<SkinnedMeshRenderer>(); //카메라 구도 기준
        Bounds bounds = renderer.bounds; //모델 실제 크기에 맞출 표시 범위
        if (bounds.size.y > 0f) previewModel.transform.localScale *= 1.45f / bounds.size.y;
        bounds = renderer.bounds;
        GameObject cameraObject = new("Character color preview"); //미리보기 카메라 오브젝트
        previewCamera = cameraObject.AddComponent<Camera>();
        previewCamera.enabled = false;
        previewCamera.clearFlags = CameraClearFlags.SolidColor;
        previewCamera.backgroundColor = new Color(0.075f, 0.105f, 0.15f);
        previewCamera.cullingMask = 1 << 31;
        previewCamera.orthographic = true;
        previewCamera.orthographicSize = bounds.extents.y * 1.12f;
        previewCamera.nearClipPlane = 0.01f;
        previewCamera.farClipPlane = Mathf.Max(10f, bounds.size.y * 8f);
        previewCamera.transform.position = bounds.center + Vector3.back * bounds.size.y * 3f;
        previewCamera.transform.LookAt(bounds.center);
        previewTexture = new RenderTexture(400, 520, 24);
        previewCamera.targetTexture = previewTexture;
        previewImage.texture = previewTexture;
        GameObject lightObject = new("Character color preview light"); //미리보기만 비추는 조명
        previewLight = lightObject.AddComponent<Light>();
        previewLight.type = LightType.Point;
        previewLight.cullingMask = 1 << 31;
        previewLight.intensity = 5f;
        previewLight.range = 6f;
        previewLight.shadows = LightShadows.None;
        previewLight.transform.position = bounds.center + new Vector3(-1f, 1.5f, -2f);
    }

    private void OnDisable() //페이지를 닫을 때 구독·렌더 리소스 정리
    {
        if (subscribed && PlayerAppearance.settings != null) PlayerAppearance.settings.changed -= refresh;
        subscribed = false;
        if (previewImage != null) previewImage.texture = null;
        if (previewCamera != null) previewCamera.targetTexture = null;
        if (previewModel != null) Destroy(previewModel);
        if (previewCamera != null) Destroy(previewCamera.gameObject);
        if (previewLight != null) Destroy(previewLight.gameObject);
        if (previewTexture != null) { previewTexture.Release(); Destroy(previewTexture); }
        previewModel = null;
        previewVisual = null;
        previewCamera = null;
        previewLight = null;
        previewTexture = null;
    }

    private void OnDestroy() //팔레트 입력 구독 정리
    {
        if (colorActions == null) return;
        for (int i = 0; i < colorActions.Length; i++) colorButtons[i].onClick.RemoveListener(colorActions[i]);
    }
}
