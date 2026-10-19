using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Main 씬에서 [Start]를 눌렀을 때 뜨는 닉네임 입력 팝업.
/// 확인하면 닉네임을 PlayerPrefs("Nickname")에 저장하고 Confirmed를 알린다(MainMenuUI가 받아서 로비로 이동).
/// 저장된 닉네임이 있으면 입력란에 미리 채워 두므로, 다음부터는 [확인]만 누르면 된다.
/// 같은 키를 PlayerPresence(초대 시스템)도 읽는다.
/// </summary>
public class NicknamePopupUI : MonoBehaviour
{
    public const string PrefsKey = "Nickname";
    public const string DefaultNickname = "Player";

    [SerializeField] private GameObject popupRoot;          // 팝업 전체 (평소엔 꺼 둠)
    [SerializeField] private TMP_InputField nicknameInput;
    [SerializeField] private Button confirmButton;
    [SerializeField] private Button cancelButton;
    [SerializeField] private TMP_Text feedbackText;
    [SerializeField, Min(1)] private int minLength = 2;
    [SerializeField, Min(1)] private int maxLength = 12;

    public event Action Confirmed;

    /// <summary>저장된 닉네임. 없으면 DefaultNickname.</summary>
    public static string GetSavedNickname() => PlayerPrefs.GetString(PrefsKey, DefaultNickname);

    public static bool HasSavedNickname() => PlayerPrefs.HasKey(PrefsKey);

    private void Awake()
    {
        if (popupRoot != null) popupRoot.SetActive(false);
        if (nicknameInput != null) nicknameInput.characterLimit = maxLength;
    }

    private void OnEnable()
    {
        if (confirmButton != null) confirmButton.onClick.AddListener(OnConfirmClicked);
        if (cancelButton != null) cancelButton.onClick.AddListener(Close);
        if (nicknameInput != null) nicknameInput.onSubmit.AddListener(OnSubmit);
    }

    private void OnDisable()
    {
        if (confirmButton != null) confirmButton.onClick.RemoveListener(OnConfirmClicked);
        if (cancelButton != null) cancelButton.onClick.RemoveListener(Close);
        if (nicknameInput != null) nicknameInput.onSubmit.RemoveListener(OnSubmit);
    }

    public void Open()
    {
        if (popupRoot == null)
        {
            Debug.LogWarning("[NicknamePopupUI] Popup Root가 연결되지 않았습니다. Inspector에서 연결하세요.");
            return;
        }
        popupRoot.SetActive(true);
        if (nicknameInput != null)
        {
            nicknameInput.text = HasSavedNickname() ? GetSavedNickname() : string.Empty;
            nicknameInput.ActivateInputField();
        }
        ShowFeedback(string.Empty);
    }

    public void Close()
    {
        if (popupRoot != null) popupRoot.SetActive(false);
    }

    private void OnSubmit(string _) => OnConfirmClicked();

    private void OnConfirmClicked()
    {
        string nickname = Sanitize(nicknameInput != null ? nicknameInput.text : string.Empty);

        if (nickname.Length < minLength)
        {
            ShowFeedback($"닉네임은 {minLength}자 이상 입력하세요.");
            return;
        }

        if (nickname.Length > maxLength) nickname = nickname.Substring(0, maxLength);

        PlayerPrefs.SetString(PrefsKey, nickname);
        PlayerPrefs.Save();

        Close();
        Confirmed?.Invoke();
    }

    // 앞뒤 공백 제거 + 리치 텍스트 태그로 쓰일 수 있는 < > 제거
    private static string Sanitize(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return string.Empty;
        return raw.Replace("<", string.Empty).Replace(">", string.Empty).Trim();
    }

    private void ShowFeedback(string message)
    {
        if (feedbackText != null) feedbackText.text = message;
    }
}