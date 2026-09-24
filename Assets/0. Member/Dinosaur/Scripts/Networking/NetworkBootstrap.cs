using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Fusion;
using Fusion.Sockets;
using LockdownProtocol.Lobby;
using Photon.Voice.Fusion;
using Photon.Voice.Unity;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 게임의 네트워크 진입점(Entry Point).
/// NetworkRunner의 세션 시작·종료와 네트워크 씬 전환을 담당한다.
///
/// LobbyMenuUI, RoomManager, LobbyPlayerSpawner가 이 클래스를 전제로 작성되어 있다 -
/// CreateRoom()/JoinRoom()/LeaveRoom() 및 static OnPlayerJoinedEvent/OnPlayerLeftEvent.
///
/// NetworkRunner는 이 오브젝트가 아니라 매번 새로 만드는 별도의 자식 오브젝트에 둔다.
/// Fusion은 StartGame() 실패 시 NetworkRunner가 붙은 GameObject를 파괴하므로,
/// 이 오브젝트 자신과 분리해둬야 실패 후에도 재시도가 가능하다.
/// </summary>
public class NetworkBootstrap : MonoBehaviour, INetworkRunnerCallbacks
{
    [Header("Scene Settings")]
    [Tooltip("방 생성/참가 성공 시 이동할 씬 이름 (RoomManager가 존재하는 씬)")]
    [SerializeField] private string roomSceneName = "StandBy";

    [Tooltip("LeaveRoom() 호출 시 돌아갈 씬 이름")]
    [SerializeField] private string lobbySceneName = "Lobby";

    [SerializeField] private NetworkObject roomManagerPrefab;
    [SerializeField] private SessionDisconnectUIComponent disconnectUIPrefab; //연결 종료 안내창

    [Header("Voice")]
    [Tooltip("로비 씬에 미리 배치된 Recorder(내 마이크)")]
    [SerializeField] private Recorder primaryRecorder;

    private NetworkRunner _runner;
    private GameObject _runnerObject;
    private bool _sessionBusy;
    private bool _leaving;
    private string _roomName;
    private int _maxPlayers;
    private bool _isPrivate;
    private bool sessionConnected; //실제 방 입장 여부
    private bool disconnected; //연결 종료 안내 중복 방지
    private bool returningToRoom; //대기실 복귀 중복 방지
    private SessionDisconnectUIComponent disconnectUI; //현재 연결 종료 안내창

    public static event Action<NetworkRunner, PlayerRef> OnPlayerJoinedEvent;
    public static event Action<NetworkRunner, PlayerRef> OnPlayerLeftEvent;
    public static event Action<NetworkRunner> OnSceneLoadDoneEvent;

    private void Awake()
    {
        DontDestroyOnLoad(gameObject);
        if (disconnectUIPrefab != null)
        {
            disconnectUI = Instantiate(disconnectUIPrefab, transform);
            disconnectUI.initialize(this);
        }
    }

    /// <summary>방 생성 (Host). 성공 시 RoomManager를 초기화한다.</summary>
    public async Task<StartGameResult> CreateRoom(string roomName, int maxPlayers, bool isPrivate)
    {
        return await StartSession(GameMode.Host, roomName, maxPlayers, isPrivate);
    }

    /// <summary>기존 방에 참가 (Client).</summary>
    public async Task<StartGameResult> JoinRoom(string roomName)
    {
        return await StartSession(GameMode.Client, roomName, 0);
    }

    /// <summary>세션 종료 후 로비 씬으로 복귀.</summary>
    public async void LeaveRoom()
    {
        if (_leaving) return;
        _leaving = true;
        try
        {
            GetSceneRefByName(lobbySceneName);
            if (_runner != null)
                await _runner.Shutdown();

            LockdownProtocol.Lobby.Invite.GlobalLobbyInviteTransport.Instance?.UpdateLocalRoomStatus(false, null, false);
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            SceneManager.LoadScene(lobbySceneName);
            Destroy(gameObject);
        }
        catch (Exception exception)
        {
            _leaving = false;
            Debug.LogException(exception);
        }
    }

