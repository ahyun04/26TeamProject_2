using TMPro;
using UnityEngine;
using UnityEngine.UI;
using LockdownProtocol.Networking;

namespace LockdownProtocol.Lobby
{
    /// <summary>
    /// 로비 메뉴 화면(방 만들기 / 방 참가 입력). NetworkBootstrap에 요청만 넘기고
    /// 성공 시 씬 전환은 Fusion의 SceneManager가 자동으로 처리한다.
    ///
    /// 방 목록(서버 세션 리스트를 받아와 표시)은 기획서 범위지만, Fusion의
    /// OnSessionListUpdated 콜백 연동은 별도 작업으로 남겨둠 (TODO) — 지금은
    /// 방 이름을 직접 입력해서 참가하는 방식만 지원.
    /// </summary>
    public class LobbyMenuUI : MonoBehaviour
    {
        [Header("Dependencies")]
        [SerializeField] private NetworkBootstrap bootstrap;

        [Header("Create Room")]
        [SerializeField] private TMP_InputField roomNameInput;
        [SerializeField] private TMP_InputField maxPlayersInput;
        [SerializeField] private Toggle isPrivateToggle;
        [SerializeField] private Button createRoomButton;

        [Header("Join Room")]
        [SerializeField] private TMP_InputField joinRoomNameInput;
        [SerializeField] private Button joinRoomButton;

        [Header("Feedback")]
        [SerializeField] private TMP_Text feedbackText;

        private bool _isBusy;

        private void OnEnable()
        {
            createRoomButton.onClick.AddListener(OnCreateClicked);
            joinRoomButton.onClick.AddListener(OnJoinClicked);
        }

        private void OnDisable()
        {
            createRoomButton.onClick.RemoveListener(OnCreateClicked);
            joinRoomButton.onClick.RemoveListener(OnJoinClicked);
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

            int maxPlayers = 6;
            if (maxPlayersInput != null && int.TryParse(maxPlayersInput.text, out int parsed))
            {
                maxPlayers = Mathf.Clamp(parsed, 2, 20);
            }

            _isBusy = true;
            SetInteractable(false);

            var result = await bootstrap.CreateRoom(roomName, maxPlayers, isPrivateToggle != null && isPrivateToggle.isOn);

            _isBusy = false;
            SetInteractable(true);

            if (!result.Ok)
            {
                ShowFeedback($"방 생성 실패: {result.ShutdownReason}");
            }
            // 성공 시 씬 전환은 NetworkBootstrap의 SceneManager가 자동 처리
        }

        private async void OnJoinClicked()
        {
            if (_isBusy) return;

            string roomName = joinRoomNameInput.text.Trim();
            if (string.IsNullOrEmpty(roomName))
            {
                ShowFeedback("참가할 방 이름을 입력하세요.");
                return;
            }

            _isBusy = true;
            SetInteractable(false);

            var result = await bootstrap.JoinRoom(roomName);
            var mapped = RoomManager.MapJoinResult(result);

            _isBusy = false;
            SetInteractable(true);

            switch (mapped)
            {
                case RoomManager.JoinRoomResult.RoomFull:
                    ShowFeedback("방이 가득 찼습니다");
                    break;
                case RoomManager.JoinRoomResult.NotFound:
                    ShowFeedback("존재하지 않는 방입니다");
                    break;
                case RoomManager.JoinRoomResult.GameStarted:
                    ShowFeedback("이미 게임이 시작되었습니다");
                    break;
                case RoomManager.JoinRoomResult.ConnectionError:
                    ShowFeedback("접속 오류가 발생했습니다");
                    break;
                case RoomManager.JoinRoomResult.Success:
                    // 씬 전환은 자동
                    break;
            }
        }

        private void SetInteractable(bool interactable)
        {
            createRoomButton.interactable = interactable;
            joinRoomButton.interactable = interactable;
        }

        private void ShowFeedback(string message)
        {
            if (feedbackText != null)
            {
                feedbackText.text = message;
            }
            Debug.Log($"[LobbyMenuUI] {message}");
        }
    }
}