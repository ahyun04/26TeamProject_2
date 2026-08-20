using System.Collections.Generic;
using Fusion;
using LockdownProtocol.Networking;
using UnityEngine;

namespace LockdownProtocol.Lobby
{
    /// <summary>
    /// 방에 참가한 플레이어의 LobbyPlayerController 프리팹을 Spawn/Despawn한다.
    /// PlayerSpawner(게임 씬용)와 동일한 NetworkBootstrap 이벤트 구독 패턴을 사용한다.
    /// 인게임 PlayerSpawner와는 별개 — 로비 씬에만 존재하고, 게임 씬 전환 시점에
    /// LobbyGameStartManager가 먼저 이 오브젝트들을 정리한다는 전제.
    /// </summary>
    public class LobbyPlayerSpawner : MonoBehaviour
    {
        [Header("Prefab")]
        [SerializeField] private NetworkObject lobbyPlayerPrefab;

        [Header("Spawn Points")]
        [SerializeField] private Transform[] spawnPoints;

        private readonly Dictionary<PlayerRef, NetworkObject> _spawnedPlayers = new Dictionary<PlayerRef, NetworkObject>();
        private int _joinOrderCounter;

        private void OnEnable()
        {
            NetworkBootstrap.OnPlayerJoinedEvent += HandlePlayerJoined;
            NetworkBootstrap.OnPlayerLeftEvent += HandlePlayerLeft;
        }

        private void OnDisable()
        {
            NetworkBootstrap.OnPlayerJoinedEvent -= HandlePlayerJoined;
            NetworkBootstrap.OnPlayerLeftEvent -= HandlePlayerLeft;
        }

        private void HandlePlayerJoined(NetworkRunner runner, PlayerRef player)
        {
            if (!runner.IsServer) return;

            if (lobbyPlayerPrefab == null)
            {
                Debug.LogError("[LobbyPlayerSpawner] Lobby Player Prefab이 할당되지 않았습니다.");
                return;
            }

            Vector3 spawnPosition = GetSpawnPosition(player);
            NetworkObject spawnedObject = runner.Spawn(lobbyPlayerPrefab, spawnPosition, Quaternion.identity, player);
            _spawnedPlayers[player] = spawnedObject;

            var controller = spawnedObject.GetComponent<LobbyPlayerController>();
            if (controller != null)
            {
                controller.SetJoinOrder(_joinOrderCounter++);

                bool isHost = RoomManager.Instance != null && RoomManager.Instance.HostPlayerId == player;
                controller.SetHost(isHost);
            }

            Debug.Log($"[LobbyPlayerSpawner] Lobby Player {player} 스폰 완료 at {spawnPosition}");
        }

        private void HandlePlayerLeft(NetworkRunner runner, PlayerRef player)
        {
            if (!runner.IsServer) return;

            if (_spawnedPlayers.TryGetValue(player, out NetworkObject spawnedObject))
            {
                runner.Despawn(spawnedObject);
                _spawnedPlayers.Remove(player);
                Debug.Log($"[LobbyPlayerSpawner] Lobby Player {player} 오브젝트 제거 완료");
            }
        }

        /// <summary>방장이 바뀌었을 때 RoomManager가 호출해서 새 방장의 LobbyPlayerController를 갱신.</summary>
        public void RefreshHostFlag(PlayerRef newHost)
        {
            foreach (var kvp in _spawnedPlayers)
            {
                var controller = kvp.Value.GetComponent<LobbyPlayerController>();
                if (controller == null) continue;
                controller.SetHost(kvp.Key == newHost);
            }
        }

        private Vector3 GetSpawnPosition(PlayerRef player)
        {
            if (spawnPoints == null || spawnPoints.Length == 0)
            {
                return new Vector3(player.RawEncoded % 10, 1f, 0f);
            }
            int index = player.RawEncoded % spawnPoints.Length;
            return spawnPoints[index].position;
        }
    }
}