    private async Task<StartGameResult> StartSession(GameMode mode, string roomName, int maxPlayers, bool isPrivate = false)
    {
        if (_sessionBusy || (_runner != null && _runner.IsRunning))
            throw new InvalidOperationException("이미 방에 연결 중이거나 참가 중입니다.");
        if (mode == GameMode.Host && roomManagerPrefab == null)
            throw new InvalidOperationException("RoomManager Prefab이 연결되지 않았습니다.");
        SceneRef roomScene = GetSceneRefByName(roomSceneName);
        GetSceneRefByName(lobbySceneName);

        _sessionBusy = true;
        _roomName = roomName;
        _maxPlayers = Mathf.Clamp(maxPlayers, 2, 10);
        _isPrivate = isPrivate;
        try
        {
            sessionConnected = false;
            disconnected = false;
            PrepareRunner();

            var args = new StartGameArgs
            {
                GameMode = mode,
                SessionName = roomName,
                Scene = roomScene,
                SceneManager = _runnerObject.GetComponent<NetworkSceneManagerDefault>(),
                EnableClientSessionCreation = false
            };

            if (mode == GameMode.Host && maxPlayers > 0)
            {
                args.PlayerCount = _maxPlayers;
            }

            StartGameResult result = await _runner.StartGame(args);
            sessionConnected = result.Ok && !disconnected;
            return result;
        }
        finally
        {
            _sessionBusy = false;
        }
    }

    /// <summary>NetworkRunner와 연동 컴포넌트를 담을 새 자식 오브젝트를 매 시도마다 새로 만든다.</summary>
    private void PrepareRunner()
    {
        if (_runnerObject != null)
        {
            Destroy(_runnerObject);
        }

        _runnerObject = new GameObject("NetworkRunner (Dynamic)");
        _runnerObject.transform.SetParent(transform);

        _runner = _runnerObject.AddComponent<NetworkRunner>();
        _runnerObject.AddComponent<NetworkSceneManagerDefault>();

        var voiceClient = _runnerObject.AddComponent<FusionVoiceClient>();
        voiceClient.PrimaryRecorder = primaryRecorder;
        // UsePrimaryRecorder는 읽기 전용이라 코드로 못 바꾼다. AddComponent로 새로 만들면
        // bool 기본값이 false라 원하는 상태(꺼짐) 그대로다 - VoiceNetworkObject가 스폰 시점에 바인딩한다.

        _runner.ProvideInput = true;

        // NetworkRunner가 이 오브젝트와 다른 GameObject에 있어 자동 콜백 탐색 대상이 아니므로 수동 등록한다.
        _runner.AddCallbacks(this);

        // PlayerInputProvider(실제 입력을 채워 넣는 스크립트)도 같은 이유로 수동 등록이 필요하다.
        // 씬 어딘가에 배치되어 있다고 가정하고 찾는다.
        var inputProvider = FindObjectOfType<PlayerInputProvider>();
        if (inputProvider != null)
        {
            _runner.AddCallbacks(inputProvider);
        }
        else
        {
            Debug.LogWarning("[NetworkBootstrap] PlayerInputProvider를 씬에서 찾을 수 없습니다. 입력이 전달되지 않습니다.");
        }
    }

    /// <summary>빌드 인덱스 대신 이름으로 씬을 찾는다 (인덱스는 자주 바뀌어 불안정하다).</summary>
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

