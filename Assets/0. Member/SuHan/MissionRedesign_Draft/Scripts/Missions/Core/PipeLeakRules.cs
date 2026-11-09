namespace TrustNoOne.Missions
{
    /// <summary>
    /// [역할] 배관 누수 차단(TG012)의 판정 규칙 (엔진 없음 — dotnet 테스트 T45). 배관 한 개의 "새는 곳"과 "막힌 곳"을 6칸 비트 묶음으로 다룬다.
    ///  - 6칸 = 받은 배관 모델 3종의 구멍: 자리 1 = 1번 모델(구멍 1개) · 자리 2 · 3 = 2번 모델 · 자리 4 · 5 · 6 = 3번 모델 (15 명세 1-2)
    ///  - PickMask: 새는 곳 뽑기 — 개수 1 ~ 3 (테스트용 고정 개수가 있으면 그 개수) → 그 개수 모델의 자리 묶음 (GroupMask)
    ///  - ModelIndex: 켤 배관 모델 번호 = 새는 곳 비트 수 (0 = 구멍 없는 모델 — 미션 없는 판 · 배정 전) / ModelOf: 자리가 속한 모델 (변환 도구가 씀)
    ///  - IsLeaking: 이 자리를 지금 막을 수 있나 (새는 중 · 아직 안 막힘)
    ///  - Seal · Remaining · AllSealed · Count: 막기 · 남은 수 · 다 막혔나
    /// [이유] 장치(PipeLeakStation)는 Fusion 상태 · 표시만 맡고, 숫자 규칙은 엔진 밖에서 시험한다 (작업 규칙). 무작위 값은 밖에서 받아 결과가 정해진다.
    ///  모델 번호를 새는 곳 비트 수에서 바로 얻으므로 새로 동기화할 값이 없다 (15 접근법 1 — 늦게 들어온 사람도 같은 모델).
    /// [근거] 13a 명세 PL11 · PL15 · PL27 · PL28, 15 명세 NM2 · NM6 · NM17 (기획서 "배관마다 가스 새는 곳은 랜덤 (1~3개)" · 그림 "구멍 1 · 2 · 3개 배관" — 사용자 결정 A, 질문 2)
    /// </summary>
    public static class PipeLeakRules
    {
        /// <summary>자리 수 (15 명세 1-2 — 1번 모델 1 · 2번 모델 2 · 3번 모델 3).</summary>
        public const int SpotCount = 6;

        /// <summary>배관 모델 수 — 0번(구멍 없음) ~ 3번(구멍 3개).</summary>
        public const int ModelCount = 4;

        /// <summary>새는 곳 개수 범위 — 기획서 "랜덤 (1~3개)".</summary>
        public const int MinLeaks = 1;
        public const int MaxLeaks = 3;

        /// <summary>
        /// 새는 곳 비트 묶음을 뽑는다.
        /// </summary>
        /// <param name="fixedCount">1 ~ 3 이면 그 개수로 고정 (테스트 설정 칸 — PL27), 그 밖(0)이면 countRoll 로 정한다</param>
        /// <param name="countRoll">0 ~ 1 무작위 값 → 1 · 2 · 3 을 같은 확률로 (범위 밖 값은 끝 값으로)</param>
        public static int PickMask(int fixedCount, float countRoll)
        {
            int count = fixedCount >= MinLeaks && fixedCount <= MaxLeaks
                ? fixedCount
                : MinLeaks + Clamp((int)(countRoll * (MaxLeaks - MinLeaks + 1)), 0, MaxLeaks - MinLeaks);

            return GroupMask(count);
        }

        /// <summary>그 개수 모델의 자리 묶음: 1 → 자리 1 · 2 → 자리 2 · 3 · 3 → 자리 4 · 5 · 6 (NM2). 그 밖은 0.</summary>
        public static int GroupMask(int count)
        {
            if (count < MinLeaks || count > MaxLeaks)
                return 0;

            // 앞 모델들의 자리 수 합 (0 · 1 · 3) 만큼 밀고, 그 모델의 자리 수(= 개수)만큼 켠다
            int first = count * (count - 1) / 2;
            return ((1 << count) - 1) << first;
        }

        /// <summary>켤 배관 모델 번호 = 새는 곳 비트 수 (0 ~ 3 — 0 은 구멍 없는 모델). 비트 묶음이 늘 한 모델의 자리 묶음이라 수 = 번호 (NM6).</summary>
        public static int ModelIndex(int leakMask)
        {
            return Count(leakMask);
        }

        /// <summary>자리가 속한 모델 번호: 자리 0 → 1번, 1 · 2 → 2번, 3 ~ 5 → 3번. 범위 밖이면 0.</summary>
        public static int ModelOf(int spot)
        {
            if (spot < 0 || spot >= SpotCount)
                return 0;

            return spot < 1 ? 1 : spot < 3 ? 2 : 3;
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
