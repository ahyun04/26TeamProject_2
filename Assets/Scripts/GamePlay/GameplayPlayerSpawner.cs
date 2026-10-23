using System.Collections.Generic;
using Fusion;
using UnityEngine;

public class GameplayPlayerSpawner : NetworkBehaviour
{
    [SerializeField] private NetworkObject playerPrefab;
    [SerializeField] private Transform[] spawnPoints;
    [SerializeField, Min(1)] private int playersPerSpawnPoint = 1; //구역마다 배정할 최대 인원과 자식 스폰 위치 수

    public override void Spawned()
    {
        Runner.ProvideInput = true;

        if (!HasStateAuthority)
            return;

        var players = new List<PlayerRef>(Runner.ActivePlayers); //이번 경기에 참가한 플레이어
        List<Transform> positions = getSpawnOrder(players.Count); //겹치지 않는 무작위 구역별 배정
        if (playerPrefab == null || positions.Count != players.Count)
        {
            Debug.LogError("[GameplayPlayerSpawner] Player Prefab 또는 Spawn Points가 올바르게 연결되지 않았습니다.");
            return;
        }
        NetworkBootstrap.OnPlayerLeftEvent += HandlePlayerLeft;

        for (int index = 0; index < players.Count; index++)
        {
            PlayerRef player = players[index]; //해당 위치에 배정한 참가자
            Transform spawnPoint = positions[index]; //구역별 서로 다른 위치

            NetworkObject playerObject = Runner.Spawn(
                playerPrefab,
                spawnPoint.position,
                spawnPoint.rotation,
                player
            );

            Runner.SetPlayerObject(player, playerObject);
        }
    }

    internal List<Transform> getSpawnOrder(int playerCount) //네 구역을 먼저 채운 뒤 구역별 제한 안에서 무작위 분산
    {
        var result = new List<Transform>(); //서버에서 한 번 결정할 스폰 순서
        if (spawnPoints == null || spawnPoints.Length == 0 || playersPerSpawnPoint < 1 ||
            playerCount > spawnPoints.Length * playersPerSpawnPoint) return result;
        var groups = new List<List<Transform>>(); //각 구역의 중복 없는 위치
        var uniquePositions = new HashSet<Transform>(); //잘못 중복 연결한 위치 검증
        foreach (Transform point in spawnPoints)
        {
            if (point == null || (playersPerSpawnPoint > 1 && point.childCount < playersPerSpawnPoint)) return result;
            var slots = new List<Transform>(); //구역 내 무작위 배정 후보
            for (int i = 0; i < playersPerSpawnPoint; i++)
            {
                Transform slot = playersPerSpawnPoint == 1 ? point : point.GetChild(i); //기존 단일 위치 설정도 유지
                if (!uniquePositions.Add(slot)) return result;
                slots.Insert(Random.Range(0, slots.Count + 1), slot);
            }
            groups.Insert(Random.Range(0, groups.Count + 1), slots);
        }
        for (int round = 0; round < playersPerSpawnPoint && result.Count < playerCount; round++)
            foreach (List<Transform> slots in groups)
            {
                if (result.Count == playerCount) break;
                result.Add(slots[round]);
            }
        return result;
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        NetworkBootstrap.OnPlayerLeftEvent -= HandlePlayerLeft;
    }

    private void HandlePlayerLeft(NetworkRunner runner, PlayerRef player)
    {
        if (runner != Runner || !HasStateAuthority) return;
        GameEndSystem gameEnd = FindFirstObjectByType<GameEndSystem>();
        if (gameEnd != null && gameEnd.Object != null && gameEnd.Object.IsValid && gameEnd.Runner == runner)
            gameEnd.handlePlayerLeft(player);
        NetworkObject playerObject = runner.GetPlayerObject(player);
        if (playerObject == null || !playerObject.IsValid) return;
        playerObject.GetComponent<PlayerItemController>()?.DropCurrentItem();
        runner.Despawn(playerObject);
    }
}
