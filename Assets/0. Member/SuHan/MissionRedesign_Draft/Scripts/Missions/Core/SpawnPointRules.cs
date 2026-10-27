using System;
using System.Collections.Generic;
using UnityEngine;

namespace TrustNoOne.Missions
{
    /// <summary>
    /// [역할] 아이템을 NavMesh 위 무작위 지점에 놓을 때 쓰는 순수 계산 (6b 보강 명세 NS9 · NS11).
    ///  - 넓이 누적 · 넓이 비례 삼각형 고르기 → 넓은 구역 · 좁은 구역에 고르게 퍼진다 (넓이 0 인 삼각형은 뽑히지 않는다)
    ///  - 삼각형 안 · 고리 안 고른 분포 무작위 점
    ///  - 거리 조건: 간격(다른 점들에서 떨어짐) · 고리(중심에서 거리 범위) · 앞쪽(바라보는 방향으로 앞)
    ///  거리는 모두 가로면(x, z) — 단상 높이 차이로 간격 · 범위가 바뀌지 않게.
    ///  - (11b) 놓을 면 고르기: 위에서 아래로 쏜 광선에 맞은 면 중 바닥으로 볼 것 (PickSurface)
    /// [이유] NavMesh(엔진)와 떼어 엔진 밖에서 테스트한다 (T27). NavMesh 읽기 · 경로 검사는 NavMeshSpawnArea.
    /// [근거] 6b 보강 명세 NS6 ~ NS9 · NS11, 11b (11a 명세 7장 FL1 ~ FL6)
    /// </summary>
    public static class SpawnPointRules
    {
        /// <summary>
        /// 위에서 아래로 쏜 광선에 맞은 면 중 아이템을 놓을 면 (11b FL2): 무시 대상(플레이어 · 아이템 · 장치)이 아니고 위를 향한(법선 위쪽 성분 &gt; 0)
        /// 면 중 가장 가까운 것. 아래를 향한 면(판 아랫면 · 천장)과 수직면(벽)은 바닥이 아니다. 없으면 −1 (원래 자리 그대로).
        /// 팀원의 내려놓기(ItemBase.Unequip)와 같은 규칙이다.
        /// </summary>
        /// <param name="count">앞에서부터 쓸 결과 수 (광선 결과 배열이 더 길 수 있다)</param>
        public static int PickSurface(float[] distances, float[] normalYs, bool[] ignored, int count)
        {
            int best = -1;

            for (int i = 0; i < count; i++)
            {
                if (ignored[i] || normalYs[i] <= 0f)
                    continue;

                if (best < 0 || distances[i] < distances[best])
                    best = i;
            }

            return best;
        }

        /// <param name="cumulative">삼각형 수(indices.Count / 3) 길이. i 번째 = 0 ~ i 번째 삼각형 넓이 합</param>
        /// <returns>넓이 합 (m²)</returns>
        public static float BuildCumulativeAreas(IReadOnlyList<Vector3> vertices, IReadOnlyList<int> indices, float[] cumulative)
        {
            int triangles = Math.Min(indices.Count / 3, cumulative.Length);
            float total = 0f;

            for (int t = 0; t < triangles; t++)
            {
                Vector3 a = vertices[indices[t * 3]];
                Vector3 b = vertices[indices[t * 3 + 1]];
                Vector3 c = vertices[indices[t * 3 + 2]];
                total += Vector3.Cross(b - a, c - a).magnitude * 0.5f;
                cumulative[t] = total;
            }

            return total;
        }

        /// <summary>넓이에 비례해 삼각형 번호를 고른다. random01 = 1(끝)이면 마지막 넓이 있는 삼각형. 넓이가 모두 0 이면 −1.</summary>
        public static int PickTriangle(float[] cumulative, float total, float random01)
        {
            float target = Clamp01(random01) * total;
            int last = -1;

            for (int i = 0; i < cumulative.Length; i++)
            {
                float previous = i == 0 ? 0f : cumulative[i - 1];

                // 넓이 0 (일직선 · 겹친 점) — 뽑히지 않는다
                if (cumulative[i] <= previous)
                    continue;

                last = i;

                if (target < cumulative[i])
                    return i;
            }

            return last;
        }

        /// <summary>삼각형 안 고른 분포 무작위 점 (r1 의 제곱근을 써서 꼭짓점 쪽으로 몰리지 않게).</summary>
        public static Vector3 PointInTriangle(Vector3 a, Vector3 b, Vector3 c, float r1, float r2)
        {
            float s = (float)Math.Sqrt(Clamp01(r1));
            float t = Clamp01(r2);
            return a * (1f - s) + b * (s * (1f - t)) + c * (s * t);
        }

        /// <summary>가로면 고리(minRadius ~ maxRadius) 안 고른 분포 무작위 점. 높이는 중심과 같다 (NavMesh 에 붙이는 건 호출하는 쪽).</summary>
        public static Vector3 PointInRing(Vector3 center, float minRadius, float maxRadius, float angle01, float radius01)
        {
            double angle = Clamp01(angle01) * 2.0 * Math.PI;
            float min2 = minRadius * minRadius;
            float max2 = maxRadius * maxRadius;
            float radius = (float)Math.Sqrt(min2 + (max2 - min2) * Clamp01(radius01));
            return new Vector3(center.x + (float)Math.Cos(angle) * radius, center.y, center.z + (float)Math.Sin(angle) * radius);
        }

        /// <summary>point 가 others 모두에서 가로면 거리 minDistance 이상인가 (others 가 비면 true).</summary>
        public static bool IsFarFromAll(Vector3 point, IReadOnlyList<Vector3> others, float minDistance)
        {
            float min2 = minDistance * minDistance;

            for (int i = 0; i < others.Count; i++)
            {
                if (FlatDistanceSquared(point, others[i]) < min2)
                    return false;
            }

            return true;
        }

        /// <summary>point 가 center 에서 가로면 거리 minRadius ~ maxRadius 안인가 (경계 포함).</summary>
        public static bool InRing(Vector3 point, Vector3 center, float minRadius, float maxRadius)
        {
            float d2 = FlatDistanceSquared(point, center);
            return d2 >= minRadius * minRadius && d2 <= maxRadius * maxRadius;
        }

        /// <summary>point 가 origin 에서 forward(가로면) 쪽으로 minAhead 이상 앞에 있는가.</summary>
        public static bool InFront(Vector3 point, Vector3 origin, Vector3 forward, float minAhead)
        {
            float length = (float)Math.Sqrt(forward.x * forward.x + forward.z * forward.z);

            if (length < 1e-6f)
                return false;

            float ahead = ((point.x - origin.x) * forward.x + (point.z - origin.z) * forward.z) / length;
            return ahead >= minAhead;
        }

        private static float FlatDistanceSquared(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x;
            float dz = a.z - b.z;
            return dx * dx + dz * dz;
        }

        private static float Clamp01(float value)
        {
            return value < 0f ? 0f : value > 1f ? 1f : value;
        }
    }
}
