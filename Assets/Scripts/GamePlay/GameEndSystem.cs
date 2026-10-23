using System;
using System.Collections;
using System.Collections.Generic;
using Fusion;
using UnityEngine;

public class GameEndSystem : NetworkBehaviour
{
    private const int MaxPlayerCount = LockdownProtocol.Lobby.RoomManager.AbsoluteMaxPlayers;

    [SerializeField] private TrustNoOne.Missions.MissionManager missionSystem; // 새 미션 시스템으로 교체(6b)
    [SerializeField] private RoleAssignment roleAssignment;
    [SerializeField] private GameTimer gameTimer;

    [Networked, Capacity(MaxPlayerCount)]
    public NetworkDictionary<PlayerRef, PlayerResult> PlayerResults => default;

    [Networked] public NetworkBool ResultsInitialized { get; private set; }
    [Networked] public NetworkBool HasCitizenEscaped { get; private set; }
    [Networked, OnChangedRender(nameof(HandleGameEndedChanged))]
    public NetworkBool IsGameEnded { get; private set; }

    public event Action<PlayerRef, PlayerResult> OnPlayerResultDecided;
    public event Action OnGameEnded;

    private readonly List<PlayerRef> players = new();
    private readonly Dictionary<PlayerRef, PlayerHealth> playerHealths = new();
    private readonly HashSet<PlayerRef> departedPlayers = new(); //패배 확정 후 퇴장한 플레이어
    private readonly HashSet<PlayerRef> pendingResultPlayers = new(); //씬 전환 전에 결과 보관을 확인할 참가자
    private bool resultRequestsSent; //종료 결과 전달 중복 방지
    private bool returnRequested; //자동 대기실 복귀 중복 방지

    public override void FixedUpdateNetwork() //모든 연결 참가자가 결과를 보관한 뒤 호스트만 자동 복귀
    {
        if (!HasStateAuthority || !IsGameEnded || returnRequested) return;
        if (!resultRequestsSent) preparePlayerResults();
        pendingResultPlayers.RemoveWhere(player => !isConnected(player));
        if (!resultRequestsSent || pendingResultPlayers.Count != 0) return;
        NetworkBootstrap bootstrap = FindFirstObjectByType<NetworkBootstrap>(); //기존 씬 전환 진입점
        if (bootstrap == null) return;
        returnRequested = true;
        bootstrap.returnAfterGameEnd(this);
    }

    private bool isConnected(PlayerRef player) //결과 보관 대기 중 퇴장한 참가자 제외
    {
        foreach (PlayerRef connected in Runner.ActivePlayers)
            if (connected == player) return true;
        return false;
    }

