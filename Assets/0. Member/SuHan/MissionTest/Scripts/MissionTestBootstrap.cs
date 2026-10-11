using System;
using System.Collections.Generic;
using Fusion;
using Fusion.Sockets;
using TrustNoOne.Missions;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// [테스트 전용] SuhanTest 씬에서 미션 시스템을 혼자 또는 여러 명(호스트 / 손님)으로 켜 보는 시작 스크립트.
///
/// [역할]
///  - 시작 화면: Play(빌드 실행) 직후 [혼자] [호스트] [손님] 중 고른 방식으로 러너를 시작한다. 실패하면 이유를 보이고 다시 고른다 (6a 명세 MT6)
///  - 플레이어 스폰: 호스트(혼자 모드 포함)가 들어온 사람마다 플레이어를 스폰하고, 나가면 지운다 (MT9)
///  - 혼자 모드 자동 배정: 미션 관리자가 준비되면 "나 = 시민, 범인 없음"으로 배정한다 (멀티 배정은 MissionTestAssignmentPanel)
///  - 판 다시 시작: 러너를 끝내고 씬을 다시 불러 시작 화면으로 돌아간다. 호스트가 끝내면 손님도 끊겨 같이 돌아간다 (MT5)
///
/// [왜 필요한가] Fusion 의 NetworkBehaviour(Spawned / FixedUpdateNetwork 등)는 러너가 그 오브젝트를 등록해야 콜백이 불린다.
///  원래 게임 흐름은 Lobby 씬의 NetworkBootstrap 이 러너를 만들어 들고 다니다가 GamePlay 씬에서 플레이어를 스폰하는데,
///  SuhanTest 씬을 단독으로 열면 이 흐름이 시작되지 않는다. 이 스크립트가 그 흐름을 최소한으로 대신한다.
///
/// [씬 오브젝트] 미션 장치 · 청소기 · 미션 관리자는 설정 메뉴가 씬에 미리 놓는다 (MT7). 러너가 시작될 때 테스트용 씬 관리자
///  (SuhanTestSceneManager)가 열린 SuhanTest 를 넘겨받아 등록하므로 여기서는 스폰하지 않는다. 플레이어만 스폰한다.
///
/// [OnPlayerJoined 에서 스폰하는 이유] Runner.SetPlayerObject() 는 내부적으로 시뮬레이션 메시지로 처리되는 API 라,
///  Fusion 콜백 밖(async 이어붙이기)에서 호출하면 등록이 안 됐다 (TryGetPlayerObject 가 계속 실패 → 미션 상호작용 거부).
///  실제 게임 코드처럼 Fusion 이 직접 호출하는 콜백 안에서 스폰한다.
///
/// [RoleAssignment 를 쓰지 않는 이유] 실제 역할 배정은 GamePlay 씬 흐름에 묶여 있고 최소 2명이 필요하다.
///  테스트에서는 혼자 모드는 이 스크립트가, 멀티는 배정 패널이 같은 InitializeMissions 를 직접 부른다.
///
/// [음성] 플레이어 프리팹의 음성 컴포넌트는 음성 연결이 없으면 경고만 남긴다 → 음성 연결은 만들지 않는다 (MT12)
/// [근거] 6a 명세 MT5 · MT6 · MT9 · MT10 · MT11 · MT12 · MT14
/// </summary>
public class MissionTestBootstrap : MonoBehaviour, INetworkRunnerCallbacks
{
    // 방 이름 고정 (MT6). 멀티는 에디터와 빌드가 같은 방을 찾아야 하므로 바꾸지 않는다
    public const string SoloSessionName = "SuHanSoloTest";
    public const string MultiSessionName = "SuHanMultiTest";

    [Header("플레이어")]
    [SerializeField] private NetworkObject playerPrefab;
    [SerializeField] private Transform playerSpawnPoint;
    [Tooltip("들어온 사람마다 시작점에서 이만큼(m)씩 오른쪽으로 비켜 세운다 (겹치지 않게)")]
    [SerializeField] private float playerSpacing = 1.5f;

    private enum Phase { Choosing, Connecting, Running }

