using System;
using System.Collections.Generic;
using System.Text;
using Fusion;
using UnityEngine;
using UnityEngine.AI;
using Random = UnityEngine.Random;

namespace TrustNoOne.Missions
{
    /// <summary>
    /// [역할] 맵의 아이템 자리 관리자 (12b 명세 IS5 · IS10 ~ IS17, 호스트에서만 동작). 4~12 메뉴가 `Missions (새 미션)/Item Spots (아이템 자리)` 에 만든다.
    ///  - 자리 목록 (첫 요청 때 한 번): 자식 구역 상자(ItemZone) 바닥을 1.5m 격자로 훑은 아이템 NavMesh 점 + 씬의 모든 선반 칸(ShelfSlot)
    ///  - TryPlace(프리팹): 그 아이템이 들어갈 수 있는 빈자리 하나를 무작위로 고르고(ItemSpotRules.Pick) 쓴 자리로 기록 → 놓을 위치 · 회전
    ///    바닥 = 실제 바닥 높이(11b ToSurface) + 무작위 방향, 선반 = 칸에 맞는 자세(ItemPoseRules — 긴 축 → 칸 폭, 짧은 축 → 높이)
    ///  - 무기 · 청소기: 맡은 장치가 없어 판마다 미션 배정이 끝난 순간 한 번 직접 만든다 — 무기 3종 각 1개 늘, 청소기는 필터 미션이 뽑힌 판만 (7a TC12)
    /// [장치와의 관계] 생명 유지(산소통) · 장비 조립(부품)은 이 관리자가 씬에 있으면 자리를 받아 가고, 없으면(GamePlay · 테스트 씬) 지금 방식 (IS16).
    ///  개수 · 위험 여부 · 7e 다시 생성 · 부품 다시 흩기 같은 규칙은 장치에 그대로 남는다 — 바뀌는 것은 "어디에"뿐 (접근 1).
    /// [쓴 자리 기록] 한 판 동안만 (씬이 새로 열리면 비워진다). 다시 생기는 아이템은 아직 안 쓴 자리에서 (IS15).
    /// [동기화] 호스트가 Runner.Spawn 으로 만든다 — 눕힌 자세도 생성 순간의 위치 · 회전으로 모든 PC 에 같다 (IS20). 이 컴포넌트 자체는 네트워크 상태가 없다.
    /// [근거] 12b 명세 IS1 ~ IS20 (사용자 결정 A — 질문 1 ~ 4, 청소기 선반 추가, 접근 1)
    /// </summary>
    public class ItemSpotDirector : MonoBehaviour
    {
        /// <summary>아이템 하나의 상자 크기 (아이템 루트 기준 — 루트 크기 포함, 루트 회전 제외). 메뉴가 프리팹의 렌더러로 잰다 (IS13).</summary>
        [Serializable]
        public class ItemSize
        {
            public NetworkObject Prefab;
            public Vector3 Center;
            public Vector3 Size;
        }

        [Tooltip("비워 두면 씬에서 찾는다")]
        [SerializeField] private MissionManager manager;

        [Tooltip("아이템 크기표 — 선반 칸에 들어가는지 · 놓는 자세를 정한다 (메뉴가 채운다). 표에 없는 아이템은 바닥에만")]
        [SerializeField] private ItemSize[] itemSizes = new ItemSize[0];

        [Tooltip("판마다 하나씩 만드는 무기 (팀원 프리팹 — 부르기만)")]
        [SerializeField] private NetworkObject[] weaponPrefabs = new NetworkObject[0];

        [Tooltip("필터 미션이 뽑힌 판에만 만드는 청소기")]
        [SerializeField] private NetworkObject vacuumPrefab;

        [Tooltip("선반 무리를 고를 확률 0 ~ 1. −1 = 자리 수 비례 (대략 바닥 2 : 선반 1)")]
        [Range(-1f, 1f)]
        [SerializeField] private float shelfChance = -1f;

        [Tooltip("구역 바닥 격자 간격 (m)")]
        [Min(0.5f)]
        [SerializeField] private float floorStep = 1.5f;