        throw new InvalidOperationException($"씬 '{sceneName}'을 Build Settings에서 찾을 수 없습니다.");
    }

    internal async Task<bool> returnToStandBy() //종료된 게임의 참가자들을 방장 권한으로 대기실에 복귀
    {
        if (_runner == null || !_runner.IsRunning || !_runner.IsServer ||
            _leaving || disconnected || returningToRoom)
            return false;

        GameEndSystem gameEnd = FindFirstObjectByType<GameEndSystem>();
        if (gameEnd == null || gameEnd.Object == null || !gameEnd.Object.IsValid ||
            gameEnd.Runner != _runner || !gameEnd.IsGameEnded)
            return false;

        returningToRoom = true;
        try
        {
            SceneRef roomScene = GetSceneRefByName(roomSceneName);
            RoomManager.Instance?.SetRoomState(RoomManager.RoomState.Ending);
            foreach (PlayerRef player in _runner.ActivePlayers)
            {
                NetworkObject playerObject = _runner.GetPlayerObject(player);
                if (playerObject != null && playerObject.IsValid)
                    _runner.Despawn(playerObject);
            }

            await _runner.LoadScene(roomScene, LoadSceneMode.Single);
            return !disconnected;
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            showDisconnect("대기실로 이동할 수 없습니다.\n로비로 돌아가 다시 참가해 주세요.");
            return false;
        }
        finally
        {
            returningToRoom = false;
        }
    }

    private void showDisconnect(string message) //연결 종료 상태와 로비 복귀 안내를 갱신
    {
        if (_leaving || disconnected)
            return;

        disconnected = true;
        sessionConnected = false;
        LockdownProtocol.Lobby.Invite.GlobalLobbyInviteTransport.Instance?.UpdateLocalRoomStatus(false, null, false);
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        if (disconnectUI != null)
            disconnectUI.show(message);
        else
        {
            Debug.LogError("[NetworkBootstrap] 연결 종료 안내 프리팹이 연결되지 않았습니다.");
            LeaveRoom();
        }
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

    public void OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason)
    {
        if (runner == _runner && sessionConnected)
            showDisconnect("방장과의 연결이 종료되었습니다.\n로비로 돌아가 다시 참가해 주세요.");
    }
    public void OnConnectedToServer(NetworkRunner runner) { }
    public void OnDisconnectedFromServer(NetworkRunner runner, NetDisconnectReason reason)
    {
        if (runner == _runner && sessionConnected)
            showDisconnect("방장과의 연결이 종료되었습니다.\n로비로 돌아가 다시 참가해 주세요.");
    }
    public void OnConnectFailed(NetworkRunner runner, NetAddress remoteAddress, NetConnectFailedReason reason) { }
    public void OnConnectRequest(NetworkRunner runner, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token) { }
    public void OnInput(NetworkRunner runner, NetworkInput input) { }
    public void OnInputMissing(NetworkRunner runner, PlayerRef player, NetworkInput input) { }
    public void OnSimulationMessage(NetworkRunner runner, SimulationMessagePtr message) { }
    public void OnSessionListUpdated(NetworkRunner runner, List<SessionInfo> sessionList) { }
    public void OnCustomAuthenticationResponse(NetworkRunner runner, Dictionary<string, object> data) { }
    public void OnHostMigration(NetworkRunner runner, HostMigrationToken hostMigrationToken)
    {
        if (runner == _runner && sessionConnected)
            showDisconnect("방장이 퇴장하여 방이 종료되었습니다.\n로비로 돌아가 다시 참가해 주세요.");
    }
    public void OnReliableDataReceived(NetworkRunner runner, PlayerRef player, ReliableKey key, ArraySegment<byte> data) { }
    public void OnReliableDataProgress(NetworkRunner runner, PlayerRef player, ReliableKey key, float progress) { }
    public void OnSceneLoadDone(NetworkRunner runner)
    {
        if (runner != _runner || disconnected || _leaving)
            return;
        sessionConnected = true;
        if (runner.IsServer && SceneManager.GetSceneByName(roomSceneName).isLoaded)
        {
            var room = RoomManager.Instance;
            if (room == null || room.Runner != runner)
            {
                if (roomManagerPrefab == null)
                {
                    Debug.LogError("RoomManager Prefab이 연결되지 않았습니다.");
                    return;
                }
                room = runner.Spawn(roomManagerPrefab).GetComponent<RoomManager>();
            }
            room.InitializeRoom(_roomName, _maxPlayers, _isPrivate, runner.LocalPlayer);
        }
        OnSceneLoadDoneEvent?.Invoke(runner);
    }
    public void OnSceneLoadStart(NetworkRunner runner) { }
    public void OnObjectExitAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
    public void OnObjectEnterAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
    public void OnUserSimulationMessage(NetworkRunner runner, SimulationMessagePtr message) { }

    #endregion
}