    private Phase phase = Phase.Choosing;
    private NetworkRunner runner;
    private GameMode mode;
    private string lastError;
    private bool reloadRequested;
    private bool soloInitialized;
    private MissionManager missionManager;
    private PlayerInputProvider inputProvider;
    private GUIStyle titleStyle;
    private GUIStyle textStyle;
    private readonly Dictionary<PlayerRef, NetworkObject> spawnedPlayers = new Dictionary<PlayerRef, NetworkObject>();

    /// <summary>지금 러너 (시작 전 · 실패 뒤에는 null)</summary>
    public NetworkRunner Runner => runner;

    /// <summary>고른 시작 방식 (혼자 / 호스트 / 손님)</summary>
    public GameMode Mode => mode;

    /// <summary>러너가 시작에 성공해 돌고 있는가</summary>
    public bool IsRunning => phase == Phase.Running && runner != null && runner.IsRunning;

    public static string ModeName(GameMode gameMode)
    {
        switch (gameMode)
        {
            case GameMode.Single: return "혼자";
            case GameMode.Host: return "호스트";
            case GameMode.Client: return "손님";
            default: return gameMode.ToString();
        }
    }

    private void Start()
    {
        // 판 다시 시작으로 씬을 다시 불러오면 앞 판 플레이어 카메라가 고정해 둔 커서가 그대로 남아 시작 화면 버튼을 못 누른다
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

#if !UNITY_EDITOR
        // 프로젝트 설정은 전체 화면 시작이다. 에디터 옆에 나란히 놓고 보도록 테스트 빌드에서만 창 모드로 (MT14 — 공유 설정은 안 바꾼다)
        Screen.SetResolution(1280, 720, FullScreenMode.Windowed);
#endif
    }

    private void Update()
    {
        if (reloadRequested)
        {
            reloadRequested = false;
            ReloadScene();
            return;
        }

        if (mode == GameMode.Single && IsRunning && !soloInitialized)
            TryInitializeSolo();
    }

    // ═════════════════════════════════════════════════════════════
    //  시작 · 다시 시작
    // ═════════════════════════════════════════════════════════════

    private async void StartSession(GameMode chosen)
    {
        if (phase != Phase.Choosing)
            return;

        mode = chosen;
        phase = Phase.Connecting;
        lastError = null;

        // Fusion 은 시작에 실패하면 러너 오브젝트를 지운다 → 시도마다 새로 만든다 (MT11, 실제 게임 시작 코드와 같은 방식)
        GameObject runnerObject = new GameObject("NetworkRunner (Mission Test)");
        runner = runnerObject.AddComponent<NetworkRunner>();
        SuhanTestSceneManager sceneManager = runnerObject.AddComponent<SuhanTestSceneManager>();
        runner.ProvideInput = true;
        runner.AddCallbacks(this);

        // 팀원 입력 제공자(실제 입력을 채워 넣는 스크립트): 이 오브젝트에 하나만 붙여 두고 새 러너마다 콜백으로 등록한다
        if (inputProvider == null)
            inputProvider = gameObject.AddComponent<PlayerInputProvider>();

        runner.AddCallbacks(inputProvider);

        StartGameArgs args = new StartGameArgs
        {
            GameMode = chosen,
            SessionName = chosen == GameMode.Single ? SoloSessionName : MultiSessionName,
            // 열린 SuhanTest 를 경로로 알려 준다 → 씬 관리자가 넘겨받아 씬에 놓인 장치 · 미션 관리자를 등록한다 (MT2)
            Scene = sceneManager.GetSceneRef(SceneManager.GetActiveScene().path),
            SceneManager = sceneManager,
            // 손님은 방을 새로 만들지 않는다 — 호스트가 없으면 실패로 끝난다 (MT11)
            EnableClientSessionCreation = false,
        };

        StartGameResult result = await runner.StartGame(args);

        if (result.Ok)
        {
            phase = Phase.Running;
            Debug.Log($"[MissionTestBootstrap] {ModeName(chosen)} 시작 (방 {args.SessionName})");
            return;
        }

        lastError = DescribeFailure(result);
        Debug.LogError($"[MissionTestBootstrap] 러너 시작 실패: {result.ShutdownReason} {result.ErrorMessage}");
        runner = null;
        phase = Phase.Choosing;
    }