        // 격자 점에서 아이템 NavMesh 를 찾는 반경 · 찾은 점이 격자 점에서 가로로 벗어나도 되는 거리 (m) — 장치 · 선반 몸체 자리는 NavMesh 가 비어 빠진다
        private const float FloorSearchRadius = 1.5f;
        private const float FloorHorizontalLimit = 0.6f;

        private struct Spot
        {
            public Vector3 Position;
            public ShelfSlot Slot;
            public string Name;
            public bool IsShelf => Slot != null;
        }

        private readonly List<Spot> spots = new List<Spot>();
        private bool[] used = new bool[0];
        private bool built;
        private bool extrasSpawned;

        /// <summary>씬의 자리 관리자. 없으면(GamePlay · 테스트 씬) false — 장치는 지금 방식으로 놓는다.</summary>
        public static bool TryGet(out ItemSpotDirector director)
        {
            director = FindFirstObjectByType<ItemSpotDirector>();
            return director != null;
        }

        /// <summary>
        /// 호스트: 이 아이템이 들어갈 빈자리 하나를 무작위로 골라 쓴 자리로 기록하고, 놓을 위치 · 회전과 자리 이름(로그)을 준다.
        /// 빈자리가 없으면 false — 부르는 쪽이 지금 방식으로 놓는다 (IS14).
        /// </summary>
        public bool TryPlace(NetworkObject prefab, out Vector3 position, out Quaternion rotation, out string where)
        {
            EnsureSpots();
            ItemSize size = FindSize(prefab);
            bool[] usable = new bool[spots.Count];
            bool[] shelf = new bool[spots.Count];

            for (int i = 0; i < spots.Count; i++)
            {
                Spot spot = spots[i];
                shelf[i] = spot.IsShelf;

                if (used[i])
                    continue;

                usable[i] = !spot.IsShelf
                    || (spot.Slot != null && size != null
                        && ItemPoseRules.TryFit(size.Center, size.Size, spot.Slot.Width, spot.Slot.Height, spot.Slot.Depth, out _, out _));
            }

            int pick = ItemSpotRules.Pick(usable, shelf, shelfChance, Random.value, Random.value);

            if (pick < 0)
            {
                position = default;
                rotation = Quaternion.identity;
                where = null;
                return false;
            }

            used[pick] = true;
            Spot chosen = spots[pick];
            where = chosen.Name;

            if (chosen.IsShelf)
            {
                ItemPoseRules.TryFit(size.Center, size.Size, chosen.Slot.Width, chosen.Slot.Height, chosen.Slot.Depth, out Quaternion local, out Vector3 offset);
                Transform slot = chosen.Slot.transform;
                rotation = slot.rotation * local;
                position = slot.position + slot.rotation * offset;
            }
            else
            {
                position = NavMeshSpawnArea.ToSurface(GetPhysics(), chosen.Position);
                rotation = NavMeshSpawnArea.RandomYaw();
            }

            return true;
        }

        private void Update()
        {
            if (extrasSpawned)
                return;

            if (manager == null)
                manager = FindFirstObjectByType<MissionManager>();

            // 호스트 · 미션 배정이 끝난 뒤 한 번 (장치들의 OnMissionsAssignedHost 와 같은 때)
            if (manager == null || manager.Object == null || !manager.Object.IsValid || !manager.Object.HasStateAuthority || !manager.Initialized)
                return;

            extrasSpawned = true;
            SpawnExtras();
        }

        /// <summary>무기 3종 각 1개 늘, 청소기는 필터 미션이 이번 판에 뽑혔을 때만 (IS17).</summary>
        private void SpawnExtras()
        {
            StringBuilder log = new StringBuilder();

            foreach (NetworkObject weapon in weaponPrefabs)
            {
                if (weapon != null)
                    SpawnOne(weapon, log);
            }

            if (vacuumPrefab == null)
                log.Append("청소기 프리팹 없음  ");
            else if (manager.IsEventInPlan(MissionEventType.FilterCleaned))
                SpawnOne(vacuumPrefab, log);
            else
                log.Append("청소기: 필터 미션이 없는 판 — 만들지 않음  ");

            Debug.Log($"[ItemSpotDirector] 무기 · 청소기: {log}");
        }

