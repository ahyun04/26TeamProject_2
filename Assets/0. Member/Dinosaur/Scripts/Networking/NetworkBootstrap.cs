using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Fusion;
using Fusion.Sockets;
using LockdownProtocol.Lobby;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 게임의 네트워크 진입점(Entry Point).
///
/// LobbyMenuUI, RoomManager, LobbyPlayerSpawner가 이미 이 클래스를 전제로 작성되어 있다 -
/// CreateRoom()/JoinRoom()/LeaveRoom() 및 static OnPlayerJoinedEvent/OnPlayerLeftEvent.
///
/// 참고: LobbyNetworkManager.cs와 PlayerSpawnManager.cs는 이 클래스가 도입되기 전 버전으로
/// 보인다 (프로젝트 내 어떤 파일도 그 둘을 참조하지 않음). 팀 확인 후 정리 대상.
/// </summary>
[RequireComponent(typeof(NetworkRunner))]
public class NetworkBootstrap : MonoBehaviour, INetworkRunnerCallbacks
{
    [Header("Scene Settings")]
    [Tooltip("방 생성/참가 성공 시 이동할 씬 이름 (RoomManager가 존재하는 씬)")]
    [SerializeField] private string roomSceneName = "Room";

    [Tooltip("LeaveRoom() 호출 시 돌아갈 씬 이름")]
    [SerializeField] private string lobbySceneName = "Lobby";

    private NetworkRunner _runner;
    private NetworkSceneManagerDefault _sceneManager;

    /// <summary>
    /// RoomManager, LobbyPlayerSpawner 등이 구독하는 접속/퇴장 이벤트.
    /// INetworkRunnerCallbacks를 직접 구현한 컴포넌트는 NetworkRunner와 같은 GameObject에 있어야만
    /// 실제로 콜백을 받는데, 다른 씬의 NetworkBehaviour(RoomManager 등)는 그 조건을 만족 못 하므로
    /// 이 static 이벤트로 대신 중계한다.
    /// </summary>
    public static event Action<NetworkRunner, PlayerRef> OnPlayerJoinedEvent;
    public static event Action<NetworkRunner, PlayerRef> OnPlayerLeftEvent;

    private void Awake()
    {
        // 씬 전환(로비 -> 방) 후에도 NetworkRunner가 살아있어야 Photon 연결이 유지된다.
        DontDestroyOnLoad(gameObject);

        _runner = GetComponent<NetworkRunner>();
        _sceneManager = GetComponent<NetworkSceneManagerDefault>();
        if (_sceneManager == null)
        {
            _sceneManager = gameObject.AddComponent<NetworkSceneManagerDefault>();
        }

        // 입력 자체는 PlayerInputProvider가 이미 채워서 보내므로 여기서는 별도 처리하지 않는다.
        _runner.ProvideInput = true;
    }

    /// <summary>방 생성 (Host). 성공 시 RoomManager를 초기화한다.</summary>
    public async Task<StartGameResult> CreateRoom(string roomName, int maxPlayers, bool isPrivate)
    {
        StartGameResult result = await StartSession(GameMode.Host, roomName, maxPlayers);

        if (result.Ok)
        {
            // RoomManager는 씬 로드가 끝난 뒤 스폰되는 NetworkObject다. StartGame()은 씬 로드 완료까지
            // 기다린 뒤 반환하므로, 이 시점엔 이미 스폰이 끝나 있어야 한다 (실제 테스트로 재확인 필요).
            RoomManager.Instance?.InitializeRoom(roomName, maxPlayers, isPrivate, _runner.LocalPlayer);
        }

        return result;
    }

    /// <summary>기존 방에 참가 (Client).</summary>
    public async Task<StartGameResult> JoinRoom(string roomName)
    {
        return await StartSession(GameMode.Client, roomName, 0);
    }

    /// <summary>세션 종료 후 로비 씬으로 복귀.</summary>
    public async void LeaveRoom()
    {
        if (_runner != null)
        {
            await _runner.Shutdown();
        }

        SceneManager.LoadScene(lobbySceneName);
    }

    private async Task<StartGameResult> StartSession(GameMode mode, string roomName, int maxPlayers)
    {
        var args = new StartGameArgs
        {
            GameMode = mode,
            SessionName = roomName,
            Scene = GetSceneRefByName(roomSceneName),
            SceneManager = _sceneManager,
            // Client가 존재하지 않는 방에 접속 시도할 때 새 방을 만들어버리지 않게 막는다.
            EnableClientSessionCreation = false
        };

        if (mode == GameMode.Host && maxPlayers > 0)
        {
            args.PlayerCount = maxPlayers;
        }

        return await _runner.StartGame(args);
    }

    /// <summary>
    /// 씬 빌드 인덱스 대신 이름으로 찾는다 - AutoSceneLoader.cs와 같은 이유로,
    /// 개발 중 인덱스가 자주 바뀌는 것보다 이름 참조가 안전하다.
    /// </summary>
    private static SceneRef GetSceneRefByName(string sceneName)
    {
        for (int i = 0; i < SceneManager.sceneCountInBuildSettings; i++)
        {
            string path = SceneUtility.GetScenePathByBuildIndex(i);
            string name = Path.GetFileNameWithoutExtension(path);
            if (name == sceneName)
            {
                return SceneRef.FromIndex(i);
            }
        }

        Debug.LogError($"[NetworkBootstrap] 씬 '{sceneName}'을 Build Settings에서 찾을 수 없습니다.");
        return default;
    }

    #region INetworkRunnerCallbacks

    public void OnPlayerJoined(NetworkRunner runner, PlayerRef player)
    {
        OnPlayerJoinedEvent?.Invoke(runner, player);
    }

    public void OnPlayerLeft(NetworkRunner runner, PlayerRef player)
    {
        OnPlayerLeftEvent?.Invoke(runner, player);
    }

    public void OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason) { }
    public void OnConnectedToServer(NetworkRunner runner) { }
    public void OnDisconnectedFromServer(NetworkRunner runner, NetDisconnectReason reason) { }
    public void OnConnectFailed(NetworkRunner runner, NetAddress remoteAddress, NetConnectFailedReason reason) { }
    public void OnConnectRequest(NetworkRunner runner, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token) { }
    public void OnInput(NetworkRunner runner, NetworkInput input) { } // PlayerInputProvider가 이미 처리
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
}