using System.Collections.Generic;

namespace TrustNoOne.Missions
{
    /// <summary>
    /// [역할] 배관 누수 차단(TG012)의 판정 규칙 (엔진 없음 — dotnet 테스트 T45). 배관 한 개의 "새는 곳"과 "막힌 곳"을 6칸 비트 묶음으로 다룬다.
    ///  - PickMask: 새는 곳 뽑기 — 개수 1 ~ 3 (테스트용 고정 개수가 있으면 그 개수), 후보 6곳 중 서로 다른 자리
    ///  - IsLeaking: 이 자리를 지금 막을 수 있나 (새는 중 · 아직 안 막힘)
    ///  - Seal · Remaining · AllSealed · Count: 막기 · 남은 수 · 다 막혔나
    /// [이유] 장치(PipeLeakStation)는 Fusion 상태 · 표시만 맡고, 숫자 규칙은 엔진 밖에서 시험한다 (작업 규칙). 무작위 값은 밖에서 받아 결과가 정해진다.
    /// [근거] 13a 명세 PL5 (기획서 "배관마다 가스 새는 곳은 랜덤 (1~3개)" · 사용자 결정 A — 앞쪽 후보 6곳), PL11 · PL15 · PL27 · PL28
    /// </summary>
    public static class PipeLeakRules
    {
        /// <summary>후보 자리 수 (PL16 — 아래 곧은 구간 3 · 위 곧은 구간 3).</summary>
        public const int SpotCount = 6;

        /// <summary>새는 곳 개수 범위 — 기획서 "랜덤 (1~3개)".</summary>
        public const int MinLeaks = 1;
        public const int MaxLeaks = 3;

        /// <summary>
        /// 새는 곳 비트 묶음을 뽑는다.
        /// </summary>
        /// <param name="fixedCount">1 ~ 3 이면 그 개수로 고정 (테스트 설정 칸 — PL27), 그 밖(0)이면 countRoll 로 정한다</param>
        /// <param name="countRoll">0 ~ 1 무작위 값 → 1 · 2 · 3 을 같은 확률로</param>
        /// <param name="orderRolls">자리마다 0 ~ 1 무작위 값 — 값이 작은 자리부터 고른다 (서로 다른 자리가 보장된다)</param>
        public static int PickMask(int fixedCount, float countRoll, IReadOnlyList<float> orderRolls)
        {
            int count = fixedCount >= MinLeaks && fixedCount <= MaxLeaks
                ? fixedCount
                : MinLeaks + Clamp((int)(countRoll * (MaxLeaks - MinLeaks + 1)), 0, MaxLeaks - MinLeaks);

            int spots = orderRolls != null ? System.Math.Min(orderRolls.Count, SpotCount) : 0;
            List<int> order = new List<int>(spots);

            for (int i = 0; i < spots; i++)
                order.Add(i);

            // 값이 같으면 번호가 작은 자리 먼저 (정렬 결과가 늘 같게)
            order.Sort((a, b) => orderRolls[a] != orderRolls[b] ? orderRolls[a].CompareTo(orderRolls[b]) : a.CompareTo(b));

            int mask = 0;

            for (int i = 0; i < count && i < order.Count; i++)
                mask |= 1 << order[i];

            return mask;
        }

        /// <summary>이 자리가 지금 새는 중인가 (= 막을 수 있나). 범위 밖이면 false.</summary>
        public static bool IsLeaking(int leakMask, int sealedMask, int spot)
        {
            if (spot < 0 || spot >= SpotCount)
                return false;

            int bit = 1 << spot;
            return (leakMask & bit) != 0 && (sealedMask & bit) == 0;
        }

        /// <summary>그 자리를 막은 비트 묶음. 범위 밖이면 그대로.</summary>
        public static int Seal(int sealedMask, int spot)
        {
            return spot >= 0 && spot < SpotCount ? sealedMask | (1 << spot) : sealedMask;
        }

        /// <summary>아직 새는 곳 수.</summary>
        public static int Remaining(int leakMask, int sealedMask)
        {
            return Count(leakMask & ~sealedMask);
        }

        /// <summary>새는 곳이 모두 막혔나. 새는 곳이 하나도 없으면 false — 뽑기 전 상태가 완료로 보이지 않게.</summary>
        public static bool AllSealed(int leakMask, int sealedMask)
        {
            return (leakMask & AllSpots) != 0 && Remaining(leakMask, sealedMask) == 0;
        }

        /// <summary>비트 묶음 안 자리 수 (6칸만 센다).</summary>
        public static int Count(int mask)
        {
            int count = 0;

            for (int i = 0; i < SpotCount; i++)
            {
                if ((mask & (1 << i)) != 0)
                    count++;
            }

            return count;
        }

        private const int AllSpots = (1 << SpotCount) - 1;

        private static int Clamp(int value, int lo, int hi) => value < lo ? lo : value > hi ? hi : value;
    }
}
