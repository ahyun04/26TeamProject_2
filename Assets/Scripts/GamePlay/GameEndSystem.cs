using System;
using System.Collections.Generic;
using Fusion;
using UnityEngine;

public class GameEndSystem : NetworkBehaviour
{
    private const int MaxPlayerCount = 10;

    [SerializeField] private MissionSystem missionSystem;
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
        if (!hasState)
            return;

        UnsubscribeEvents();
        UnsubscribePlayerHealths();
    }

    public PlayerResult GetPlayerResult(PlayerRef player)
    {
        return PlayerResults.TryGet(player, out PlayerResult result)
            ? result
            : PlayerResult.None;
    }

    private void FindDependencies()
    {
        if (missionSystem == null)
            missionSystem = FindFirstObjectByType<MissionSystem>();

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
            if (!IsCitizen(player) || !playerHealths.TryGetValue(player, out PlayerHealth health))
                continue;

            if (health.IsEscaped && TrySetResult(player, PlayerResult.Win))
                escapedCitizenFound = true;

            if (health.IsDead && GetPlayerResult(player) == PlayerResult.None &&
                (missionSystem == null || !missionSystem.IsPersonalActionCompleted(player)))
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

            PlayerResult result = missionSystem.IsPersonalActionCompleted(player)
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

            if (!playerHealths.TryGetValue(player, out PlayerHealth health) ||
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
