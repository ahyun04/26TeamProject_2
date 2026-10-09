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
    private bool _isMigrating; //방장 이탈로 호스트 마이그레이션 진행 중 (이 동안은 연결 종료 안내를 띄우지 않음)
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
        // 기획서: 방마다 8자리 영숫자 초대 코드를 생성. Fusion SessionName을 곧 초대 코드로 쓰므로
        // 코드 -> 방 매핑은 Photon이 보장하고, 같은 코드가 이미 있으면(GameIdAlreadyExists) 새 코드로 재시도한다.
        StartGameResult result = default;
        for (int attempt = 0; attempt < 5; attempt++)
        {
            string code = GenerateInviteCode();
            result = await StartSession(GameMode.Host, code, maxPlayers, isPrivate, roomName);
            if (result.Ok || result.ShutdownReason != ShutdownReason.GameIdAlreadyExists)
                break;
        }
        return result;
    }

    /// <summary>초대 코드로 기존 방에 참가 (Client). 코드는 대소문자 구분 없음.</summary>
    public async Task<StartGameResult> JoinRoom(string inviteCode)
    {
        string code = (inviteCode ?? string.Empty).Trim().ToLowerInvariant();
        return await StartSession(GameMode.Client, code, 0);
    }

    private const string InviteCodeChars = "abcdefghijklmnopqrstuvwxyz0123456789";

    private static string GenerateInviteCode()
    {
        var chars = new char[8];
        for (int i = 0; i < chars.Length; i++)
            chars[i] = InviteCodeChars[UnityEngine.Random.Range(0, InviteCodeChars.Length)];
        return new string(chars);
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

    private async Task<StartGameResult> StartSession(GameMode mode, string sessionName, int maxPlayers, bool isPrivate = false, string roomName = null)
    {
        if (_sessionBusy || (_runner != null && _runner.IsRunning))
            throw new InvalidOperationException("이미 방에 연결 중이거나 참가 중입니다.");
        if (mode == GameMode.Host && roomManagerPrefab == null)
            throw new InvalidOperationException("RoomManager Prefab이 연결되지 않았습니다.");
        SceneRef roomScene = GetSceneRefByName(roomSceneName);
        GetSceneRefByName(lobbySceneName);

        _sessionBusy = true;
        _roomName = roomName ?? sessionName;
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
                SessionName = sessionName,
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
        _runner.AddCallbacks(voiceClient); //방 입장·퇴장 알림을 음성 연결에도 전달
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
        Debug.Log($"[NetworkBootstrap] OnShutdown: {shutdownReason}");
        if (_isMigrating || shutdownReason == ShutdownReason.HostMigration)
            return;
        if (runner == _runner && sessionConnected)
            showDisconnect("방장과의 연결이 종료되었습니다.\n로비로 돌아가 다시 참가해 주세요.");
    }
    public void OnConnectedToServer(NetworkRunner runner) { }
    public void OnDisconnectedFromServer(NetworkRunner runner, NetDisconnectReason reason)
    {
        Debug.Log($"[NetworkBootstrap] OnDisconnectedFromServer: {reason}");
        if (_isMigrating)
            return;
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
    /// <summary>
    /// 방장(Fusion Host)이 나가면 남은 사람 중 한 명이 새 Host로 승격되고 모든 피어에서 이 콜백이 불린다.
    /// 기존 Runner를 끄고 마이그레이션 토큰으로 새 Runner(+음성 클라이언트)를 시작해 방을 이어간다.
    /// 마이그레이션 자체가 실패하면 예전처럼 연결 종료 안내를 띄운다.
    /// (NetworkProjectConfig의 "Enable Host Migration"이 켜져 있어야 이 콜백이 불린다)
    /// </summary>
    public async void OnHostMigration(NetworkRunner runner, HostMigrationToken hostMigrationToken)
    {
        Debug.Log($"[NetworkBootstrap] OnHostMigration 호출됨 (새 역할: {hostMigrationToken.GameMode})");
        if (runner != _runner || !sessionConnected || _leaving || disconnected || _isMigrating)
            return;

        _isMigrating = true;
        try
        {
            // 이 오브젝트(NetworkBootstrap)가 같이 파괴되지 않도록 destroyGameObject: false
            await runner.Shutdown(destroyGameObject: false, shutdownReason: ShutdownReason.HostMigration);

            // 옛 세션의 오브젝트는 모두 사라졌으므로 스포너가 들고 있던 참조/입장 순서를 초기화
            FindFirstObjectByType<LobbyPlayerSpawner>()?.ResetForHostMigration();

            PrepareRunner(); // 옛 Runner 오브젝트를 지우고 새 Runner + FusionVoiceClient 생성

            StartGameResult result = await _runner.StartGame(new StartGameArgs
            {
                HostMigrationToken = hostMigrationToken,
                HostMigrationResume = HostMigrationResume,
                SceneManager = _runnerObject.GetComponent<NetworkSceneManagerDefault>()
            });

            if (!result.Ok)
            {
                _isMigrating = false;
                showDisconnect("방장이 퇴장하여 방이 종료되었습니다.\n로비로 돌아가 다시 참가해 주세요.");
            }
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            _isMigrating = false;
            showDisconnect("방장이 퇴장하여 방이 종료되었습니다.\n로비로 돌아가 다시 참가해 주세요.");
        }
        finally
        {
            _isMigrating = false;
        }
    }

    /// <summary>새 Host에서만, 시뮬레이션 재개 직전에 불린다. 방 싱글턴(RoomManager 프리팹)만 복구한다.
    /// 플레이어 오브젝트는 복구하지 않는다 - 새 세션에서 OnPlayerJoined가 다시 불리면
    /// LobbyPlayerSpawner가 새로 스폰한다(옛 PlayerRef/입력 권한을 그대로 믿을 수 없음).</summary>
    private void HostMigrationResume(NetworkRunner runner)
    {
        foreach (var resumeNO in runner.GetResumeSnapshotNetworkObjects())
        {
            if (!resumeNO.TryGetBehaviour<RoomManager>(out _)) continue;

            NetworkObject spawned = runner.Spawn(resumeNO, onBeforeSpawned: (r, newNO) =>
            {
                newNO.CopyStateFrom(resumeNO);
            });

            var room = spawned.GetComponent<RoomManager>();
            if (room != null)
            {
                // 새 Host가 원래 클라이언트였다면 _roomName/_maxPlayers/_isPrivate가 비어 있거나 기본값이다.
                // OnSceneLoadDone에서 InitializeRoom이 다시 불려도 값이 덮어써지지 않도록 복구된 값으로 맞춰둔다.
                _roomName = room.RoomName.ToString();
                _maxPlayers = room.MaxPlayerCount;
                _isPrivate = room.IsPrivate;
                room.RecoverAfterHostMigration(runner.LocalPlayer);
            }

            spawned.GetComponent<LobbyGameStartManager>()?.ResetAfterMigration();
            Debug.Log($"[NetworkBootstrap] 방 정보 복구 완료. 새 방장: {runner.LocalPlayer}");
            break;
        }
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