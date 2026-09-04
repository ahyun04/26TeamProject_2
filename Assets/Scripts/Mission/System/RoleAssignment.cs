using Fusion;
using System.Collections.Generic;
using UnityEngine;

public enum PlayerRole
{
    Citizen,
    Killer
}

/// <summary>
/// 현재 게임에 참가한 플레이어들의 역할을 랜덤으로 결정하고,
/// 역할 배정이 끝나면 MissionSystem에 시민/살인마 목록을 전달
/// 역할 결정은 StateAuthority만 수행한다.
/// </summary>
public class RoleAssignment : NetworkBehaviour
{
    private const int MinPlayerCount = 2;
    private const int MaxPlayerCount = 10;

    [Header("역할 설정")]
    [SerializeField, Min(1)] private int killerCount = 1;

    [Header("연결")]
    [SerializeField] private MissionSystem missionSystem;

    // 역할 배정 중복 실행 방지
    [Networked]
    public NetworkBool Initialized { get; private set; }

    [Networked, Capacity(MaxPlayerCount)]
    public NetworkDictionary<PlayerRef, PlayerRole> Roles => default;

    public event System.Action OnRolesAssigned;


    public override void Spawned()
    {
        if (!HasStateAuthority)
            return;

        if (missionSystem == null)
            missionSystem = FindFirstObjectByType<MissionSystem>();
    }


    public override void FixedUpdateNetwork()
    {
        if (!HasStateAuthority || Initialized)
            return;

        if (missionSystem == null)
            missionSystem = FindFirstObjectByType<MissionSystem>();

        if (missionSystem == null || !missionSystem.IsReady)
            return;

        AssignRoles();
    }


    /// <summary>
    /// 특정 플레이어의 역할을 확인할 때 사용
    /// </summary>
    public bool TryGetRole(PlayerRef player, out PlayerRole role)
    {
        return Roles.TryGet(player, out role);
    }


    /// <summary>
    /// 현재 접속 플레이어를 랜덤으로 섞은 뒤
    /// 시민과 살인마로 나눈다
    /// </summary>
    private void AssignRoles()
    {
        List<PlayerRef> players = GetActivePlayers();

        if (players.Count < MinPlayerCount)
            return;

        int currentKillerCount = Mathf.Clamp(
            killerCount,
            1,
            players.Count - 1
        );

        Shuffle(players);

        List<PlayerRef> citizens = new();
        List<PlayerRef> killers = new();

        Roles.Clear();

        for (int i = 0; i < players.Count; i++)
        {
            PlayerRef player = players[i];

            if (i < currentKillerCount)
            {
                killers.Add(player);
                Roles.Add(player, PlayerRole.Killer);

                Debug.Log($"[역할 배정] {player} → Killer");
            }
            else
            {
                citizens.Add(player);
                Roles.Add(player, PlayerRole.Citizen);

                Debug.Log($"[역할 배정] {player} → Citizen");
            }
        }

        // 역할 배정 결과를 기존 MissionSystem에 전달
        missionSystem.InitializeMissions(citizens, killers);

        Initialized = true;

        OnRolesAssigned?.Invoke();

        Debug.Log(
            $"[RoleAssignment] 역할 배정 완료 / " +
            $"Citizen: {citizens.Count} / " +
            $"Killer: {killers.Count}"
        );
    }


    /// <summary>
    /// 현재 Fusion 방에 접속해 있는 플레이어 목록을 가져온다
    /// </summary>
    private List<PlayerRef> GetActivePlayers()
    {
        List<PlayerRef> players = new();

        foreach (PlayerRef player in Runner.ActivePlayers)
            players.Add(player);

        return players;
    }


    /// <summary>
    /// 플레이어 순서를 랜덤하게 섞음
    /// StateAuthority에서만 실행하기 때문에 결과 불일치가 발생하지 않음
    /// </summary>
    private void Shuffle(List<PlayerRef> players)
    {
        for (int i = players.Count - 1; i > 0; i--)
        {
            int randomIndex = Random.Range(0, i + 1);

            (players[i], players[randomIndex]) =
                (players[randomIndex], players[i]);
        }
    }
}
