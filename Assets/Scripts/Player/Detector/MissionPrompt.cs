using UnityEngine;


public enum MissionPromptMode
{
    HighlightOnly,
    PromptAndHighlight
}


/// <summary>
/// 미션 감지 시 보여지는 텍스트와 외각선 관리
/// </summary>
public class MissionPrompt : MonoBehaviour
{
    [SerializeField] private MissionPromptMode mode = MissionPromptMode.PromptAndHighlight;

    [SerializeField] private GameObject promptObject;
    [SerializeField] private GameObject highlightObject;


    private void Awake() { Hide(); }

    public void Show() { SetVisible(true); }

    public void Hide() { SetVisible(false); }

    private void SetVisible(bool visible)
    {
        if (promptObject != null)
            promptObject.SetActive(visible && mode == MissionPromptMode.PromptAndHighlight);

        if (highlightObject != null)
            highlightObject.SetActive(visible);
    }
}