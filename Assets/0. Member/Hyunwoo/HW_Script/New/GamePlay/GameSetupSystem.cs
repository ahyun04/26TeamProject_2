using Fusion;
using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 게임 시작 시 역할을 배정하고 미션 시스템을 초기화합니다.
/// </summary>
[RequireComponent(typeof(MissionSystem))]
public class GameSetupSystem : NetworkBehaviour
{
    private const int MaxPlayerCount = 8;

    [Header("역할 설정")]
    [SerializeField, Min(1)] private int killerCount = 1;

    [Networked, Capacity(MaxPlayerCount)]
    public NetworkDictionary<PlayerRef, PlayerRole> PlayerRoles => default;

    [Networked] public NetworkBool IsInitialized { get; set; }

    public bool HasSpawned { get; private set; }

    private MissionSystem missionSystem;

    public override void Spawned()
    {
        HasSpawned = true;
        missionSystem = GetComponent<MissionSystem>();

        if (!Object.HasStateAuthority || IsInitialized)
            return;

        InitializeGame();
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        HasSpawned = false;
    }

    private void InitializeGame()
    {
        List<PlayerRef> players = new List<PlayerRef>(Runner.ActivePlayers);

        if (players.Count < 2)
        {
            Debug.LogError("역할 배정에는 최소 2명의 플레이어가 필요합니다.");
            return;
        }

        ShufflePlayers(players);

        int validKillerCount = Mathf.Clamp(killerCount, 1, players.Count - 1);

        PlayerRoles.Clear();

        Dictionary<PlayerRef, PlayerRole> assignedRoles = new Dictionary<PlayerRef, PlayerRole>();

        for (int i = 0; i < players.Count; i++)
        {
            PlayerRef player = players[i];
            PlayerRole role = i < validKillerCount ? PlayerRole.Killer : PlayerRole.Citizen;

            PlayerRoles.Add(player, role);
            assignedRoles.Add(player, role);
        }

        missionSystem.InitializeMissions(assignedRoles);

        if (!missionSystem.IsInitialized)
        {
            PlayerRoles.Clear();
            Debug.LogError("미션 초기화에 실패하여 역할 배정을 취소했습니다.");
            return;
        }

        IsInitialized = true;

        Debug.Log($"게임 초기화 완료 - 플레이어: {players.Count}, 살인자: {validKillerCount}");
    }

    private void ShufflePlayers(List<PlayerRef> players)
    {
        System.Random random = new System.Random();

        for (int i = players.Count - 1; i > 0; i--)
        {
            int randomIndex = random.Next(i + 1);

            PlayerRef temporaryPlayer = players[i];
            players[i] = players[randomIndex];
            players[randomIndex] = temporaryPlayer;
        }
    }

    public bool TryGetRole(PlayerRef player, out PlayerRole role)
    {
        role = default;

        if (!HasSpawned)
            return false;

        return PlayerRoles.TryGet(player, out role);
    }

    public bool TryGetLocalRole(out PlayerRole role)
    {
        role = default;

        if (!HasSpawned || Runner == null)
            return false;

        return PlayerRoles.TryGet(Runner.LocalPlayer, out role);
    }
}