using TMPro;
using UnityEngine;
using UnityEngine.UI;
using LockdownProtocol.Networking;

namespace LockdownProtocol.Lobby
{
    /// <summary>
    /// 로비 메뉴 화면: [빠른 입장] / [방 생성] / [초대 코드 입력 + 입력 버튼] 4가지만 담당한다.
    /// - [방 생성]은 다음 화면(Create Panel, CreateRoomPanelUI)을 열기만 한다.
    /// - 초대 코드로 참가하면 성공 시 씬 전환은 Fusion의 SceneManager가 자동으로 처리한다.
    /// - [빠른 입장]은 껍데기(TODO: Fusion 로비 세션 목록 연동).
    /// </summary>
    public class LobbyMenuUI : MonoBehaviour
    {
        [Header("Dependencies")]
        [SerializeField] private NetworkBootstrap bootstrap;

        [Header("Quick Join (껍데기)")]
        [SerializeField] private Button quickJoinButton;

        [Header("Create Room")]
        [SerializeField] private Button createRoomButton;
        [Tooltip("방 생성 다음 화면(CreateRoomPanelUI가 붙은 오브젝트). 이 스크립트가 붙은 오브젝트 자신은 넣지 말 것")]
        [SerializeField] private GameObject createPanel;

        [Header("Join By Invite Code")]
        [SerializeField] private TMP_InputField joinRoomNameInput; // 초대 코드 입력란
        [SerializeField] private Button joinRoomButton;            // [입력] 버튼

        [Header("Feedback")]
        [SerializeField] private TMP_Text feedbackText;

        private bool _isBusy;

        private void OnEnable()
        {
            createRoomButton.onClick.AddListener(OnCreateRoomButtonClicked);
            joinRoomButton.onClick.AddListener(OnJoinClicked);
            if (quickJoinButton != null) quickJoinButton.onClick.AddListener(OnQuickJoinClicked);
            if (createPanel != null) createPanel.SetActive(false);
        }

        private void OnDisable()
        {
            createRoomButton.onClick.RemoveListener(OnCreateRoomButtonClicked);
            joinRoomButton.onClick.RemoveListener(OnJoinClicked);
            if (quickJoinButton != null) quickJoinButton.onClick.RemoveListener(OnQuickJoinClicked);
        }

        /// <summary>빠른 입장 - 껍데기. TODO: Fusion 로비 세션 목록(OnSessionListUpdated)에서
        /// 공개 + Waiting + 자리 있는 방을 골라 JoinRoom(세션명) 호출.</summary>
        private void OnQuickJoinClicked()
        {
            if (_isBusy) return;
            ShowFeedback("빠른 입장은 준비 중입니다");
        }

        private void OnCreateRoomButtonClicked()
        {
            if (_isBusy) return;
            if (createPanel == null)
            {
                Debug.LogWarning("[LobbyMenuUI] Create Panel이 연결되지 않았습니다.");
                return;
            }
            ShowFeedback(string.Empty);
            createPanel.SetActive(true);
        }

        private async void OnJoinClicked()
        {
            if (_isBusy) return;

            string inviteCode = joinRoomNameInput.text.Trim();
            if (string.IsNullOrEmpty(inviteCode))
            {
                ShowFeedback("초대 코드를 입력하세요.");
                return;
            }

            _isBusy = true;
            SetInteractable(false);

            try
            {
                var result = await bootstrap.JoinRoom(inviteCode);
                if (this == null) return;
                var mapped = RoomManager.MapJoinResult(result);

                switch (mapped)
                {
                    case RoomManager.JoinRoomResult.RoomFull:
                        ShowFeedback("방이 가득 찼습니다");
                        break;
                    case RoomManager.JoinRoomResult.NotFound:
                        ShowFeedback("유효하지 않은 초대 코드이거나 참가할 수 없는 방입니다");
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
        }

        private void SetInteractable(bool interactable)
        {
            createRoomButton.interactable = interactable;
            joinRoomButton.interactable = interactable;
            if (quickJoinButton != null) quickJoinButton.interactable = interactable;
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