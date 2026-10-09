using System;

namespace TrustNoOne.Missions
{
    /// <summary>
    /// [역할] 고장난 장비 조립(TG003)의 계산 규칙. 엔진 없이 테스트한다 (T21). 3c 명세 3-1.
    ///  - 배치: 무작위 1색은 장비 주변 묶음, 나머지 색은 맵 묶음의 서로 다른 자리 (EA4 — 기획서 "주변에 흩어져" · "맵 곳곳" 둘 다)
    ///  - 조립 위치 안: 수평 거리만 본다 (바닥 원이라 높이 차이는 무시)
    ///  - 시각 공식: 자리별 붙은 시각만 동기화하고, 날아가기 진행은 각 PC 가 그 시각으로 계산한다 (EA12, 3a · 3b 와 같은 방식)
    /// [이유] 판정 규칙을 엔진 · 네트워크와 떼어 두면 숫자를 바꿔도 테스트로 바로 확인할 수 있다.
    /// </summary>
    public static class AssemblyRules
    {
        /// <summary>부품 수 = 장비 자리 수. 색 번호 0 파랑 · 1 회색 · 2 빨강 · 3 노랑.</summary>
        public const int PartCount = 4;

        /// <summary>붙은 시각이 "없음"(빈 자리)일 때.</summary>
        public const float NotAttached = -1f;

        /// <summary>
        /// 색마다(isNear · spot 의 길이만큼) 놓을 묶음과 자리 번호를 채운다. 놓지 못한 색은 spot = −1.
        /// 주변 후보가 있으면 무작위 1색을 주변의 무작위 자리에, 나머지 색은 맵의 서로 다른 자리에.
        /// 맵 후보가 모자라면 남은 색은 주변의 남은 자리로 (그래도 모자라면 놓지 않는다).
        /// </summary>
        /// <param name="randomBelow">0 이상 n 미만 정수 (테스트에서 고정값 주입)</param>
        /// <returns>놓은 색 수</returns>
        public static int PickPlacement(int nearCount, int farCount, bool[] isNear, int[] spot, Func<int, int> randomBelow)
        {
            int colors = Math.Min(isNear.Length, spot.Length);

            for (int c = 0; c < colors; c++)
            {
                isNear[c] = false;
                spot[c] = -1;
            }

            // 묶음마다 서로 다른 자리 순서 (생명 유지 장치와 같은 "서로 다른 자리 고르기")
            int[] nearOrder = new int[colors];
            int nearAvailable = LifeSupportRules.PickSpots(nearCount, nearOrder, randomBelow);
            int[] farOrder = new int[colors];
            int farAvailable = LifeSupportRules.PickSpots(farCount, farOrder, randomBelow);
            int nearUsed = 0;
            int farUsed = 0;
            int placed = 0;

            // 장비 주변에 놓을 색 하나 (장비에 오면 무엇을 찾을지 보이게)
            int nearColor = nearAvailable > 0 && colors > 0 ? randomBelow(colors) : -1;

            if (nearColor >= 0)
            {
                isNear[nearColor] = true;
                spot[nearColor] = nearOrder[nearUsed++];
                placed++;
            }

            for (int c = 0; c < colors; c++)
            {
                if (c == nearColor)
                    continue;

                if (farUsed < farAvailable)
                {
                    spot[c] = farOrder[farUsed++];
                    placed++;
                }
                else if (nearUsed < nearAvailable)
                {
                    isNear[c] = true;
                    spot[c] = nearOrder[nearUsed++];
                    placed++;
                }
            }

            return placed;
        }

        /// <summary>조립 위치 원 안인가 (수평 거리 ≤ 반경, 경계 포함).</summary>
        public static bool InZone(float dx, float dz, float radius)
        {
            return dx * dx + dz * dz <= radius * radius;
        }

        /// <summary>날아가기 진행 0 ~ 1. 빈 자리면 −1, 붙은 시각 이전 · 같음 0, duration 뒤 1 (duration ≤ 0 이면 바로 1).</summary>
        public static float FlyProgress(float t, float attachTime, float duration)
        {
            if (attachTime < 0f)
                return -1f;

            if (duration <= 0f)
                return 1f;

            float progress = (t - attachTime) / duration;
            return progress < 0f ? 0f : (progress > 1f ? 1f : progress);
        }

        /// <summary>붙은 자리 수 (붙은 시각이 0 이상).</summary>
        public static int CountAttached(float[] attachTimes)
        {
            int count = 0;

            foreach (float attachTime in attachTimes)
            {
                if (attachTime >= 0f)
                    count++;
            }

            return count;
        }
    }
}
