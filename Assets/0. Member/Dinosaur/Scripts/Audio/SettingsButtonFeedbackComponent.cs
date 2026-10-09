using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

[DisallowMultipleComponent]
[RequireComponent(typeof(Button))]
public class SettingsButtonFeedbackComponent : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    [SerializeField, Min(1f)] private float hoverScale = 1.05f; //일반 버튼의 호버 확대 배율
    [SerializeField, Min(0f)] private float hoverDuration = 0.08f; //확대와 복귀에 걸리는 시간
    [SerializeField] private GameObject hoverName; //아이콘 옆에 표시할 탭 이름

    private Button button; //연출을 적용할 버튼
    private RectTransform buttonRect; //크기를 변경할 버튼 영역
    private Vector3 originalScale; //호버 전의 원래 크기
    private Coroutine scaleRoutine; //실행 중인 크기 연출
    private float currentScale = 1f; //현재 확대 배율
    private bool pointerInside; //마우스가 버튼 안에 있는지 여부

    private void Awake() //버튼과 원래 크기 보관
    {
        button = GetComponent<Button>();
        buttonRect = (RectTransform)transform;
        originalScale = buttonRect.localScale;
        if (hoverName != null) hoverName.SetActive(false);
    }

    public void OnPointerEnter(PointerEventData eventData) //탭 이름 표시와 일반 버튼 확대
    {
        pointerInside = true;
        if (hoverName != null) hoverName.SetActive(true);
        if (hoverScale > 1f && scaleRoutine == null) scaleRoutine = StartCoroutine(animateScale());
    }

    public void OnPointerExit(PointerEventData eventData) //탭 이름 숨김과 버튼 크기 복귀
    {
        pointerInside = false;
        if (hoverName != null) hoverName.SetActive(false);
    }

    private IEnumerator animateScale() //호버 중 비활성화도 반영하는 크기 연출
    {
        while (pointerInside || !Mathf.Approximately(currentScale, 1f))
        {
            float targetScale = pointerInside && button.IsInteractable() ? hoverScale : 1f; //입력 가능한 버튼만 확대
            currentScale = hoverDuration <= 0f ? targetScale : Mathf.MoveTowards(currentScale, targetScale,
                (hoverScale - 1f) * Time.unscaledDeltaTime / hoverDuration);
            buttonRect.localScale = originalScale * currentScale;
            yield return null;
        }
        scaleRoutine = null;
    }

    private void OnDisable() //창 닫기와 페이지 전환 시 연출 초기화
    {
        pointerInside = false;
        if (scaleRoutine != null) StopCoroutine(scaleRoutine);
        scaleRoutine = null;
        currentScale = 1f;
        if (buttonRect != null) buttonRect.localScale = originalScale;
        if (hoverName != null) hoverName.SetActive(false);
    }
}
