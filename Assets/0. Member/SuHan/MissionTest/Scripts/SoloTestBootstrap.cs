using System;
using System.Collections.Generic;
using Fusion;
using Fusion.Sockets;
using TrustNoOne.Missions;
using UnityEngine;

/// <summary>
/// [테스트 전용] SuhanTest 씬에서 혼자 미션 시스템을 켜보기 위한 최소 부트스트랩.
///
/// [왜 필요한가] Fusion의 NetworkBehaviour(Spawned/FixedUpdateNetwork 등)는 NetworkRunner가
///  그 오브젝트를 실제로 "스폰"해야만 콜백이 호출된다. 원래 게임 흐름은 Lobby 씬의
///  NetworkBootstrap이 러너를 만들어 DontDestroyOnLoad로 들고 다니다가 GamePlay 씬에서
///  GameplayPlayerSpawner가 플레이어를 스폰하는데, SuhanTest 씬을 단독으로 열면 이 체인이
///  전혀 시작되지 않는다 (씬에 미리 놓인 프리팹 인스턴스는 Spawned()가 절대 호출되지 않아
///  플레이어가 움직이지 않았던 것이 이 문제였다). 이 스크립트가 체인을 최소한으로 대신한다.
///
/// [OnPlayerJoined 에서 스폰하는 이유] 처음엔 StartGame() 직후 바로 스폰했는데,
///  Runner.SetPlayerObject()가 내부적으로 시뮬레이션 메시지(SimulationMessageInternal_SetPlayerObject)로
///  처리되는 API라, Fusion 콜백 밖(async 이어붙이기)에서 호출하면 등록이 안 됐다
///  (TryGetPlayerObject가 계속 실패 → 미션 상호작용의 gate 체크가 항상 거부됨).
///  실제 게임 코드(GameplayPlayerSpawner)처럼 Fusion이 직접 호출하는 콜백 안에서 해야 하므로,
///  이 스크립트도 INetworkRunnerCallbacks.OnPlayerJoined 안에서 스폰한다.
///
/// [GameMode.Single] 오프라인 1인 테스트 전용. Photon 클라우드 연결이 필요 없고,
///  로컬 플레이어가 스폰한 모든 오브젝트의 StateAuthority/InputAuthority를 갖는다.
///
/// [RoleAssignment를 쓰지 않는 이유] RoleAssignment는 최소 2명이 있어야 역할을 배정한다
///  (MinPlayerCount = 2). 혼자 테스트해야 하므로, 스폰이 끝나면 이 스크립트가 직접
///  MissionManager.InitializeMissions(나 혼자를 시민으로, 살인마 없음)을 호출한다.
///  RoleAssignment/실제 게임 로직은 건드리지 않는다 — 이 스크립트는 SuHan 테스트 씬 전용이다.
/// </summary>
public class SoloTestBootstrap : MonoBehaviour, INetworkRunnerCallbacks
{
    [Header("플레이어")]
    [SerializeField] private NetworkObject playerPrefab;
    [SerializeField] private Transform playerSpawnPoint;

    [Header("미션 시스템")]
    [SerializeField] private NetworkObject missionManagerPrefab;

    [Serializable]
    private struct SpawnEntry
    {
        public NetworkObject prefab;
        public Transform point;
    }

    [Header("테스트용 네트워크 오브젝트 (미션 오브젝트 · 행동 큐브)")]
    [Tooltip("NetworkObject 가 붙은 것은 씬에 놓아두면 Spawned()가 호출되지 않으므로 전부 여기서 Runner.Spawn 한다.")]
    [SerializeField] private SpawnEntry[] networkedSpawns;

    private NetworkRunner runner;
    private MissionManager missionManager;
    private bool missionsInitialized;
    private bool spawned;

