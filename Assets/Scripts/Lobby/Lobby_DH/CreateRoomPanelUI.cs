using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace LockdownProtocol.Lobby
{
    /// <summary>
    /// 로비의 [방 생성]을 눌렀을 때 나오는 다음 화면(방 이름/인원/비공개 입력 -> 생성).
    /// 로비 화면(LobbyMenuUI)은 이 입력 필드들을 알 필요가 없도록 분리했다.
    /// 이 스크립트는 'Create Panel' 오브젝트에 붙인다 (LobbyMenuUI가 SetActive로 열고 닫는다).
    /// </summary>
    public class CreateRoomPanelUI : MonoBehaviour
    {
        [Header("Dependencies")]
        [SerializeField] private NetworkBootstrap bootstrap;

        [Header("Inputs")]
        [SerializeField] private TMP_InputField roomNameInput;
        [Header("Max Players (◀ n ▶)")]
        [SerializeField] private TMP_Text maxPlayersText;
        [SerializeField] private Button maxPlayersDecreaseButton;
        [SerializeField] private Button maxPlayersIncreaseButton;
        [SerializeField] private int minPlayers = 2;
        [SerializeField] private int maxPlayersLimit = 10;
        [SerializeField] private int defaultMaxPlayers = 6;

        [Header("Options")]
        [SerializeField] private Toggle isPrivateToggle;

        [Header("Buttons")]
        [SerializeField] private Button confirmCreateButton;
        [SerializeField] private Button cancelButton;

        [Header("Feedback")]
        [SerializeField] private TMP_Text feedbackText;

        private bool _isBusy;
        private int _maxPlayers;

        private void OnEnable()
        {
            confirmCreateButton.onClick.AddListener(OnCreateClicked);
            if (cancelButton != null) cancelButton.onClick.AddListener(Close);
            if (maxPlayersDecreaseButton != null) maxPlayersDecreaseButton.onClick.AddListener(OnDecrease);
            if (maxPlayersIncreaseButton != null) maxPlayersIncreaseButton.onClick.AddListener(OnIncrease);

            // 패널을 열 때마다 기본값으로 초기화
            SetMaxPlayers(defaultMaxPlayers);
            ShowFeedback(string.Empty);
        }

        private void OnDisable()
        {
            confirmCreateButton.onClick.RemoveListener(OnCreateClicked);
            if (cancelButton != null) cancelButton.onClick.RemoveListener(Close);
            if (maxPlayersDecreaseButton != null) maxPlayersDecreaseButton.onClick.RemoveListener(OnDecrease);
            if (maxPlayersIncreaseButton != null) maxPlayersIncreaseButton.onClick.RemoveListener(OnIncrease);
        }

        private void OnDecrease() { if (!_isBusy) SetMaxPlayers(_maxPlayers - 1); }
        private void OnIncrease() { if (!_isBusy) SetMaxPlayers(_maxPlayers + 1); }

        private void SetMaxPlayers(int value)
        {
            _maxPlayers = Mathf.Clamp(value, minPlayers, maxPlayersLimit);
            if (maxPlayersText != null) maxPlayersText.text = _maxPlayers.ToString();
            // 끝값에서는 해당 방향 화살표를 비활성화
            if (maxPlayersDecreaseButton != null) maxPlayersDecreaseButton.interactable = _maxPlayers > minPlayers;
            if (maxPlayersIncreaseButton != null) maxPlayersIncreaseButton.interactable = _maxPlayers < maxPlayersLimit;
        }

        public void Close()
        {
            if (_isBusy) return;
            gameObject.SetActive(false);
        }

        private async void OnCreateClicked()
        {
            if (_isBusy) return;

            string roomName = roomNameInput.text.Trim();
            if (string.IsNullOrEmpty(roomName))
            {
                ShowFeedback("방 이름을 입력하세요.");
                return;
            }

            int maxPlayers = _maxPlayers;

            _isBusy = true;
            SetInteractable(false);

            try
            {
                var result = await bootstrap.CreateRoom(roomName, maxPlayers, isPrivateToggle != null && isPrivateToggle.isOn);
                if (this != null && !result.Ok)
                    ShowFeedback($"방 생성 실패: {result.ShutdownReason}");
            }
            catch (System.Exception exception)
            {
                if (this != null) ShowFeedback(exception.Message);
                Debug.LogException(exception);
            }
            finally
            {
                _isBusy = false;
                if (this != null) SetInteractable(true);
            }
            // 성공 시 씬 전환은 NetworkBootstrap의 SceneManager가 자동 처리
        }

        private void SetInteractable(bool interactable)
        {
            confirmCreateButton.interactable = interactable;
            if (cancelButton != null) cancelButton.interactable = interactable;
            if (interactable) SetMaxPlayers(_maxPlayers); // 화살표 상태 복원
            else
            {
                if (maxPlayersDecreaseButton != null) maxPlayersDecreaseButton.interactable = false;
                if (maxPlayersIncreaseButton != null) maxPlayersIncreaseButton.interactable = false;
            }
        }

        private void ShowFeedback(string message)
        {
            if (feedbackText != null) feedbackText.text = message;
            if (!string.IsNullOrEmpty(message)) Debug.Log($"[CreateRoomPanelUI] {message}");
        }
    }
}