    private static string DescribeFailure(StartGameResult result)
    {
        switch (result.ShutdownReason)
        {
            case ShutdownReason.GameNotFound:
                return "방을 찾지 못했습니다 — 다른 창에서 호스트를 먼저 시작하세요";
            case ShutdownReason.GameIdAlreadyExists:
                return "같은 이름의 방이 이미 있습니다 — 다른 창이 이미 호스트입니다. [손님]으로 들어오세요";
            default:
                return $"{result.ShutdownReason} {result.ErrorMessage}";
        }
    }

    /// <summary>
    /// 판 다시 시작 (배정 패널의 F7). 실행 중이면 러너를 끝낸다 → OnShutdown 이 씬을 다시 불러온다.
    /// 호스트가 끝내면 손님은 연결이 끊겨 손님 쪽 OnShutdown 으로 같이 시작 화면에 돌아간다.
    /// </summary>
    public void Restart()
    {
        if (runner != null && runner.IsRunning)
        {
            _ = runner.Shutdown();
            return;
        }

        reloadRequested = true;
    }

    private static void ReloadScene()
    {
#if UNITY_EDITOR
        // 에디터: SuhanTest 는 빌드 목록에 없어 SceneManager.LoadScene 으로 열 수 없다 → 경로로 여는 에디터 전용 함수
        UnityEditor.SceneManagement.EditorSceneManager.LoadSceneInPlayMode(
            SuhanTestSceneManager.ScenePath, new LoadSceneParameters(LoadSceneMode.Single));
#else
        // 빌드: SuhanTest 하나만 넣어 만들었으므로 경로로 바로 연다
        SceneManager.LoadScene(SuhanTestSceneManager.ScenePath);
#endif
    }

    // ═════════════════════════════════════════════════════════════
    //  플레이어 스폰 · 혼자 모드 배정
    // ═════════════════════════════════════════════════════════════

    /// <summary>Fusion 이 직접 호출하는 콜백. 호스트(혼자 모드 포함)에서 들어온 사람마다 한 번 불린다.</summary>
    public void OnPlayerJoined(NetworkRunner runner, PlayerRef player)
    {
        // 플레이어는 호스트만 스폰한다. 손님 화면의 플레이어는 호스트가 만든 것이 복제돼 온다
        if (!runner.IsServer || spawnedPlayers.ContainsKey(player))
            return;

        if (playerPrefab == null || playerSpawnPoint == null)
        {
            Debug.LogError("[MissionTestBootstrap] Player Prefab / Player Spawn Point 가 비어 있습니다. 메뉴 SuHan/Setup Solo Mission Test 를 다시 실행하세요.");
            return;
        }

        // 지금 인원만큼 오른쪽으로 비켜 세운다 (겹치지 않게)
        Vector3 position = playerSpawnPoint.position + playerSpawnPoint.right * (playerSpacing * spawnedPlayers.Count);
        NetworkObject playerObject = runner.Spawn(playerPrefab, position, playerSpawnPoint.rotation, player);

        runner.SetPlayerObject(player, playerObject);
        spawnedPlayers[player] = playerObject;
    }

    /// <summary>나간 사람의 플레이어를 지운다 — 같은 손님이 다시 들어와도 되게 (호스트만)</summary>
    public void OnPlayerLeft(NetworkRunner runner, PlayerRef player)
    {
        if (!runner.IsServer || !spawnedPlayers.TryGetValue(player, out NetworkObject playerObject))
            return;

        spawnedPlayers.Remove(player);

        if (playerObject != null)
            runner.Despawn(playerObject);
    }

    private void TryInitializeSolo()
    {
        if (missionManager == null)
            missionManager = FindFirstObjectByType<MissionManager>();

        // MissionManager.Spawned() 가 호스트 판정 로직을 준비할 때까지 기다렸다가 한 번만 배정한다
        if (missionManager == null || !missionManager.IsReady)
            return;

        missionManager.InitializeMissions(
            new List<PlayerRef> { runner.LocalPlayer },
            new List<PlayerRef>());

        soloInitialized = true;
        Debug.Log("[MissionTestBootstrap] 혼자 모드 미션 배정 완료 (나 = 시민 1명, 범인 없음)");
    }