    private async void Start()
    {
        GameObject runnerObject = new GameObject("NetworkRunner (Solo Test)");
        runner = runnerObject.AddComponent<NetworkRunner>();
        runnerObject.AddComponent<NetworkSceneManagerDefault>();
        runner.ProvideInput = true;
        runner.AddCallbacks(this);

        // 씬 어딘가에 있을 필요 없이, 테스트에서만 쓸 것이므로 이 오브젝트에 직접 붙여서 자급자족한다.
        PlayerInputProvider inputProvider = gameObject.AddComponent<PlayerInputProvider>();
        runner.AddCallbacks(inputProvider);

        StartGameArgs args = new StartGameArgs
        {
            GameMode = GameMode.Single,
            SessionName = "SuHanSoloTest",
        };

        StartGameResult result = await runner.StartGame(args);

        if (!result.Ok)
            Debug.LogError($"[SoloTestBootstrap] 러너 시작 실패: {result.ShutdownReason} {result.ErrorMessage}");
    }

    /// <summary>Fusion이 직접 호출하는 콜백. GameMode.Single에서는 로컬 플레이어에 대해 한 번 호출된다.</summary>
    public void OnPlayerJoined(NetworkRunner runner, PlayerRef player)
    {
        if (spawned || player != runner.LocalPlayer)
            return;

        spawned = true;
        SpawnAll(runner, player);
    }

    private void SpawnAll(NetworkRunner runner, PlayerRef localPlayer)
    {
        if (playerPrefab == null || playerSpawnPoint == null)
        {
            Debug.LogError("[SoloTestBootstrap] Player Prefab / Player Spawn Point가 비어 있습니다.");
            return;
        }

        NetworkObject playerObject = runner.Spawn(
            playerPrefab, playerSpawnPoint.position, playerSpawnPoint.rotation, localPlayer);

        runner.SetPlayerObject(localPlayer, playerObject);

        if (missionManagerPrefab != null)
        {
            NetworkObject managerObject = runner.Spawn(missionManagerPrefab, Vector3.zero, Quaternion.identity);
            missionManager = managerObject.GetComponent<MissionManager>();
        }
        else
        {
            Debug.LogWarning("[SoloTestBootstrap] MissionManager Prefab이 비어 있어 미션 초기화를 건너뜁니다.");
        }

        if (networkedSpawns == null)
            return;

        foreach (SpawnEntry entry in networkedSpawns)
        {
            if (entry.prefab == null || entry.point == null)
            {
                Debug.LogWarning("[SoloTestBootstrap] 비어 있는 스폰 항목을 건너뜁니다.");
                continue;
            }

            // 하나가 실패해도 나머지는 계속 스폰한다 (전에는 첫 실패에서 반복문 전체가 멈춰 아무것도 안 보였다).
            try
            {
                runner.Spawn(entry.prefab, entry.point.position, entry.point.rotation);
            }
            catch (Exception e)
            {
                Debug.LogError($"[SoloTestBootstrap] {entry.prefab.name} 스폰 실패 — 나머지는 계속 스폰합니다. " +
                               $"새 프리팹이 Fusion 목록에 없다면 메뉴 Tools > Fusion > Rebuild Prefab Table 을 실행하세요.\n{e.Message}");
            }
        }
    }

    private void Update()
    {
        // MissionManager.Spawned() 가 호스트 판정 로직을 준비할 때까지 기다렸다가, 준비되면 딱 한 번만 초기화한다.
        if (missionsInitialized || missionManager == null || !missionManager.IsReady)
            return;

        missionManager.InitializeMissions(
            new List<PlayerRef> { runner.LocalPlayer },
            new List<PlayerRef>());

        missionsInitialized = true;

        Debug.Log("[SoloTestBootstrap] 솔로 미션 테스트 초기화 완료 (나 = 시민 1명, 살인마 없음)");
    }

    // 아래는 INetworkRunnerCallbacks 구현용 (미사용)
    public void OnPlayerLeft(NetworkRunner runner, PlayerRef player) { }
    public void OnInput(NetworkRunner runner, NetworkInput input) { }
    public void OnInputMissing(NetworkRunner runner, PlayerRef player, NetworkInput input) { }
    public void OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason) { }
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
