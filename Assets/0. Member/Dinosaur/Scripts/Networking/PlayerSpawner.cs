using System.Collections.Generic;
using Fusion;
using UnityEngine;

namespace LockdownProtocol.Networking
{
    /// <summary>
    /// 플레이어 접속/퇴장 이벤트를 구독해 Player 프리팹을 Spawn/Despawn하는 것만 책임진다.
    /// NetworkBootstrap이 쏘는 이벤트를 구독하는 단방향 의존 구조라,
    /// NetworkBootstrap은 이 클래스의 존재를 전혀 몰라도 된다 (느슨한 결합).
    ///
    /// Runner.Spawn()은 반드시 State Authority(Host)에서만 호출해야 하므로,
    /// 모든 진입점에서 runner.IsServer를 먼저 확인한다.
    /// </summary>
    public class PlayerSpawner : MonoBehaviour
    {
        [Header("Prefab")]
        [SerializeField] private NetworkObject playerPrefab;

        [Header("Spawn Points")]
        [SerializeField] private Transform[] spawnPoints;

        // 퇴장 시 정확히 어떤 NetworkObject를 Despawn할지 찾기 위한 추적 테이블.
        private readonly Dictionary<PlayerRef, NetworkObject> _spawnedPlayers = new Dictionary<PlayerRef, NetworkObject>();

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
            // Host(State Authority)만 스폰을 실행한다. Client에서도 이 이벤트는 들어오지만 무시해야 한다.
            if (!runner.IsServer)
                return;

            if (playerPrefab == null)
            {
                Debug.LogError("[PlayerSpawner] Player Prefab이 할당되지 않았습니다.");
                return;
            }

            Vector3 spawnPosition = GetSpawnPosition(player);

            // 4번째 인자(player)가 Input Authority다.
            // 이 오브젝트는 이제 해당 플레이어의 NetworkInputData를 GetInput()으로 받게 된다.
            NetworkObject spawnedObject = runner.Spawn(playerPrefab, spawnPosition, Quaternion.identity, player);
            _spawnedPlayers[player] = spawnedObject;

            Debug.Log($"[PlayerSpawner] Player {player} 스폰 완료 at {spawnPosition}");
        }

        private void HandlePlayerLeft(NetworkRunner runner, PlayerRef player)
        {
            if (!runner.IsServer)
                return;

            if (_spawnedPlayers.TryGetValue(player, out NetworkObject spawnedObject))
            {
                runner.Despawn(spawnedObject);
                _spawnedPlayers.Remove(player);
                Debug.Log($"[PlayerSpawner] Player {player} 오브젝트 제거 완료");
            }
        }

        private Vector3 GetSpawnPosition(PlayerRef player)
        {
            if (spawnPoints == null || spawnPoints.Length == 0)
            {
                // 스폰 포인트 미설정 시 임시로 원점 근처에 겹치지 않게 흩뿌림 (테스트 단계 한정)
                return new Vector3(player.RawEncoded % 10, 1f, 0f);
            }

            int index = player.RawEncoded % spawnPoints.Length;
            return spawnPoints[index].position;
        }
    }
}