    public void OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason)
    {
        // 시작 실패 때의 종료는 무시한다 (실패 이유를 시작 화면에 그대로 보여 준다)
        if (phase != Phase.Running)
            return;

        Debug.Log($"[MissionTestBootstrap] 러너 종료 ({shutdownReason}) → 씬을 다시 불러 시작 화면으로");

        // 콜백 안에서 바로 씬을 바꾸지 않고 다음 프레임에 (러너 종료 처리가 끝난 뒤)
        reloadRequested = true;
    }

    // ═════════════════════════════════════════════════════════════
    //  시작 화면 (플레이어가 생기기 전이라 커서가 자유롭다 → 버튼)
    // ═════════════════════════════════════════════════════════════

    private void OnGUI()
    {
        // 실행 중의 상태(방식 · 인원 · 방)는 배정 패널 첫 줄이 보여 준다
        if (phase == Phase.Running)
            return;

        if (titleStyle == null)
        {
            titleStyle = new GUIStyle(GUI.skin.label) { fontSize = 18, richText = true, wordWrap = true };
            titleStyle.normal.textColor = Color.white;
            textStyle = new GUIStyle(GUI.skin.label) { fontSize = 14, richText = true, wordWrap = true };
            textStyle.normal.textColor = Color.white;
        }

        const float width = 440f;
        const float height = 250f;
        Rect box = new Rect((Screen.width - width) * 0.5f, (Screen.height - height) * 0.5f, width, height);

        GUI.Box(box, GUIContent.none);
        GUI.Box(box, GUIContent.none);   // 두 번 그려서 배경을 더 진하게 (디버그 패널과 같은 방식)

        GUILayout.BeginArea(new Rect(box.x + 16f, box.y + 12f, box.width - 32f, box.height - 24f));
        GUILayout.Label("<b>미션 테스트 — 시작 방식</b>", titleStyle);

        if (phase == Phase.Connecting)
        {
            GUILayout.Space(24f);
            GUILayout.Label($"연결 중… ({ModeName(mode)})", textStyle);
        }
        else
        {
            GUILayout.Space(6f);

            if (GUILayout.Button("혼자 (오프라인, 지금까지의 솔로 테스트)", GUILayout.Height(34f)))
                StartSession(GameMode.Single);

            if (GUILayout.Button("호스트 (방 만들기)", GUILayout.Height(34f)))
                StartSession(GameMode.Host);

            if (GUILayout.Button("손님 (방 들어가기)", GUILayout.Height(34f)))
                StartSession(GameMode.Client);

            GUILayout.Label($"멀티 방 이름: {MultiSessionName} · 호스트를 먼저 시작하세요", textStyle);

            if (!string.IsNullOrEmpty(lastError))
                GUILayout.Label($"<color=#ff7070>실패: {lastError}</color>", textStyle);
        }

        GUILayout.EndArea();
    }

    // 아래는 INetworkRunnerCallbacks 구현용 (미사용)
    public void OnInput(NetworkRunner runner, NetworkInput input) { }
    public void OnInputMissing(NetworkRunner runner, PlayerRef player, NetworkInput input) { }
    public void OnConnectedToServer(NetworkRunner runner) { }
    public void OnDisconnectedFromServer(NetworkRunner runner, NetDisconnectReason reason) { }
    public void OnConnectRequest(NetworkRunner runner, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token) { }
    public void OnConnectFailed(NetworkRunner runner, NetAddress remoteAddress, NetConnectFailedReason reason) { }
    public void OnUserSimulationMessage(NetworkRunner runner, SimulationMessagePtr message) { }
    public void OnSessionListUpdated(NetworkRunner runner, List<SessionInfo> sessionList) { }
    public void OnCustomAuthenticationResponse(NetworkRunner runner, Dictionary<string, object> data) { }
    public void OnHostMigration(NetworkRunner runner, HostMigrationToken hostMigrationToken) { }
    public void OnSceneLoadDone(NetworkRunner runner) { }
    public void OnSceneLoadStart(NetworkRunner runner) { }
    public void OnObjectExitAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
    public void OnObjectEnterAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
    public void OnReliableDataReceived(NetworkRunner runner, PlayerRef player, ReliableKey key, ArraySegment<byte> data) { }
    public void OnReliableDataProgress(NetworkRunner runner, PlayerRef player, ReliableKey key, float progress) { }
}
