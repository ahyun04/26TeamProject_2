using Fusion;
using UnityEngine;

public class GameplayPlayerSpawner : NetworkBehaviour
{
    [SerializeField] private NetworkObject playerPrefab;
    [SerializeField] private Transform[] spawnPoints;

    public override void Spawned()
    {
        Runner.ProvideInput = true;

        if (!HasStateAuthority)
            return;

        if (playerPrefab == null || spawnPoints == null || spawnPoints.Length == 0 ||
            System.Array.Exists(spawnPoints, point => point == null))
        {
            Debug.LogError("[GameplayPlayerSpawner] Player Prefab 또는 Spawn Points가 올바르게 연결되지 않았습니다.");
            return;
        }
        NetworkBootstrap.OnPlayerLeftEvent += HandlePlayerLeft;

        int index = 0;

        foreach (PlayerRef player in Runner.ActivePlayers)
        {
            Transform spawnPoint = spawnPoints[index % spawnPoints.Length];

            NetworkObject playerObject = Runner.Spawn(
                playerPrefab,
                spawnPoint.position,
                spawnPoint.rotation,
                player
            );

            Runner.SetPlayerObject(player, playerObject);

            index++;
        }
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        NetworkBootstrap.OnPlayerLeftEvent -= HandlePlayerLeft;
    }

    private void HandlePlayerLeft(NetworkRunner runner, PlayerRef player)
    {
        if (runner != Runner || !HasStateAuthority) return;
        NetworkObject playerObject = runner.GetPlayerObject(player);
        if (playerObject == null || !playerObject.IsValid) return;
        playerObject.GetComponent<PlayerItemController>()?.DropCurrentItem();
        runner.Despawn(playerObject);
    }
}
