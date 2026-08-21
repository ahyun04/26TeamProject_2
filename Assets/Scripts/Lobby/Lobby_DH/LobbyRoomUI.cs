using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace LockdownProtocol.Lobby
{
    /// <summary>
    /// 방 안 상세 UI(ESC로 여는 화면). 기획서 기준으로 "상세 로비 UI" 레이어에 해당 —
    /// 플레이어 목록, Ready, 방장 전용 게임 시작 버튼만 담당한다.
    /// 방 나가기/마이크/방 이름 요약 같은 상시 노출 요소는 LobbyMiniHudUI가 별도로 담당한다.
    ///
    /// 커서는 더 이상 이 스크립트가 건드리지 않는다 — LobbyMovementController가
    /// "우클릭을 눌렀을 때만 카메라 회전" 방식으로 바뀌면서 커서를 잠글 필요가 없어졌다.
    /// ESC는 순수하게 이 패널의 표시 여부만 토글한다.
    ///
    /// 방/플레이어 상태는 RoomManager, LobbyPlayerController를 매 프레임 폴링해서
    /// 최소 동작만 확보했다 — Networked 값의 OnChangedRender로 이벤트 기반으로
    /// 바꾸면 더 정교해지지만, 인원이 많지 않은 로비 특성상 우선 이 정도로 충분하다고 봄 (TODO).
    /// </summary>
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
        [SerializeField] private Button leaveRoomButton; // 기획서 목업에 상세패널 쪽에도 방나가기가 있어서 추가 - 실제 확인창은 LobbyMiniHudUI 걸 재사용

        [Header("Feedback")]
        [SerializeField] private TMP_Text startFailText;

        private readonly List<PlayerListEntryUI> _spawnedEntries = new List<PlayerListEntryUI>();
        private LobbyGameStartManager _gameStartManager;
        private LobbyMiniHudUI _miniHud;
        private bool _isOpen;

        private void OnEnable()
        {
            _gameStartManager = FindFirstObjectByType<LobbyGameStartManager>();
            if (_gameStartManager != null)
            {
                _gameStartManager.StartFailed += HandleStartFailed;
            }

            _miniHud = FindFirstObjectByType<LobbyMiniHudUI>();

            readyButton.onClick.AddListener(OnReadyClicked);
            startGameButton.onClick.AddListener(OnStartGameClicked);
            if (leaveRoomButton != null) leaveRoomButton.onClick.AddListener(OnLeaveClicked);

            if (startFailText != null) startFailText.text = string.Empty;

            // 기획서: "ESC로 상세 로비 UI를 열 수 있다" - 시작은 닫힌 상태.
            SetOpen(false);
        }

        private void OnDisable()
        {
            if (_gameStartManager != null)
            {
                _gameStartManager.StartFailed -= HandleStartFailed;
            }

            readyButton.onClick.RemoveListener(OnReadyClicked);
            startGameButton.onClick.RemoveListener(OnStartGameClicked);
            if (leaveRoomButton != null) leaveRoomButton.onClick.RemoveListener(OnLeaveClicked);
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                SetOpen(!_isOpen);
            }

            if (!_isOpen) return; // 패널 닫혀있으면 목록/버튼 갱신은 건너뛴다 (불필요한 연산 방지)
            if (RoomManager.Instance == null) return;

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

            // 기획서: "현재 인원 X, 준비 완료 Y" 기준으로 게임 시작 버튼 활성화 여부를 클라에서도 미리 판단
            // (최종 판정은 어차피 LobbyGameStartManager가 서버에서 다시 함 - 여긴 UX용 예측 표시일 뿐)
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
            _gameStartManager?.RPC_RequestStartGame();
        }

        private void OnLeaveClicked()
        {
            // 확인창은 LobbyMiniHudUI가 들고 있는 걸 그대로 재사용 (하나만 있으면 됨)
            _miniHud?.ShowLeaveConfirm();
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