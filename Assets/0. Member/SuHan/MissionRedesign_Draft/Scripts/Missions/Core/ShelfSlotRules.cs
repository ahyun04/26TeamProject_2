using System;
using System.Collections.Generic;
using UnityEngine;

namespace TrustNoOne.Missions
{
    /// <summary>선반 메시의 수평 면 하나 (선반 루트 기준, 높이 Y · 가로 사각형). 판(위를 향함) 또는 천장(아래를 향함).</summary>
    public readonly struct ShelfFaceRect
    {
        public readonly float Y;
        public readonly float MinX, MaxX, MinZ, MaxZ;

        public ShelfFaceRect(float y, float minX, float maxX, float minZ, float maxZ)
        {
            Y = y;
            MinX = minX;
            MaxX = maxX;
            MinZ = minZ;
            MaxZ = maxZ;
        }

        public float SizeX => MaxX - MinX;
        public float SizeZ => MaxZ - MinZ;
        public float Area => SizeX * SizeZ;
    }

    /// <summary>선반 칸 하나 (선반 루트 기준). 판 가운데 · 폭(긴 변) · 높이(판 → 천장) · 깊이(짧은 변) · 폭 방향 · 열린 쪽.</summary>
    public readonly struct ShelfCompartment
    {
        public readonly Vector3 BoardCenter;
        public readonly float Width, Height, Depth;
        public readonly Vector3 WidthAxis;
        public readonly Vector3 Front;

        public ShelfCompartment(Vector3 boardCenter, float width, float height, float depth, Vector3 widthAxis, Vector3 front)
        {
            BoardCenter = boardCenter;
            Width = width;
            Height = height;
            Depth = depth;
            WidthAxis = widthAxis;
            Front = front;
        }
    }

    /// <summary>
    /// [역할] 선반 모델의 수평 면에서 아이템을 넣을 칸을 찾는다 (12b 명세 IS8, 순수 규칙 — T40).
    ///  1) 같은 높이(1cm) · 같은 사각형은 하나로 (메시의 사각 면은 삼각형 두 개 — 둘의 가로 범위가 같다),
    ///     한 변을 통째로 맞댄 두 사각형은 합친다 (모델이 한 판을 두 면으로 나눈 경우 — 끝면 작은 칸)
    ///  2) 판마다 판보다 0.05m 넘게 위에 있고 판 넓이의 절반 이상 겹치는 천장 중 가장 낮은 것 → 칸 높이. 천장이 없으면 칸이 아니다 (맨 위 판)
    ///  3) 폭 0.3 · 깊이 0.15 · 높이 0.15m 보다 작으면 버린다 (테두리 턱 · 장식)
    ///  4) 긴 변 = 폭, 짧은 변 = 깊이. 열린 쪽 = 깊이 방향에서 선반 가운데의 반대쪽 (칸은 바깥으로 열려 있다)
    ///  5) (테스트 후 추가 — IS23) 막힌 칸: 입구 가운데(OpeningPoint)가 다른 선반 몸통 범위 안이면 막힌 칸 (IsBlocked) — 끝을 맞대고 겹쳐 놓인 선반의 끝 칸
    /// [이유] 선반 모델(모델 담당)을 고치지 않고 칸 위치를 메뉴가 계산하게 — 색 버전 · 새 선반에도 같은 규칙. 엔진 밖에서 테스트한다.
    /// [근거] 12b 명세 1-2 (Shelf.fbx 분석: 판 0.086 · 0.567 · 1.197, 천장 0.46 · 1.09 · 1.63, 맨 위 2.195 는 천장 없음), IS7 · IS8 · IS23
    /// </summary>
    public static class ShelfSlotRules
    {
        public const float MinWidth = 0.3f;
        public const float MinDepth = 0.15f;
        public const float MinHeight = 0.15f;

        // 판 위 이만큼은 판 자신의 두께 (판의 아랫면 · 옆면이 천장으로 잡히지 않게)
        private const float CeilingGap = 0.05f;
        // 천장이 판 넓이의 이만큼 이상 덮어야 그 판의 천장
        private const float CeilingCover = 0.5f;
        // 같은 높이 · 같은 변으로 볼 오차 (m)
        private const float Tolerance = 0.01f;

        /// <param name="boards">위를 향한 면 (메시 삼각형마다 하나여도 된다 — 안에서 합친다)</param>
        /// <param name="ceilings">아래를 향한 면</param>
        /// <param name="shelfCenter">선반 가운데 (루트 기준) — 열린 쪽 판단</param>
        public static List<ShelfCompartment> Find(IReadOnlyList<ShelfFaceRect> boards, IReadOnlyList<ShelfFaceRect> ceilings, Vector3 shelfCenter)
        {
            List<ShelfFaceRect> mergedBoards = MergeRects(boards);
            List<ShelfFaceRect> mergedCeilings = MergeRects(ceilings);
            List<ShelfCompartment> result = new List<ShelfCompartment>();

            foreach (ShelfFaceRect board in mergedBoards)
            {
                float ceiling = float.MaxValue;

                foreach (ShelfFaceRect top in mergedCeilings)
                {
                    if (top.Y <= board.Y + CeilingGap || top.Y >= ceiling)
                        continue;

                    if (OverlapArea(board, top) >= board.Area * CeilingCover)
                        ceiling = top.Y;
                }

                if (ceiling == float.MaxValue)
                    continue;

                bool widthAlongZ = board.SizeZ >= board.SizeX;
                float width = widthAlongZ ? board.SizeZ : board.SizeX;
                float depth = widthAlongZ ? board.SizeX : board.SizeZ;
                float height = ceiling - board.Y;

                if (width < MinWidth || depth < MinDepth || height < MinHeight)
                    continue;

                Vector3 center = new Vector3((board.MinX + board.MaxX) * 0.5f, board.Y, (board.MinZ + board.MaxZ) * 0.5f);
                Vector3 widthAxis = widthAlongZ ? Vector3.forward : Vector3.right;
                Vector3 front = widthAlongZ
                    ? (center.x >= shelfCenter.x ? Vector3.right : Vector3.left)
                    : (center.z >= shelfCenter.z ? Vector3.forward : Vector3.back);

                result.Add(new ShelfCompartment(center, width, height, depth, widthAxis, front));
            }

            return result;
        }

