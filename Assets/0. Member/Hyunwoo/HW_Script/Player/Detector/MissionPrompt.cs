using UnityEngine;

/// <summary>
/// 미션 감지 시 보여지는 텍스트와 외각선을 구현하는 스크립트
/// </summary>
public class MissionPrompt : MonoBehaviour
{
    [SerializeField] private GameObject promptObject;
    [SerializeField] private GameObject highlightObject;

    private void Awake()
    {
        Hide();
    }

    public void Show()
    {
        SetVisible(true);
    }

    public void Hide()
    {
        SetVisible(false);
    }

    private void SetVisible(bool visible)
    {
        if (promptObject != null)
            promptObject.SetActive(visible);

        if (highlightObject != null)
            highlightObject.SetActive(visible);
    }
}