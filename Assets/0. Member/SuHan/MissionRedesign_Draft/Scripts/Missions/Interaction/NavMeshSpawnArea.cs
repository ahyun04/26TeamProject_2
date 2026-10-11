using System;
using UnityEngine;
using UnityEngine.AI;
using Random = UnityEngine.Random;

namespace TrustNoOne.Missions
{
    /// <summary>
    /// [역할] 1층 아이템 NavMesh 위에서 아이템 놓을 자리를 고른다 (호스트 — 6b 보강 명세 NS5 · NS9 · NS10). 세 장치(생명 유지 · 장비 조립 · 방화문)가 같이 쓴다.
    ///  - 만들 때 NavMesh 삼각형을 한 번 읽어 넓이 누적을 만든다 (메뉴가 구운 `NavMesh (1층 아이템 구역)` — 씬이 열리면 불러온다)
    ///  - 기준점: 장치 앞 1.5m 에서 3m 안 가장 가까운 NavMesh 점 (못 찾으면 장치 위치에서 다시).
    ///    장치 몸체가 길을 막아 바로 밑은 NavMesh 가 비고, 방화문 뒤는 벽과의 막힌 틈이라 거기서 경로를 재면 모두 실패할 수 있다
    ///  - "아무 곳": 넓이 비례 삼각형 → 그 안 무작위 점 / "근처": 고리 안 무작위 점 → 1m 안 가장 가까운 NavMesh 점
    ///  - 모든 자리는 기준점에서 경로가 끝까지 이어져야 한다 (장치에 갈 수 있으면 그 자리에도 갈 수 있다). 하나당 최대 40번
    /// [이유] 6b 의 자리 후보는 바닥 높이 · 걸어갈 수 있는지를 보지 않아 단상 속 · 울타리 너머에 아이템이 생겼다 (명세 1장).
    /// [근거] 6b 보강 명세 NS1 · NS5 · NS9 · NS10 · NS11 (사용자 결정 A — 질문 1 · 2)
    /// </summary>
    public sealed class NavMeshSpawnArea
    {
        /// <summary>아이템 하나당 최대 시도 수 (NS9).</summary>
        public const int MaxAttempts = 40;

        // 기준점: 장치 앞 이만큼에서 찾는다 · 찾는 반경 (NS5)
        private const float OriginAhead = 1.5f;
        private const float OriginSearchRadius = 3f;

        // "근처" 고르기: 고리 안 점에서 이 반경 안 가장 가까운 NavMesh 점을 쓴다 (NS9)
        private const float NearSnapRadius = 1f;

        private readonly Vector3[] vertices;
        private readonly int[] indices;
        private readonly float[] cumulative;
        private readonly float total;
        private readonly NavMeshPath path = new NavMeshPath();

        private NavMeshSpawnArea()
        {
            NavMeshTriangulation triangulation = NavMesh.CalculateTriangulation();
            vertices = triangulation.vertices;
            indices = triangulation.indices;
            cumulative = new float[indices.Length / 3];
            total = SpawnPointRules.BuildCumulativeAreas(vertices, indices, cumulative);
        }

        /// <summary>씬의 NavMesh 를 읽는다. 비어 있으면(메뉴로 굽지 않음) 오류 로그를 남기고 false.</summary>
        public static bool TryCreate(string owner, out NavMeshSpawnArea area)
        {
            area = new NavMeshSpawnArea();

            if (area.total > 0f)
                return true;

            Debug.LogError($"[NavMeshSpawnArea] {owner}: 씬에 1층 아이템 NavMesh 가 없어 아이템을 놓지 못했습니다 — " +
                           "메뉴 SuHan/Apply Missions To GamePlay(메인 씬) 또는 SuHan/Setup Solo Mission Test(테스트 씬)로 NavMesh 를 구우세요.");
            return false;
        }

        /// <summary>기준점: 장치 앞 1.5m 에서 3m 안 가장 가까운 NavMesh 점, 못 찾으면 장치 위치에서 다시.</summary>
        public bool TryGetOrigin(Transform station, out Vector3 origin)
        {
            Vector3 front = station.position + station.forward * OriginAhead;

            if (TrySnap(front, OriginSearchRadius, out origin) || TrySnap(station.position, OriginSearchRadius, out origin))
                return true;

            origin = station.position;
            return false;
        }

        /// <summary>NavMesh 아무 곳 (넓이 비례). accept 통과 + 기준점에서 경로 완성인 점.</summary>
        public bool TryPickAnywhere(Vector3 origin, Func<Vector3, bool> accept, out Vector3 point)
        {
            for (int attempt = 0; attempt < MaxAttempts; attempt++)
            {
                int t = SpawnPointRules.PickTriangle(cumulative, total, Random.value);

                if (t < 0)
                    break;

                Vector3 candidate = SpawnPointRules.PointInTriangle(
                    vertices[indices[t * 3]], vertices[indices[t * 3 + 1]], vertices[indices[t * 3 + 2]], Random.value, Random.value);

                if (accept(candidate) && IsReachable(origin, candidate))
                {
                    point = candidate;
                    return true;
                }
            }

            point = default;
            return false;
        }

        /// <summary>center 둘레 고리(minRadius ~ maxRadius) 안 NavMesh 점. accept 통과 + 기준점에서 경로 완성인 점.</summary>
        public bool TryPickNear(Vector3 origin, Vector3 center, float minRadius, float maxRadius, Func<Vector3, bool> accept, out Vector3 point)
        {
            for (int attempt = 0; attempt < MaxAttempts; attempt++)
            {
                Vector3 guess = SpawnPointRules.PointInRing(center, minRadius, maxRadius, Random.value, Random.value);

                if (!TrySnap(guess, NearSnapRadius, out Vector3 candidate))
                    continue;

                // 붙이면서 고리 밖으로 밀렸을 수 있다 → 다시 확인
                if (SpawnPointRules.InRing(candidate, center, minRadius, maxRadius) && accept(candidate) && IsReachable(origin, candidate))
                {
                    point = candidate;
                    return true;
                }
            }

            point = default;
            return false;
        }

        /// <summary>세로축 무작위 회전 (NS10 — 산소통 압력계가 여러 방향을 보게).</summary>
        public static Quaternion RandomYaw()
        {
            return Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
        }

        /// <summary>로그용 "(x, z)".</summary>
        public static string Describe(Vector3 point)
        {
            return $"({point.x:0.0}, {point.z:0.0})";
        }

        private static bool TrySnap(Vector3 position, float radius, out Vector3 snapped)
        {
            if (NavMesh.SamplePosition(position, out NavMeshHit hit, radius, NavMesh.AllAreas))
            {
                snapped = hit.position;
                return true;
            }

            snapped = position;
            return false;
        }

        private bool IsReachable(Vector3 origin, Vector3 target)
        {
            return NavMesh.CalculatePath(origin, target, NavMesh.AllAreas, path) && path.status == NavMeshPathStatus.PathComplete;
        }
    }
}
