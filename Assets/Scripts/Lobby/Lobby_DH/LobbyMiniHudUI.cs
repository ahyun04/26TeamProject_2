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
        [SerializeField] private TMP_Text roomSummaryText; // 예: "Room: MyRoom  4/6"

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
            RefreshRoomSummary();
            RefreshMicIcon();
        }

        private void RefreshRoomSummary()
        {
            if (roomSummaryText == null || RoomManager.Instance == null) return;

            var room = RoomManager.Instance;
            var players = FindObjectsByType<LobbyPlayerController>(FindObjectsSortMode.None);
            roomSummaryText.text = $"Room : {room.RoomName}  {players.Length} / {room.MaxPlayerCount}";
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

        private void OnLeaveClicked()
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