using LockdownProtocol.Networking;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace LockdownProtocol.Lobby
{
    /// <summary>
    /// 기획서의 항상 떠 있는 미니 HUD: "Room : (Name) 4/6", 마이크 상태, 방 나가기.
    /// ESC 없이도 항상 화면에 보이고 클릭 가능해야 하는 요소들 — LobbyRoomUI(ESC 상세 패널)와
    /// 분리했다.
    /// </summary>
    public class LobbyMiniHudUI : MonoBehaviour
    {
        [Header("Room Info")]
        [Tooltip("좌측: 방 이름만 표시")]
        [SerializeField] private TMP_Text roomNameText;
        [Tooltip("우상단: 인원수만 표시 (예: 1 / 6)")]
        [SerializeField] private TMP_Text playerCountText;

        [Header("Mic")]
        [SerializeField] private Image micIcon;
        [SerializeField] private Sprite micOnSprite;
        [SerializeField] private Sprite micOffSprite;

        [Header("Leave")]
        [SerializeField] private Button leaveRoomButton;
        [SerializeField] private GameObject leaveConfirmPanel;
        [SerializeField] private Button leaveConfirmYes;
        [SerializeField] private Button leaveConfirmNo;

        private VoiceMuteController _localMuteController;

        private void OnEnable()
        {
            leaveRoomButton.onClick.AddListener(OnLeaveClicked);
            if (leaveConfirmYes != null) leaveConfirmYes.onClick.AddListener(OnLeaveConfirmed);
            if (leaveConfirmNo != null) leaveConfirmNo.onClick.AddListener(() => leaveConfirmPanel.SetActive(false));
            if (leaveConfirmPanel != null) leaveConfirmPanel.SetActive(false);
        }

        private void OnDisable()
        {
            leaveRoomButton.onClick.RemoveListener(OnLeaveClicked);
        }

        private void Update()
        {
            RefreshRoomInfo();
            RefreshMicIcon();
        }

        private void RefreshRoomInfo()
        {
            if (RoomManager.Instance == null) return;

            var room = RoomManager.Instance;

            if (roomNameText != null)
            {
                roomNameText.text = $"Room : {room.RoomName}";
            }

            if (playerCountText != null)
            {
                var players = FindObjectsByType<LobbyPlayerController>(FindObjectsSortMode.None);
                playerCountText.text = $"{players.Length} / {room.MaxPlayerCount}";
            }
        }

        private void RefreshMicIcon()
        {
            if (micIcon == null) return;

            // 로컬 플레이어 캐릭터에 붙은 VoiceMuteController를 한 번만 찾아서 캐싱
            if (_localMuteController == null)
            {
                foreach (var controller in FindObjectsByType<LobbyPlayerController>(FindObjectsSortMode.None))
                {
                    if (controller.Object != null && controller.Object.HasInputAuthority)
                    {
                        _localMuteController = controller.GetComponent<VoiceMuteController>();
                        break;
                    }
                }
            }

            if (_localMuteController == null) return;

            bool isMuted = _localMuteController.IsMuted;
            if (micOnSprite != null && micOffSprite != null)
            {
                micIcon.sprite = isMuted ? micOffSprite : micOnSprite;
            }
        }

        // ================== 방 나가기 ==================
        // DetailPanel(LobbyRoomUI)의 나가기 버튼도 여기 있는 확인창을 그대로 재사용한다
        // (같은 액션에 확인창을 두 벌 만들 필요 없음 - 기획서 목업엔 방나가기가 HUD와
        // 상세패널 둘 다에 있지만, 확인창 로직은 하나만 있으면 됨).

        private void OnLeaveClicked() => ShowLeaveConfirm();

        public void ShowLeaveConfirm()
        {
            if (leaveConfirmPanel != null)
            {
                leaveConfirmPanel.SetActive(true);
            }
            else
            {
                OnLeaveConfirmed();
            }
        }

        private void OnLeaveConfirmed()
        {
            if (leaveConfirmPanel != null) leaveConfirmPanel.SetActive(false);
            RoomManager.Instance?.RequestLeaveRoom();
        }
    }
}