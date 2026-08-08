using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Fusion;
using Fusion.Sockets;
using UnityEngine;

namespace LockdownProtocol.Networking
{
    /// <summary>
    /// 게임의 네트워크 진입점(Entry Point).
    /// NetworkRunner를 생성하고 Host 또는 Client로 세션을 시작하는 것만 책임진다.
    /// 플레이어 스폰, 게임 로직은 이 클래스가 담당하지 않는다 (SRP).
    /// </summary>
    public class NetworkBootstrap : MonoBehaviour, INetworkRunnerCallbacks
    {
        [Header("Session Settings")]
        [SerializeField] private string defaultRoomName = "LockdownProtocol_TestRoom";

        [Header("Dependencies")]
        [SerializeField] private PlayerInputHandler inputHandler;

        private NetworkRunner _runner;

        /// <summary>
        /// 다른 스크립트(예: PlayerSpawner)가 구독해서 플레이어 스폰 등을 처리할 수 있도록
        /// 접속 이벤트를 외부로 노출한다. 이 클래스는 "무엇을 할지"는 모르고 "일어났다"는 사실만 알린다.
        /// </summary>
        public static event Action<NetworkRunner, PlayerRef> OnPlayerJoinedEvent;
        public static event Action<NetworkRunner, PlayerRef> OnPlayerLeftEvent;

        /// <summary>
        /// 테스트/로비 UI에서 호출할 진입점. Host로 세션을 시작한다.
        /// </summary>
        public async void StartHost()
        {
            await StartSession(GameMode.Host);
        }

        /// <summary>
        /// 테스트/로비 UI에서 호출할 진입점. 기존 Host 세션에 Client로 접속한다.
        /// </summary>
        public async void StartClient()
        {
            await StartSession(GameMode.Client);
        }

        private async Task StartSession(GameMode mode)
        {
            if (_runner != null)
            {
                Debug.LogWarning("[NetworkBootstrap] Runner가 이미 존재합니다. 중복 시작을 무시합니다.");
                return;
            }

            // FusionVoiceClient에 [RequireComponent(typeof(NetworkRunner))]가 있어서,
            // Voice 연동 컴포넌트를 붙이는 순간 에디터가 이미 NetworkRunner를 미리 추가해뒀을 수 있다.
            // 그런 경우 AddComponent()로 또 추가하면 중복 컴포넌트 문제가 생기므로, 먼저 있는지 확인한다.
            _runner = GetComponent<NetworkRunner>();
            if (_runner == null)
            {
                _runner = gameObject.AddComponent<NetworkRunner>();
            }
            _runner.ProvideInput = true; // 이 클라이언트가 입력을 Fusion에 제공하도록 설정 (다음 단계 PlayerInputHandler와 연결됨)

            var sceneManager = gameObject.AddComponent<NetworkSceneManagerDefault>();

            var result = await _runner.StartGame(new StartGameArgs
            {
                GameMode = mode,
                SessionName = defaultRoomName,
                Scene = SceneRef.FromIndex(UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex),
                SceneManager = sceneManager,
                PlayerCount = 10 // 소셜 디덕션 장르 기준 임시 최대 인원, 추후 GameConfig로 분리 예정
            });

            if (result.Ok)
            {
                Debug.Log($"[NetworkBootstrap] 세션 시작 성공. Mode: {mode}");
            }
            else
            {
                Debug.LogError($"[NetworkBootstrap] 세션 시작 실패: {result.ShutdownReason}");
                Destroy(_runner);
                _runner = null;
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

        // ===== 임시 테스트 코드 =====
        // 로비 UI가 만들어지기 전까지만 사용. 로비 시스템 구현 시 이 블록은 제거한다.
        private void OnGUI()
        {
            if (_runner != null) return; // 이미 세션이 시작됐으면 버튼 숨김

            if (GUI.Button(new Rect(10, 10, 150, 40), "Start Host"))
                StartHost();

            if (GUI.Button(new Rect(10, 60, 150, 40), "Start Client"))
                StartClient();
        }
    }
}