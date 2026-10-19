using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class MainMenuUI : MonoBehaviour
{
    [SerializeField] private Button startButton;
    [SerializeField] private NicknamePopupUI nicknamePopup;
    [SerializeField] private string lobbySceneName = "Lobby";

    private bool _loading;

    private void Awake()
    {
        if (startButton != null)
            startButton.onClick.AddListener(OnStartClicked);
        if (nicknamePopup != null)
            nicknamePopup.Confirmed += LoadLobby;
    }

    private void OnDestroy()
    {
        if (startButton != null)
            startButton.onClick.RemoveListener(OnStartClicked);
        if (nicknamePopup != null)
            nicknamePopup.Confirmed -= LoadLobby;
    }

    private void OnStartClicked()
    {
        if (_loading) return;

        // 닉네임 팝업이 연결돼 있으면 먼저 닉네임을 받고, 없으면 바로 이동
        if (nicknamePopup != null) nicknamePopup.Open();
        else
        {
            Debug.LogWarning("[MainMenuUI] Nickname Popup이 연결되지 않아 바로 로비로 이동합니다.");
            LoadLobby();
        }
    }

    private void LoadLobby()
    {
        if (_loading) return;
        if (!Application.CanStreamedLevelBeLoaded(lobbySceneName))
        {
            Debug.LogError($"[MainMenuUI] 씬 '{lobbySceneName}'을(를) 불러올 수 없습니다. 이름이 정확한지, Build Profiles의 Scenes In Build에 등록돼 있는지 확인하세요.");
            return;
        }
        _loading = true;
        SceneManager.LoadScene(lobbySceneName);
    }
}