        /// <summary>칸 입구 가운데 = 칸 바닥 가운데 + 열린 쪽으로 깊이 절반 + 위로 높이 절반 (IS23). 좌표계는 넣은 값과 같다.</summary>
        public static Vector3 OpeningPoint(Vector3 boardCenter, Vector3 front, float height, float depth)
        {
            return boardCenter + front * (depth * 0.5f) + Vector3.up * (height * 0.5f);
        }

        /// <summary>
        /// 입구 가운데(월드)가 다른 선반 몸통 범위 안이면 막힌 칸 (IS23 — 테스트 후 추가: 4~12 선반이 2개씩 끝을 맞대고 0.3m 겹쳐
        /// 맞댄 쪽 끝 칸이 이웃 선반 안에 묻혔다). 다른 선반 범위는 그 선반 기준 좌표(worldToOther 로 옮겨서 본다).
        /// </summary>
        public static bool IsBlocked(Vector3 openingWorld, Matrix4x4 worldToOther, Bounds otherLocalBounds)
        {
            // Bounds.Contains 는 엔진 안쪽 호출이라 엔진 밖 테스트에서 못 쓴다 → 최소 · 최대로 직접 비교 (경계 포함)
            Vector3 p = worldToOther.MultiplyPoint3x4(openingWorld);
            Vector3 min = otherLocalBounds.min;
            Vector3 max = otherLocalBounds.max;
            return p.x >= min.x && p.x <= max.x && p.y >= min.y && p.y <= max.y && p.z >= min.z && p.z <= max.z;
        }

        /// <summary>같은 높이 · 같은 사각형은 하나로, 한 변을 통째로 맞댄 두 사각형은 합친다 (더 합칠 게 없을 때까지).</summary>
        public static List<ShelfFaceRect> MergeRects(IEnumerable<ShelfFaceRect> rects)
        {
            List<ShelfFaceRect> list = new List<ShelfFaceRect>();

            foreach (ShelfFaceRect rect in rects)
            {
                if (!list.Exists(other => Same(other, rect)))
                    list.Add(rect);
            }

            bool merged = true;

            while (merged)
            {
                merged = false;

                for (int i = 0; i < list.Count && !merged; i++)
                {
                    for (int j = i + 1; j < list.Count; j++)
                    {
                        if (!TryJoin(list[i], list[j], out ShelfFaceRect joined))
                            continue;

                        list[i] = joined;
                        list.RemoveAt(j);
                        merged = true;
                        break;
                    }
                }
            }

            return list;
        }

        private static bool Same(ShelfFaceRect a, ShelfFaceRect b)
        {
            return Near(a.Y, b.Y) && Near(a.MinX, b.MinX) && Near(a.MaxX, b.MaxX) && Near(a.MinZ, b.MinZ) && Near(a.MaxZ, b.MaxZ);
        }

        /// <summary>같은 높이에서 z 범위가 같고 x 로 맞닿거나, x 범위가 같고 z 로 맞닿으면 하나의 사각형.</summary>
        private static bool TryJoin(ShelfFaceRect a, ShelfFaceRect b, out ShelfFaceRect joined)
        {
            joined = default;

            if (!Near(a.Y, b.Y))
                return false;

            bool sameZ = Near(a.MinZ, b.MinZ) && Near(a.MaxZ, b.MaxZ);
            bool sameX = Near(a.MinX, b.MinX) && Near(a.MaxX, b.MaxX);
            bool touchX = Near(a.MaxX, b.MinX) || Near(b.MaxX, a.MinX);
            bool touchZ = Near(a.MaxZ, b.MinZ) || Near(b.MaxZ, a.MinZ);

            if (!(sameZ && touchX) && !(sameX && touchZ))
                return false;

            joined = new ShelfFaceRect(a.Y, Math.Min(a.MinX, b.MinX), Math.Max(a.MaxX, b.MaxX), Math.Min(a.MinZ, b.MinZ), Math.Max(a.MaxZ, b.MaxZ));
            return true;
        }

        private static float OverlapArea(ShelfFaceRect a, ShelfFaceRect b)
        {
            float x = Math.Min(a.MaxX, b.MaxX) - Math.Max(a.MinX, b.MinX);
            float z = Math.Min(a.MaxZ, b.MaxZ) - Math.Max(a.MinZ, b.MinZ);
            return x > 0f && z > 0f ? x * z : 0f;
        }

        private static bool Near(float a, float b) => Math.Abs(a - b) < Tolerance;
    }
}