        private void SpawnOne(NetworkObject prefab, StringBuilder log)
        {
            if (!TryPlace(prefab, out Vector3 position, out Quaternion rotation, out string where) && !TryFallback(out position, out rotation, out where))
            {
                Debug.LogWarning($"[ItemSpotDirector] {prefab.name}: 놓을 자리를 찾지 못해 만들지 않았습니다.");
                return;
            }

            manager.Runner.Spawn(prefab, position, rotation);
            log.Append(prefab.name).Append(" → ").Append(where).Append("  ");
        }

        /// <summary>빈자리가 없을 때 (IS14): 첫 바닥 자리를 기준점으로 아이템 NavMesh 아무 곳.</summary>
        private bool TryFallback(out Vector3 position, out Quaternion rotation, out string where)
        {
            position = default;
            rotation = Quaternion.identity;
            where = null;
            Spot origin = spots.Find(spot => !spot.IsShelf);

            if (origin.Name == null || !NavMeshSpawnArea.TryCreate(name, GetPhysics(), out NavMeshSpawnArea area)
                || !area.TryPickAnywhere(origin.Position, _ => true, out position))
                return false;

            rotation = NavMeshSpawnArea.RandomYaw();
            where = $"빈자리 없음 → NavMesh 아무 곳 {NavMeshSpawnArea.Describe(position)}";
            Debug.LogWarning($"[ItemSpotDirector] 빈자리가 없어 지금 방식으로 놓습니다: {where}");
            return true;
        }

        /// <summary>자리 목록을 한 번 만든다: 구역 바닥 격자의 NavMesh 점 + 모든 선반 칸 (IS10).</summary>
        private void EnsureSpots()
        {
            if (built)
                return;

            built = true;
            int floors = 0;

            foreach (ItemZone zone in GetComponentsInChildren<ItemZone>())
            {
                Vector3 size = zone.Size;

                foreach (Vector2 cell in ItemSpotRules.GridLocalPoints(size.x, size.z, floorStep))
                {
                    Vector3 guess = zone.ToWorld(cell);

                    if (!NavMesh.SamplePosition(guess, out NavMeshHit hit, FloorSearchRadius, NavMesh.AllAreas))
                        continue;

                    Vector3 offset = hit.position - guess;
                    offset.y = 0f;

                    if (offset.magnitude > FloorHorizontalLimit || !zone.Contains(hit.position))
                        continue;

                    spots.Add(new Spot { Position = hit.position, Name = $"{zone.name} 바닥 {NavMeshSpawnArea.Describe(hit.position)}" });
                    floors++;
                }
            }

            int shelves = 0;

            foreach (ShelfSlot slot in FindObjectsByType<ShelfSlot>(FindObjectsSortMode.None))
            {
                string shelfName = slot.transform.parent != null ? slot.transform.parent.name : "선반";
                spots.Add(new Spot { Position = slot.transform.position, Slot = slot, Name = $"선반 {shelfName} · {slot.name}" });
                shelves++;
            }

            used = new bool[spots.Count];
            Debug.Log($"[ItemSpotDirector] 자리 목록: 구역 바닥 {floors}곳 · 선반 칸 {shelves}칸 (선반 확률 {(shelfChance < 0f ? "자리 수 비례" : shelfChance.ToString("0.##"))})");
        }

        private ItemSize FindSize(NetworkObject prefab)
        {
            foreach (ItemSize size in itemSizes)
            {
                if (size != null && size.Prefab == prefab)
                    return size;
            }

            return null;
        }

        private PhysicsScene GetPhysics()
        {
            return manager != null && manager.Runner != null ? manager.Runner.GetPhysicsScene() : Physics.defaultPhysicsScene;
        }
    }
}