    private void preparePlayerResults() //호스트가 확정한 본인 역할과 결과만 해당 참가자에게 전달
    {
        if (roleAssignment == null || roleAssignment.Object == null || !roleAssignment.Object.IsValid) return;
        foreach (PlayerRef player in Runner.ActivePlayers)
            if (GetPlayerResult(player) != PlayerResult.None && roleAssignment.TryGetRole(player, out _))
                pendingResultPlayers.Add(player);
        resultRequestsSent = true;
        foreach (PlayerRef player in new List<PlayerRef>(pendingResultPlayers))
        {
            roleAssignment.TryGetRole(player, out PlayerRole role); //이미 검증한 참가자 역할
            RPC_prepareGameResult(player, role, GetPlayerResult(player));
        }
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All, TickAligned = false)]
    private void RPC_prepareGameResult([RpcTarget] PlayerRef target, PlayerRole role, PlayerResult result) //씬이 사라지기 전 로컬 결과 보관 요청
    {
        NetworkBootstrap bootstrap = FindFirstObjectByType<NetworkBootstrap>(); //클라이언트의 지속 세션 진입점
        if (bootstrap != null && bootstrap.prepareGameResult(Runner, role, result))
            StartCoroutine(confirmResultNextFrame());
    }

    private IEnumerator confirmResultNextFrame() //수신 RPC 처리에서 벗어난 뒤 결과 보관 응답 전송
    {
        yield return null;
        if (Object != null && Object.IsValid && Runner != null && Runner.IsRunning) RPC_confirmGameResult();
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority, HostMode = RpcHostMode.SourceIsHostPlayer, TickAligned = false)]
    private void RPC_confirmGameResult(RpcInfo info = default) //실제 요청한 참가자의 결과 보관 응답만 승인
    {
        if (!HasStateAuthority || !IsGameEnded || !resultRequestsSent || !isConnected(info.Source)) return;
        pendingResultPlayers.Remove(info.Source);
    }

    public override void Spawned()
    {
        if (!HasStateAuthority)
        {
            if (IsGameEnded)
                OnGameEnded?.Invoke();

            return;
        }

        FindDependencies();
        SubscribeEvents();

        if (roleAssignment != null && roleAssignment.Initialized)
            InitializeResults();
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        UnsubscribeEvents();
        UnsubscribePlayerHealths();
    }

    public PlayerResult GetPlayerResult(PlayerRef player)
    {
        return PlayerResults.TryGet(player, out PlayerResult result)
            ? result
            : PlayerResult.None;
    }

    internal void handlePlayerLeft(PlayerRef player) //플레이어 제거 전에 퇴장 패배와 남은 승리 조건 갱신
    {
        if (!HasStateAuthority || !ResultsInitialized || IsGameEnded ||
            !PlayerResults.ContainsKey(player) || !departedPlayers.Add(player))
            return;

        PlayerResults.Set(player, PlayerResult.Lose);
        OnPlayerResultDecided?.Invoke(player, PlayerResult.Lose);
        if (playerHealths.TryGetValue(player, out PlayerHealth health))
        {
            if (health != null)
            {
                health.Died -= HandlePlayerStateChanged;
                health.Escaped -= HandlePlayerStateChanged;
            }
            playerHealths.Remove(player);
        }

        // 마지막 시민의 퇴장을 먼저 판정하여 미션 제거가 시민 승리로 처리되지 않게 한다.
        CheckAllCitizensRemoved();
        TryEndGame();
        if (missionSystem != null && missionSystem.Object != null && missionSystem.Object.IsValid)
            missionSystem.RemovePlayerMissions(player); // 새 미션 시스템으로 교체(6b)
    }

    private void FindDependencies()
    {
        if (missionSystem == null)
            missionSystem = FindFirstObjectByType<TrustNoOne.Missions.MissionManager>(); // 새 미션 시스템으로 교체(6b)

        if (roleAssignment == null)
            roleAssignment = FindFirstObjectByType<RoleAssignment>();

        if (gameTimer == null)
            gameTimer = FindFirstObjectByType<GameTimer>();
    }

    private void SubscribeEvents()
    {
        if (roleAssignment != null)
            roleAssignment.OnRolesAssigned += InitializeResults;

        if (missionSystem != null)
            missionSystem.OnCitizenMissionsCompleted += HandleCitizenMissionsCompleted;

        if (gameTimer != null)
            gameTimer.OnTimeExpired += HandleTimeExpired;
    }

    private void UnsubscribeEvents()
    {
        if (roleAssignment != null)
            roleAssignment.OnRolesAssigned -= InitializeResults;

        if (missionSystem != null)
            missionSystem.OnCitizenMissionsCompleted -= HandleCitizenMissionsCompleted;

        if (gameTimer != null)
            gameTimer.OnTimeExpired -= HandleTimeExpired;
    }

    private void InitializeResults()
    {
        if (!HasStateAuthority || ResultsInitialized || roleAssignment == null)
            return;

        players.Clear();
        PlayerResults.Clear();

        foreach (PlayerRef player in Runner.ActivePlayers)
        {
            if (!roleAssignment.TryGetRole(player, out _))
                continue;

            players.Add(player);
            PlayerResults.Add(player, PlayerResult.None);
        }

        if (players.Count == 0)
            return;

        ResultsInitialized = true;
        SubscribePlayerHealths();
        HandlePlayerStateChanged();

        if (missionSystem != null && missionSystem.CitizenMissionsCompleted)
            HandleCitizenMissionsCompleted();

        if (gameTimer != null && gameTimer.IsTimeUp)
            HandleTimeExpired();
    }

    private void SubscribePlayerHealths()
    {
        foreach (PlayerRef player in players)
        {
            NetworkObject playerObject = Runner.GetPlayerObject(player);
            if (playerObject == null)
                continue;

            PlayerHealth health = playerObject.GetComponent<PlayerHealth>();
            if (health == null)
                continue;

            playerHealths.Add(player, health);
            health.Died += HandlePlayerStateChanged;
            health.Escaped += HandlePlayerStateChanged;
        }
    }

    private void UnsubscribePlayerHealths()
    {
        foreach (PlayerHealth health in playerHealths.Values)
        {
            if (health == null)
                continue;

            health.Died -= HandlePlayerStateChanged;
            health.Escaped -= HandlePlayerStateChanged;
        }

        playerHealths.Clear();
    }

    private void HandlePlayerStateChanged()
    {
        if (!HasStateAuthority || !ResultsInitialized || IsGameEnded)
            return;

        bool escapedCitizenFound = false;

        foreach (PlayerRef player in players)
        {
            if (!IsCitizen(player) || !playerHealths.TryGetValue(player, out PlayerHealth health) ||
                health == null || health.Object == null || !health.Object.IsValid)
                continue;

            if (health.IsEscaped && TrySetResult(player, PlayerResult.Win))
                escapedCitizenFound = true;

            if (health.IsDead && GetPlayerResult(player) == PlayerResult.None &&
                (missionSystem == null || !missionSystem.IsActionGoalSuccess(player))) // 새 미션 시스템으로 교체(6b)
            {
                TrySetResult(player, PlayerResult.Lose);
            }
        }

        if (escapedCitizenFound)
        {
            HasCitizenEscaped = true;
            SetRoleResults(PlayerRole.Killer, PlayerResult.Lose);
        }

        CheckAllCitizensRemoved();
        TryEndGame();
    }

    private void HandleCitizenMissionsCompleted()
    {
        if (!HasStateAuthority || !ResultsInitialized || IsGameEnded)
            return;

        foreach (PlayerRef player in players)
        {
            if (!IsCitizen(player) || GetPlayerResult(player) != PlayerResult.None)
                continue;

            PlayerResult result = missionSystem.IsActionGoalSuccess(player) // 새 미션 시스템으로 교체(6b)
                ? PlayerResult.Win
                : PlayerResult.Lose;

            TrySetResult(player, result);
        }

        SetRoleResults(PlayerRole.Killer, PlayerResult.Lose);
        TryEndGame();
    }

    private void HandleTimeExpired()
    {
        if (!HasStateAuthority || !ResultsInitialized || IsGameEnded)
            return;

        SetRoleResults(PlayerRole.Citizen, PlayerResult.Lose);
        SetRoleResults(
            PlayerRole.Killer,
            HasCitizenEscaped ? PlayerResult.Lose : PlayerResult.Win);

        TryEndGame();
    }

    private void CheckAllCitizensRemoved()
    {
        if (HasCitizenEscaped)
            return;

        bool hasCitizen = false;

        foreach (PlayerRef player in players)
        {
            if (!IsCitizen(player))
                continue;

            hasCitizen = true;

            if (departedPlayers.Contains(player))
                continue;

            if (!playerHealths.TryGetValue(player, out PlayerHealth health) ||
                health == null || health.Object == null || !health.Object.IsValid ||
                (!health.IsDead && !health.IsEscaped))
            {
                return;
            }
        }

        if (!hasCitizen)
            return;

        SetRoleResults(PlayerRole.Citizen, PlayerResult.Lose);
        SetRoleResults(PlayerRole.Killer, PlayerResult.Win);
    }

    private void SetRoleResults(PlayerRole role, PlayerResult result)
    {
        foreach (PlayerRef player in players)
        {
            if (roleAssignment.TryGetRole(player, out PlayerRole playerRole) && playerRole == role)
                TrySetResult(player, result);
        }
    }

    private bool IsCitizen(PlayerRef player)
    {
        return roleAssignment.TryGetRole(player, out PlayerRole role) && role == PlayerRole.Citizen;
    }

    private bool TrySetResult(PlayerRef player, PlayerResult result)
    {
        if (result == PlayerResult.None ||
            !PlayerResults.TryGet(player, out PlayerResult currentResult) ||
            currentResult != PlayerResult.None)
        {
            return false;
        }

        PlayerResults.Set(player, result);
        OnPlayerResultDecided?.Invoke(player, result);
        return true;
    }

    private void TryEndGame()
    {
        if (IsGameEnded || players.Count == 0)
            return;

        foreach (PlayerRef player in players)
        {
            if (GetPlayerResult(player) == PlayerResult.None)
                return;
        }

        IsGameEnded = true;
        OnGameEnded?.Invoke();
    }

    private void HandleGameEndedChanged()
    {
        if (IsGameEnded && !HasStateAuthority)
            OnGameEnded?.Invoke();
    }
}
