using System;
using UnityEngine;

namespace TrustNoOne.Missions
{
    /// <summary>
    /// [역할] 아이템을 선반 칸에 놓을 자세를 정한다 (12b 명세 IS12, 순수 규칙 — T41). 칸 공간: x = 칸 폭 방향, y = 위, z = 앞(열린 쪽), 원점 = 칸 바닥판 가운데.
    ///  - 아이템 상자의 가장 긴 축 → 칸 폭(x), 가장 짧은 축 → 높이(y), 나머지 → 깊이(z). 산소통처럼 세우면 안 들어가는 것은 눕는다
    ///  - 들어가는 조건: 길이 ≤ 칸 폭 − 0.1, 높이 ≤ 칸 높이 − 0.02. 깊이는 넘어도 된다 (청소기 — 앞으로 튀어나옴, IS3)
    ///  - 아랫면이 판 위(y 0), 가로 가운데. 깊이가 칸보다 깊으면 뒷면을 칸 뒤에 맞춘다 (선반 안쪽 벽을 뚫지 않게), 아니면 깊이도 가운데
    ///  - 회전은 뒤집힘(거울상)이 없게 — 축 배치가 거울상이면 깊이 축 방향을 뒤집는다
    /// [이유] 아이템마다 손으로 자세를 정하지 않고 크기만으로 정한다 — 새 아이템도 크기표만 있으면 된다. 엔진 밖에서 테스트한다.
    /// [높이 여유 0.02] 명세 IS12 의 0.03 에서 바꿈 (구현 중): 청소기 조준 상자의 가장 짧은 축이 약 0.51m, 가운데 층 칸 높이 0.523m —
    ///  0.03 이면 어느 칸에도 못 들어가 "청소기도 선반에"(IS3)를 지킬 수 없다.
    /// [회전 계산] Quaternion.LookRotation 은 엔진 안쪽 호출이라 엔진 밖 테스트에서 못 쓴다 → 회전 행렬에서 직접 만든다.
    /// [근거] 12b 명세 IS3 · IS12 (사용자 결정 A · 청소기 추가)
    /// </summary>
    public static class ItemPoseRules
    {
        /// <summary>칸 폭 여유 (m) — 양옆 5cm 씩.</summary>
        public const float WidthMargin = 0.1f;

        /// <summary>칸 높이 여유 (m) — 윗판과 닿아 깜빡이지 않게.</summary>
        public const float HeightMargin = 0.02f;

        /// <param name="itemCenter">아이템 루트 기준 상자 가운데</param>
        /// <param name="itemSize">아이템 루트 기준 상자 크기 (루트 크기 포함, 루트 회전 제외)</param>
        /// <param name="rotation">칸 공간에서 아이템 루트의 회전</param>
        /// <param name="position">칸 공간에서 아이템 루트의 위치</param>
        /// <returns>칸에 들어가면 true</returns>
        public static bool TryFit(Vector3 itemCenter, Vector3 itemSize, float width, float height, float depth, out Quaternion rotation, out Vector3 position)
        {
            rotation = Quaternion.identity;
            position = Vector3.zero;

            // 크기 내림차순 (같으면 x · y · z 순서)
            int[] order = { 0, 1, 2 };
            Array.Sort(order, (a, b) => itemSize[a] != itemSize[b] ? itemSize[b].CompareTo(itemSize[a]) : a.CompareTo(b));
            int longAxis = order[0], midAxis = order[1], shortAxis = order[2];

            if (itemSize[longAxis] > width - WidthMargin || itemSize[shortAxis] > height - HeightMargin)
                return false;

            // 아이템 축 → 칸 축 (긴 → 폭, 짧은 → 위, 나머지 → 앞). 거울상이면 앞 대신 뒤
            Vector3[] image = new Vector3[3];
            image[longAxis] = Vector3.right;
            image[shortAxis] = Vector3.up;
            image[midAxis] = Vector3.forward;

            if (Vector3.Dot(Vector3.Cross(image[0], image[1]), image[2]) < 0f)
                image[midAxis] = Vector3.back;

            rotation = FromColumns(image[0], image[1], image[2]);

            float midSize = itemSize[midAxis];
            float zCenter = midSize <= depth ? 0f : -depth * 0.5f + midSize * 0.5f;
            Vector3 target = new Vector3(0f, itemSize[shortAxis] * 0.5f, zCenter);
            position = target - rotation * itemCenter;
            return true;
        }

        /// <summary>열이 각각 아이템 x · y · z 축의 상(像)인 회전 행렬 → 쿼터니언 (직교 · 행렬식 +1 가정).</summary>
        private static Quaternion FromColumns(Vector3 c0, Vector3 c1, Vector3 c2)
        {
            float m00 = c0.x, m10 = c0.y, m20 = c0.z;
            float m01 = c1.x, m11 = c1.y, m21 = c1.z;
            float m02 = c2.x, m12 = c2.y, m22 = c2.z;
            float trace = m00 + m11 + m22;

            if (trace > 0f)
            {
                float s = (float)Math.Sqrt(trace + 1f) * 2f;
                return new Quaternion((m21 - m12) / s, (m02 - m20) / s, (m10 - m01) / s, 0.25f * s);
            }

            if (m00 > m11 && m00 > m22)
            {
                float s = (float)Math.Sqrt(1f + m00 - m11 - m22) * 2f;
                return new Quaternion(0.25f * s, (m01 + m10) / s, (m02 + m20) / s, (m21 - m12) / s);
            }

            if (m11 > m22)
            {
                float s = (float)Math.Sqrt(1f + m11 - m00 - m22) * 2f;
                return new Quaternion((m01 + m10) / s, 0.25f * s, (m12 + m21) / s, (m02 - m20) / s);
            }

            float t = (float)Math.Sqrt(1f + m22 - m00 - m11) * 2f;
            return new Quaternion((m02 + m20) / t, (m12 + m21) / t, 0.25f * t, (m10 - m01) / t);
        }
    }
}
