using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Fusion;
using Fusion.Sockets;
using LockdownProtocol.Lobby;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LockdownProtocol.Networking
{
    /// <summary>
    /// 게임의 네트워크 진입점(Entry Point).
    /// NetworkRunner를 생성하고 Host 또는 Client로 세션을 시작하는 것만 책임진다.
    /// 플레이어 스폰, 게임 로직은 이 클래스가 담당하지 않는다 (SRP).
    ///
    /// 로비 시스템 연동을 위해 방 이름/최대 인원/공개여부를 받는 CreateRoom/JoinRoom을
    /// 추가하고, 세션이 실제로 씬을 넘어가도록 대기방 씬 인덱스를 명시했다.
    /// 씬 전환에도 살아남아야 하므로 DontDestroyOnLoad를 건다.
    /// </summary>
    public class NetworkBootstrap : MonoBehaviour, INetworkRunnerCallbacks
    {
        [Header("Session Settings")]
        [SerializeField] private string defaultRoomName = "LockdownProtocol_TestRoom";
        [SerializeField] private int waitingRoomSceneIndex = 2; // 대기방(로비 룸) 씬 빌드 인덱스
        [SerializeField] private int lobbyMenuSceneIndex = 1;   // 방 목록/방 만들기 메뉴 씬 빌드 인덱스

        [Header("Lobby")]
        [SerializeField] private NetworkObject roomManagerPrefab; // RoomManager + LobbyGameStartManager가 함께 붙은 프리팹

        [Header("Dependencies")]
        [SerializeField] private PlayerInputHandler inputHandler;

        private NetworkRunner _runner;
        public NetworkRunner Runner => _runner;

        /// <summary>
        /// 다른 스크립트(예: PlayerSpawner, LobbyPlayerSpawner)가 구독해서
        /// 플레이어 스폰 등을 처리할 수 있도록 접속 이벤트를 외부로 노출한다.
        /// 이 클래스는 "무엇을 할지"는 모르고 "일어났다"는 사실만 알린다.
        /// </summary>
        public static event Action<NetworkRunner, PlayerRef> OnPlayerJoinedEvent;
        public static event Action<NetworkRunner, PlayerRef> OnPlayerLeftEvent;

        private void Awake()
        {
            // 로비 -> 대기방 -> 게임 씬 전환에도 NetworkRunner(및 Photon 연결)가 유지되도록.
            DontDestroyOnLoad(gameObject);
        }

        // ================== 방 생성 / 참가 (로비 시스템 진입점) ==================

        /// <summary>방 만들기 -> 로비 UI가 이 메서드를 호출한다.</summary>
        public async Task<StartGameResult> CreateRoom(string roomName, int maxPlayers, bool isPrivate)
        {
            var result = await StartSession(GameMode.Host, roomName, maxPlayers, isPrivate);

            if (result.Ok)
            {
                SpawnRoomManager(roomName, maxPlayers, isPrivate);
            }

            return result;
        }

        /// <summary>방 참가 -> 로비 UI가 이 메서드를 호출한다.</summary>
        public async Task<StartGameResult> JoinRoom(string roomName)
        {
            return await StartSession(GameMode.Client, roomName, maxPlayers: 0, isPrivate: false);
        }

        /// <summary>방 나가기 -> 세션 종료 후 로비 메뉴 씬으로 복귀.</summary>
        public async void LeaveRoom()
        {
            if (_runner == null) return;

            await _runner.Shutdown();
            _runner = null;
            SceneManager.LoadScene(lobbyMenuSceneIndex);
        }

        private async Task<StartGameResult> StartSession(GameMode mode, string roomName, int maxPlayers, bool isPrivate)
        {
            if (_runner != null)
            {
                Debug.LogWarning("[NetworkBootstrap] Runner가 이미 존재합니다. 중복 시작을 무시합니다.");
                return default;
            }

            // FusionVoiceClient에 [RequireComponent(typeof(NetworkRunner))]가 있어서,
            // Voice 연동 컴포넌트를 붙이는 순간 에디터가 이미 NetworkRunner를 미리 추가해뒀을 수 있다.
            // 그런 경우 AddComponent()로 또 추가하면 중복 컴포넌트 문제가 생기므로, 먼저 있는지 확인한다.
            _runner = GetComponent<NetworkRunner>();
            if (_runner == null)
            {
                _runner = gameObject.AddComponent<NetworkRunner>();
            }
            // 로비 세션(LobbyMovementController)은 RPC로 직접 위치를 보내는 방식이라
            // Fusion의 GetInput<T>() 입력 폴링이 필요 없다. InputHandler가 없는 상태에서
            // ProvideInput만 켜두면 매 틱 OnInput이 불려서 경고가 계속 찍히므로,
            // 핸들러가 실제로 할당된 경우(=게임 씬 등 인풋이 필요한 세션)에만 켠다.
            _runner.ProvideInput = inputHandler != null;

            var sceneManager = gameObject.GetComponent<NetworkSceneManagerDefault>();
            if (sceneManager == null)
            {
                sceneManager = gameObject.AddComponent<NetworkSceneManagerDefault>();
            }

            var args = new StartGameArgs
            {
                GameMode = mode,
                SessionName = string.IsNullOrEmpty(roomName) ? defaultRoomName : roomName,
                Scene = SceneRef.FromIndex(waitingRoomSceneIndex),
                SceneManager = sceneManager,
                IsVisible = !isPrivate,
                // 클라이언트가 존재하지 않는 방에 접속하려고 했을 때 새 방을 만들지 못하게 함
                EnableClientSessionCreation = mode == GameMode.Host
            };

            if (mode == GameMode.Host)
            {
                args.PlayerCount = maxPlayers > 0 ? maxPlayers : 10;
            }

            var result = await _runner.StartGame(args);

            if (result.Ok)
            {
                Debug.Log($"[NetworkBootstrap] 세션 시작 성공. Mode: {mode}, Room: {roomName}");
            }
            else
            {
                Debug.LogError($"[NetworkBootstrap] 세션 시작 실패: {result.ShutdownReason}");
                Destroy(_runner);
                _runner = null;
            }

            return result;
        }

        private void SpawnRoomManager(string roomName, int maxPlayers, bool isPrivate)
        {
            if (roomManagerPrefab == null)
            {
                Debug.LogError("[NetworkBootstrap] RoomManager Prefab이 할당되지 않았습니다.");
                return;
            }

            // roomManagerPrefab 하나에 RoomManager와 LobbyGameStartManager를 함께 붙여서
            // 한 번의 Spawn으로 둘 다 살아나게 한다 (방 단위 싱글턴이 두 개로 나뉘어 있을 이유가 없음).
            NetworkObject spawned = _runner.Spawn(roomManagerPrefab, inputAuthority: PlayerRef.None);

            var roomManager = spawned.GetComponent<RoomManager>();
            if (roomManager == null)
            {
                Debug.LogError("[NetworkBootstrap] RoomManager Prefab에 RoomManager 컴포넌트가 없습니다.");
                return;
            }
            roomManager.InitializeRoom(roomName, maxPlayers, isPrivate, _runner.LocalPlayer);

            if (spawned.GetComponent<LobbyGameStartManager>() == null)
            {
                Debug.LogWarning("[NetworkBootstrap] RoomManager Prefab에 LobbyGameStartManager 컴포넌트가 없습니다. " +
                                  "게임 시작 버튼이 동작하지 않습니다 - 프리팹에 컴포넌트를 추가하세요.");
            }
        }

        #region INetworkRunnerCallbacks

        public void OnPlayerJoined(NetworkRunner runner, PlayerRef player)
        {
            Debug.Log($"[NetworkBootstrap] 플레이어 접속: {player}");
            OnPlayerJoinedEvent?.Invoke(runner, player);
        }

        public void OnPlayerLeft(NetworkRunner runner, PlayerRef player)
        {
            Debug.Log($"[NetworkBootstrap] 플레이어 퇴장: {player}");
            OnPlayerLeftEvent?.Invoke(runner, player);
        }

        public void OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason)
        {
            Debug.Log($"[NetworkBootstrap] 세션 종료: {shutdownReason}");
        }

        public void OnConnectedToServer(NetworkRunner runner) { }
        public void OnDisconnectedFromServer(NetworkRunner runner, NetDisconnectReason reason) { }
        public void OnConnectFailed(NetworkRunner runner, NetAddress remoteAddress, NetConnectFailedReason reason) { }
        public void OnConnectRequest(NetworkRunner runner, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token) { }
        public void OnInput(NetworkRunner runner, NetworkInput input)
        {
            if (inputHandler == null)
            {
                Debug.LogWarning("[NetworkBootstrap] PlayerInputHandler가 연결되지 않았습니다. Inspector에서 할당해주세요.");
                return;
            }

            input.Set(inputHandler.GatherInput());
        }
        public void OnInputMissing(NetworkRunner runner, PlayerRef player, NetworkInput input) { }
        public void OnSimulationMessage(NetworkRunner runner, SimulationMessagePtr message) { }
        public void OnSessionListUpdated(NetworkRunner runner, List<SessionInfo> sessionList) { }
        public void OnCustomAuthenticationResponse(NetworkRunner runner, Dictionary<string, object> data) { }
        public void OnHostMigration(NetworkRunner runner, HostMigrationToken hostMigrationToken) { }
        public void OnReliableDataReceived(NetworkRunner runner, PlayerRef player, ReliableKey key, ArraySegment<byte> data) { }
        public void OnReliableDataProgress(NetworkRunner runner, PlayerRef player, ReliableKey key, float progress) { }
        public void OnSceneLoadDone(NetworkRunner runner) { }
        public void OnSceneLoadStart(NetworkRunner runner) { }
        public void OnObjectExitAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
        public void OnObjectEnterAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
        public void OnUserSimulationMessage(NetworkRunner runner, SimulationMessagePtr message) { }

        #endregion

        // ===== 임시 테스트 코드 블록은 제거함 =====
        // 로비 시스템(RoomManager + LobbyUI)이 이제 생겼으므로, 기존 OnGUI Start Host/Client
        // 버튼은 삭제. 테스트가 다시 필요하면 로비 UI를 통해 CreateRoom/JoinRoom을 호출할 것.
    }
}