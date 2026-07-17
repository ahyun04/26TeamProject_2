using Fusion;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 로비 플레이어와 준비 상태를 관리하고,
/// 호스트의 게임 시작을 승인합니다.
/// </summary>
public class LobbyReadySystem : NetworkBehaviour, IPlayerJoined, IPlayerLeft
{
    private const int MaxPlayerCount = 8;

    [Header("게임 시작 조건")]
    [SerializeField, Min(2)]
    private int minimumPlayerCount = 2;

    [Header("게임 씬")]
    [SerializeField]
    private int gameSceneBuildIndex = 2;

    [Networked, Capacity(MaxPlayerCount)]
    public NetworkDictionary<PlayerRef, NetworkBool> ReadyStates => default;

    [Networked]
    public PlayerRef HostPlayer { get; set; }

    [Networked]
    public NetworkBool IsInitialized { get; set; }

    [Networked]
    public NetworkBool IsGameStarting { get; set; }

    public bool HasSpawned { get; private set; }

    public int MinimumPlayerCount => minimumPlayerCount;

    public int PlayerCount => ReadyStates.Count;

    public bool IsLocalHost =>
    HasSpawned &&
    IsInitialized &&
    Runner != null &&
    Runner.LocalPlayer == HostPlayer;

    public override void Spawned()
    {
        HasSpawned = true;

        if (!Object.HasStateAuthority)
            return;

        HostPlayer = Runner.LocalPlayer;

        ReadyStates.Clear();

        foreach (PlayerRef player in Runner.ActivePlayers)
        {
            AddPlayer(player);
        }

        IsInitialized = true;
    }

    public void PlayerJoined(PlayerRef player)
    {
        if (!HasSpawned || !Object.HasStateAuthority || !IsInitialized)
        {
            return;
        }

        AddPlayer(player);
    }

    public void PlayerLeft(PlayerRef player)
    {
        if (!HasSpawned || !Object.HasStateAuthority)
        {
            return;
        }

        ReadyStates.Remove(player);
    }

    private void AddPlayer(PlayerRef player)
    {
        if (ReadyStates.ContainsKey(player))
            return;

        if (ReadyStates.Count >= MaxPlayerCount)
        {
            Debug.LogError(
                "로비 최대 플레이어 수를 초과했습니다.");

            return;
        }

        // 호스트는 준비 버튼을 누르지 않으므로
        // 처음부터 준비 완료로 저장합니다.
        bool initialReady =
            player == HostPlayer;

        ReadyStates.Add(player, initialReady);
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        HasSpawned = false;
    }

    /// <summary>
    /// 게스트의 준비 버튼에서 호출합니다.
    /// </summary>
    public void ToggleLocalReady()
    {
        if (!HasSpawned || !IsInitialized || IsGameStarting || Runner == null)
            return;

        PlayerRef localPlayer = Runner.LocalPlayer;

        if (localPlayer == HostPlayer)
            return;

        if (!ReadyStates.TryGet(localPlayer, out NetworkBool currentReady))
            return;

        RPC_SetReady(!currentReady);
    }

    [Rpc(
        RpcSources.All,
        RpcTargets.StateAuthority)]
    private void RPC_SetReady(
        bool isReady,
        RpcInfo info = default)
    {
        if (!Object.HasStateAuthority ||
            IsGameStarting)
        {
            return;
        }

        PlayerRef player =
            info.Source;

        if (player == PlayerRef.None ||
            player == HostPlayer)
        {
            return;
        }

        if (!ReadyStates.ContainsKey(player))
            return;

        ReadyStates.Set(player, isReady);
    }

    /// <summary>
    /// 호스트의 게임 시작 버튼에서 호출합니다.
    /// </summary>
    public void TryStartGame()
    {
        if (!Object.HasStateAuthority ||
            !Runner.IsSceneAuthority ||
            IsGameStarting)
        {
            return;
        }

        if (!CanStartGame())
        {
            Debug.LogWarning(
                "모든 게스트가 준비되지 않았습니다.");

            return;
        }

        if (gameSceneBuildIndex < 0 ||
            gameSceneBuildIndex >=
            SceneManager.sceneCountInBuildSettings)
        {
            Debug.LogError(
                "게임 씬 Build Index가 올바르지 않습니다.");

            return;
        }

        IsGameStarting = true;

        Runner.LoadScene(
            SceneRef.FromIndex(
                gameSceneBuildIndex),
            LoadSceneMode.Single);
    }

    public bool CanStartGame()
    {
        if (!HasSpawned || !IsInitialized || IsGameStarting || ReadyStates.Count < minimumPlayerCount)
            return false;

        bool hasGuest = false;

        foreach (KeyValuePair<PlayerRef, NetworkBool> pair in ReadyStates)
        {
            if (pair.Key == HostPlayer)
                continue;

            hasGuest = true;

            if (!pair.Value)
                return false;
        }

        return hasGuest;
    }

    public bool IsLocalPlayerReady()
    {
        if (Runner == null)
            return false;

        if (!ReadyStates.TryGet(
                Runner.LocalPlayer,
                out NetworkBool ready))
        {
            return false;
        }

        return ready;
    }

    public void GetGuestReadyStatus(
        out int readyCount,
        out int guestCount)
    {
        readyCount = 0;
        guestCount = 0;

        foreach (KeyValuePair<
                     PlayerRef,
                     NetworkBool> pair
                 in ReadyStates)
        {
            if (pair.Key == HostPlayer)
                continue;

            guestCount++;

            if (pair.Value)
                readyCount++;
        }
    }

   
}