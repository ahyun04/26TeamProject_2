using System;
using System.Collections.Generic;
using UnityEngine;

namespace TrustNoOne.Missions
{
    /// <summary>
    /// [역할] 아이템 자리 목록의 순수 계산 (12b 명세 IS10 · IS11 — T42).
    ///  - GridLocalPoints: 구역 상자 바닥을 일정 간격 격자로 나눈 칸 가운데 점 (단위 상자 공간 −0.5 ~ 0.5 — 상자의 위치 · 회전 · 크기는 부르는 쪽이 곱한다)
    ///  - Pick: 쓸 수 있는 자리(아이템이 들어가고 · 아직 안 쓴 자리) 중 하나. 선반 확률이 비어 있으면(음수) 자리 수 비례로 고르게,
    ///    값이 있으면 그 확률로 선반 무리 · 아니면 바닥 무리를 먼저 고르고 그 안에서 고르게 (고른 무리가 비면 다른 무리)
    /// [이유] 난수는 부르는 쪽이 넣어 결과를 엔진 밖에서 확인한다 (6b SpawnPointRules 와 같은 방식).
    /// [근거] 12b 명세 IS10 (바닥 1.5m 격자) · IS11 (빈자리 무작위 · 선반 확률)
    /// </summary>
    public static class ItemSpotRules
    {
        /// <summary>상자 바닥(가로 sizeX · 세로 sizeZ, m)을 step 간격으로 나눈 칸 가운데 (단위 상자 공간). 간격보다 작은 쪽은 가운데 한 줄.</summary>
        public static List<Vector2> GridLocalPoints(float sizeX, float sizeZ, float step)
        {
            int countX = step > 0f ? Math.Max(1, (int)Math.Floor(sizeX / step)) : 1;
            int countZ = step > 0f ? Math.Max(1, (int)Math.Floor(sizeZ / step)) : 1;
            List<Vector2> points = new List<Vector2>(countX * countZ);

            for (int x = 0; x < countX; x++)
            {
                for (int z = 0; z < countZ; z++)
                    points.Add(new Vector2(-0.5f + (x + 0.5f) / countX, -0.5f + (z + 0.5f) / countZ));
            }

            return points;
        }

        /// <param name="usable">자리마다 쓸 수 있는가 (아이템이 들어감 · 아직 안 씀)</param>
        /// <param name="isShelf">자리마다 선반 칸인가 (아니면 바닥)</param>
        /// <param name="shelfChance">선반 무리를 고를 확률 0 ~ 1. 음수면 자리 수 비례</param>
        /// <param name="r1">무리 안 고르기 난수 0 ~ 1</param>
        /// <param name="r2">무리 고르기 난수 0 ~ 1</param>
        /// <returns>고른 자리 번호, 없으면 −1</returns>
        public static int Pick(IReadOnlyList<bool> usable, IReadOnlyList<bool> isShelf, float shelfChance, float r1, float r2)
        {
            List<int> shelves = new List<int>();
            List<int> floors = new List<int>();

            for (int i = 0; i < usable.Count; i++)
            {
                if (!usable[i])
                    continue;

                if (isShelf[i])
                    shelves.Add(i);
                else
                    floors.Add(i);
            }

            if (shelves.Count == 0 && floors.Count == 0)
                return -1;

            List<int> group;

            if (shelfChance < 0f)
            {
                group = new List<int>(usable.Count);

                for (int i = 0; i < usable.Count; i++)
                {
                    if (usable[i])
                        group.Add(i);
                }
            }
            else
            {
                bool wantShelf = r2 < shelfChance;
                group = wantShelf ? (shelves.Count > 0 ? shelves : floors) : (floors.Count > 0 ? floors : shelves);
            }

            int index = (int)(Clamp01(r1) * group.Count);
            return group[Math.Min(index, group.Count - 1)];
        }

        private static float Clamp01(float value) => value < 0f ? 0f : value > 1f ? 1f : value;
    }
}
