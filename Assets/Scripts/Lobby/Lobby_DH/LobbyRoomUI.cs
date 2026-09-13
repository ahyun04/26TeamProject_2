using System.Collections.Generic;
using LockdownProtocol.Lobby.Invite;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace LockdownProtocol.Lobby
{
    public class LobbyRoomUI : MonoBehaviour
    {
        [Header("Panel Root")]
        [Tooltip("플레이어목록/Ready/게임시작을 전부 담는 '자식' 오브젝트를 연결해야 한다. " +
                 "이 스크립트가 붙은 오브젝트 자신을 넣으면 SetActive(false) 될 때 이 스크립트도 같이 꺼져서 " +
                 "ESC를 다시 눌러도 반응하지 않는 버그가 생긴다. 반드시 별도의 자식 패널 오브젝트를 만들어 연결할 것.")]
        [SerializeField] private GameObject detailPanelRoot;

        [Header("Room Info")]
        [SerializeField] private TMP_Text roomNameText;
        [SerializeField] private TMP_Text playerCountText;

        [Header("Player List")]
        [SerializeField] private Transform playerListContainer;
        [SerializeField] private PlayerListEntryUI playerEntryPrefab;

        [Header("Buttons")]
        [SerializeField] private Button readyButton;
        [SerializeField] private TMP_Text readyButtonLabel;
        [SerializeField] private Button startGameButton;
        [SerializeField] private Button leaveRoomButton;
        [SerializeField] private Button inviteButton;

        [Header("Invite")]
        [SerializeField] private InviteListPanel inviteListPanel;

        [Header("Feedback")]
        [SerializeField] private TMP_Text startFailText;

        private readonly List<PlayerListEntryUI> _spawnedEntries = new List<PlayerListEntryUI>();
        private LobbyGameStartManager _gameStartManager;
        private LobbyMiniHudUI _miniHud;
        private bool _isOpen;

        internal static LobbyRoomUI Instance { get; private set; }
        internal bool BlocksPlayerInput => _isOpen ||
            (_miniHud != null && _miniHud.IsLeaveConfirmationOpen) ||
            (inviteListPanel != null && inviteListPanel.IsOpen) ||
            (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject());


        private void OnEnable()
        {
            Instance = this;
            _gameStartManager = FindFirstObjectByType<LobbyGameStartManager>();
            if (_gameStartManager != null)
            {
                _gameStartManager.StartFailed += HandleStartFailed;
            }

            _miniHud = FindFirstObjectByType<LobbyMiniHudUI>();

            readyButton.onClick.AddListener(OnReadyClicked);
            startGameButton.onClick.AddListener(OnStartGameClicked);
            if (leaveRoomButton != null) leaveRoomButton.onClick.AddListener(OnLeaveClicked);
            if (inviteButton != null) inviteButton.onClick.AddListener(OnInviteClicked);

            if (startFailText != null) startFailText.text = string.Empty;

            SetOpen(false);
        }

        private void OnDisable()
        {
            if (Instance == this) Instance = null;
            if (_gameStartManager != null)
            {
                _gameStartManager.StartFailed -= HandleStartFailed;
            }

            readyButton.onClick.RemoveListener(OnReadyClicked);
            startGameButton.onClick.RemoveListener(OnStartGameClicked);
            if (leaveRoomButton != null) leaveRoomButton.onClick.RemoveListener(OnLeaveClicked);
            if (inviteButton != null) inviteButton.onClick.RemoveListener(OnInviteClicked);
        }

        private void Update()
        {
            if (_gameStartManager == null)
            {
                _gameStartManager = FindFirstObjectByType<LobbyGameStartManager>();
                if (_gameStartManager != null)
                    _gameStartManager.StartFailed += HandleStartFailed;
            }
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                SetOpen(!_isOpen);
            }

            if (!_isOpen) return;
            if (RoomManager.Instance == null || RoomManager.Instance.Object == null || !RoomManager.Instance.Object.IsValid) return;

            RefreshRoomInfo();
            RefreshPlayerList();
            RefreshButtons();
        }

        // ================== 패널 열기/닫기 ==================

        private void SetOpen(bool open)
        {
            _isOpen = open;


            // 미니 HUD도 클릭할 수 있도록 대기실에서는 커서를 유지한다.
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;


            if (detailPanelRoot == null)
            {
                Debug.LogWarning("[LobbyRoomUI] Detail Panel Root가 할당되지 않았습니다.");
            }
            else if (detailPanelRoot == gameObject)
            {
                Debug.LogError("[LobbyRoomUI] Detail Panel Root에 이 스크립트가 붙은 오브젝트 자신을 넣으면 안 됩니다 " +
                                "(SetActive(false) 시 이 스크립트도 같이 꺼져서 ESC가 다시 안 먹힘). 별도 자식 오브젝트를 만들어 연결하세요.");
            }
            else
            {
                detailPanelRoot.SetActive(open);
            }
        }

        // ================== 방 정보 ==================

        private void RefreshRoomInfo()
        {
            var room = RoomManager.Instance;
            if (roomNameText != null) roomNameText.text = room.RoomName.ToString();

            var players = FindObjectsByType<LobbyPlayerController>(FindObjectsSortMode.None);
            if (playerCountText != null)
            {
                playerCountText.text = $"{players.Length} / {room.MaxPlayerCount} Players";
            }
        }

        // ================== 플레이어 목록 ==================

        private void RefreshPlayerList()
        {
            var players = FindObjectsByType<LobbyPlayerController>(FindObjectsSortMode.None);
            System.Array.Sort(players, (left, right) => left.JoinOrder.CompareTo(right.JoinOrder));

            EnsureEntryCount(players.Length);

            for (int i = 0; i < players.Length; i++)
            {
                var p = players[i];
                _spawnedEntries[i].gameObject.SetActive(true);
                _spawnedEntries[i].Bind(p.Nickname.ToString(), p.IsReady, p.IsHost);
            }

            for (int i = players.Length; i < _spawnedEntries.Count; i++)
            {
                _spawnedEntries[i].gameObject.SetActive(false);
            }
        }

        private void EnsureEntryCount(int count)
        {
            while (_spawnedEntries.Count < count)
            {
                var entry = Instantiate(playerEntryPrefab, playerListContainer);
                _spawnedEntries.Add(entry);
            }
        }

        // ================== 버튼 상태 ==================

        private void RefreshButtons()
        {
            var localPlayer = GetLocalLobbyPlayer();

            bool isHost = localPlayer != null && localPlayer.IsHost;
            startGameButton.gameObject.SetActive(isHost);

            if (readyButtonLabel != null && localPlayer != null)
            {
                readyButtonLabel.text = localPlayer.IsReady ? "READY" : "READY?";
            }

            if (isHost)
            {
                var players = FindObjectsByType<LobbyPlayerController>(FindObjectsSortMode.None);
                bool allReady = players.Length > 0;
                foreach (var p in players)
                {
                    if (!p.IsReady) { allReady = false; break; }
                }
                startGameButton.interactable = allReady;
            }
        }

        private LobbyPlayerController GetLocalLobbyPlayer()
        {
            foreach (var p in FindObjectsByType<LobbyPlayerController>(FindObjectsSortMode.None))
            {
                if (p.Object != null && p.Object.HasInputAuthority) return p;
            }
            return null;
        }

        // ================== 버튼 핸들러 ==================

        private void OnReadyClicked()
        {
            GetLocalLobbyPlayer()?.ToggleReady();
        }

        private void OnStartGameClicked()
        {
            if (_gameStartManager == null)
                _gameStartManager = FindFirstObjectByType<LobbyGameStartManager>();

            _gameStartManager?.RPC_RequestStartGame();
        }

        private void OnLeaveClicked()
        {
            // 확인창은 LobbyMiniHudUI가 들고 있는 걸 그대로 재사용 
            _miniHud?.ShowLeaveConfirm();
        }

        private void OnInviteClicked()
        {
            inviteListPanel?.Open();
        }

        private void HandleStartFailed(LobbyGameStartManager.StartFailReason reason)
        {
            if (startFailText == null) return;

            startFailText.text = reason switch
            {
                LobbyGameStartManager.StartFailReason.NotHost => "방장만 시작할 수 있습니다",
                LobbyGameStartManager.StartFailReason.NotEnoughPlayers => "최소 인원이 부족합니다",
                LobbyGameStartManager.StartFailReason.NotAllReady => "모든 플레이어가 준비되지 않았습니다",
                LobbyGameStartManager.StartFailReason.AlreadyStarting => "이미 시작 절차가 진행 중입니다",
                _ => "게임을 시작할 수 없습니다"
            };
        }
    }
}
