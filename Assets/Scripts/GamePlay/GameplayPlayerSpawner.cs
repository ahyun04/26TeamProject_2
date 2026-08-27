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
}