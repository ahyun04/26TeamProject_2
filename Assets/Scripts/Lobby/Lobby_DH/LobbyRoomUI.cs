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
        [Tooltip("초대 코드 표시 (대문자로 보여줌)")]
        [SerializeField] private TMP_Text inviteCodeText;
        [Tooltip("초대 코드 옆 복사 버튼")]
        [SerializeField] private Button copyInviteCodeButton;

        [Header("Max Players (방장 전용 ◀ n ▶)")]
        [SerializeField] private TMP_Text maxPlayerText;
        [SerializeField] private Button maxPlayerDecreaseButton;
        [SerializeField] private Button maxPlayerIncreaseButton;

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
        [Tooltip("안내 문구가 표시되는 시간(초). 지나면 자동으로 사라진다")]
        [SerializeField] private float feedbackDuration = 3f;

        private readonly List<PlayerListEntryUI> _spawnedEntries = new List<PlayerListEntryUI>();
        private LobbyGameStartManager _gameStartManager;
        private LobbyMiniHudUI _miniHud;
        private bool _isOpen;

        internal static LobbyRoomUI Instance { get; private set; }
        internal bool IsRoomMenuOpen => _isOpen; //공용 설정 버튼의 표시 조건
        internal bool BlocksPlayerInput => SessionDisconnectUIComponent.IsOpen || GameAudio.blocksPlayerInput || _isOpen ||
            (_miniHud != null && _miniHud.IsLeaveConfirmationOpen) ||
            (inviteListPanel != null && inviteListPanel.isActiveAndEnabled && inviteListPanel.IsOpen);


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
            if (copyInviteCodeButton != null) copyInviteCodeButton.onClick.AddListener(OnCopyInviteCodeClicked);
            if (maxPlayerDecreaseButton != null) maxPlayerDecreaseButton.onClick.AddListener(OnMaxPlayerDecrease);
            if (maxPlayerIncreaseButton != null) maxPlayerIncreaseButton.onClick.AddListener(OnMaxPlayerIncrease);

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
            if (copyInviteCodeButton != null) copyInviteCodeButton.onClick.RemoveListener(OnCopyInviteCodeClicked);
            if (maxPlayerDecreaseButton != null) maxPlayerDecreaseButton.onClick.RemoveListener(OnMaxPlayerDecrease);
            if (maxPlayerIncreaseButton != null) maxPlayerIncreaseButton.onClick.RemoveListener(OnMaxPlayerIncrease);
            UnbindRoomEvents();
        }

        private RoomManager _boundRoom;

        private void BindRoomEvents(RoomManager room)
        {
            if (_boundRoom == room) return;
            UnbindRoomEvents();
            _boundRoom = room;
            if (_boundRoom != null) _boundRoom.MaxPlayerChangeRejected += HandleMaxPlayerRejected;
        }

        private void UnbindRoomEvents()
        {
            if (_boundRoom != null) _boundRoom.MaxPlayerChangeRejected -= HandleMaxPlayerRejected;
            _boundRoom = null;
        }

        private void HandleMaxPlayerRejected(string reason)
        {
            ShowFeedback(reason);
        }

        // 안내 문구는 일정 시간 뒤 자동으로 사라진다
        private float _feedbackClearTime = -1f;

        private void ShowFeedback(string message)
        {
            if (startFailText == null) return;
            startFailText.text = message;
            _feedbackClearTime = string.IsNullOrEmpty(message) ? -1f : Time.unscaledTime + feedbackDuration;
        }

        private void TickFeedback()
        {
            if (_feedbackClearTime < 0f || Time.unscaledTime < _feedbackClearTime) return;
            _feedbackClearTime = -1f;
            if (startFailText != null) startFailText.text = string.Empty;
        }

        private void OnCopyInviteCodeClicked()
        {
            var room = RoomManager.Instance;
            if (room == null) return;
            GUIUtility.systemCopyBuffer = room.InviteCode.ToString().ToUpperInvariant();
            ShowFeedback("초대 코드가 복사되었습니다");
        }

        private void OnMaxPlayerDecrease() => ChangeMaxPlayer(-1);
        private void OnMaxPlayerIncrease() => ChangeMaxPlayer(+1);

        private void ChangeMaxPlayer(int delta)
        {
            var room = RoomManager.Instance;
            if (room == null) return;
            // 최종 검증(방장/상태/범위/현재 인원)은 서버가 다시 한다
            room.RPC_RequestChangeMaxPlayer(room.MaxPlayerCount + delta);
        }

        private void Update()
        {
            TickFeedback();

            if (_gameStartManager == null)
            {
                _gameStartManager = FindFirstObjectByType<LobbyGameStartManager>();
                if (_gameStartManager != null)
                    _gameStartManager.StartFailed += HandleStartFailed;
            }
            if (!SessionDisconnectUIComponent.IsOpen && !GameAudio.blocksPlayerInput && Input.GetKeyDown(KeyCode.Escape))
            {
                SetOpen(!_isOpen);
            }

            RefreshCursorState();

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

            RefreshCursorState();
        }

        private void RefreshCursorState()
        {
            bool showCursor = BlocksPlayerInput;
            CursorLockMode lockMode = showCursor ? CursorLockMode.None : CursorLockMode.Locked;
            if (Cursor.lockState != lockMode)
            {
                Cursor.lockState = lockMode;
                if (!showCursor && EventSystem.current != null)
                    EventSystem.current.SetSelectedGameObject(null);
            }
            Cursor.visible = showCursor;
        }

        // ================== 방 정보 ==================

        private void RefreshRoomInfo()
        {
            var room = RoomManager.Instance;
            BindRoomEvents(room);
            if (roomNameText != null) roomNameText.text = room.RoomName.ToString();
            if (inviteCodeText != null) inviteCodeText.text = room.InviteCode.ToString().ToUpperInvariant();
            if (maxPlayerText != null) maxPlayerText.text = room.MaxPlayerCount.ToString();

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

            // 인원 조절 화살표는 방장 + 대기 상태에서만 노출
            bool canEditMax = isHost && RoomManager.Instance.CurrentRoomState == RoomManager.RoomState.Waiting;
            if (maxPlayerDecreaseButton != null) maxPlayerDecreaseButton.gameObject.SetActive(canEditMax);
            if (maxPlayerIncreaseButton != null) maxPlayerIncreaseButton.gameObject.SetActive(canEditMax);

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

            ShowFeedback(reason switch
            {
                LobbyGameStartManager.StartFailReason.NotHost => "방장만 시작할 수 있습니다",
                LobbyGameStartManager.StartFailReason.NotEnoughPlayers => "최소 인원이 부족합니다",
                LobbyGameStartManager.StartFailReason.NotAllReady => "모든 플레이어가 준비되지 않았습니다",
                LobbyGameStartManager.StartFailReason.AlreadyStarting => "이미 시작 절차가 진행 중입니다",
                _ => "게임을 시작할 수 없습니다"
            });
        }
    }
}