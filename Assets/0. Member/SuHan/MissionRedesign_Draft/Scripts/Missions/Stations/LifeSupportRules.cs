using System;

namespace TrustNoOne.Missions
{
    /// <summary>
    /// [역할] 생명 유지 장치(TG004 · 산소통 선별)의 계산 규칙. 엔진 없이 테스트한다 (T19). 3b 명세 3-1.
    ///  - 배치: 자리 후보 중 서로 다른 자리 고르기, 정상 / 위험 섞기 — 정상 개수는 정확히 (LS2 · LS7)
    ///  - 시각 공식: 넣은 시각 · 폭발 시각만 동기화하고, 카운트다운 숫자 · 램프 깜빡임 · 폭발 섬광은 각 PC 가 그 시각으로 계산한다 (LS14, 3a C3 와 같은 방식)
    ///  - 폭발: 반경 안 여부와 피해량 (LS5)
    /// [이유] 판정 규칙을 엔진 · 네트워크와 떼어 두면 숫자를 바꿔도 테스트로 바로 확인할 수 있다.
    /// </summary>
    public static class LifeSupportRules
    {
        /// <summary>시각 값이 "없음"(판정 중 아님 · 폭발 없음)일 때.</summary>
        public const float NotStarted = -1f;

        /// <summary>
        /// 자리 후보 0 ~ candidateCount−1 중 서로 다른 번호를 buffer 에 채운다 (앞에서부터 섞어 뽑기). 후보가 모자라면 후보 수만큼만 채운다.
        /// </summary>
        /// <param name="randomBelow">0 이상 n 미만 정수 (테스트에서 고정값 주입)</param>
        /// <returns>채운 개수</returns>
        public static int PickSpots(int candidateCount, int[] buffer, Func<int, int> randomBelow)
        {
            int total = Math.Max(candidateCount, 0);
            int count = Math.Min(total, buffer.Length);
            int[] pool = new int[total];

            for (int i = 0; i < total; i++)
                pool[i] = i;

            for (int i = 0; i < count; i++)
            {
                int j = i + randomBelow(total - i);
                (pool[i], pool[j]) = (pool[j], pool[i]);
                buffer[i] = pool[i];
            }

            return count;
        }

        /// <summary>
        /// dangerous 중 정확히 safeCount 개(0 ~ 길이로 제한)를 false(정상), 나머지를 true(위험)로 하고 순서를 섞는다 (LS2).
        /// 기획서 "산소통마다 정상 또는 위험 상태가 무작위" — 다만 필요한 공급 수만큼의 정상 통은 반드시 있어야 미션을 끝낼 수 있다.
        /// </summary>
        public static void DrawDangers(bool[] dangerous, int safeCount, Func<int, int> randomBelow)
        {
            int safe = Math.Min(Math.Max(safeCount, 0), dangerous.Length);

            for (int i = 0; i < dangerous.Length; i++)
                dangerous[i] = i >= safe;

            for (int i = dangerous.Length - 1; i > 0; i--)
            {
                int j = randomBelow(i + 1);
                (dangerous[i], dangerous[j]) = (dangerous[j], dangerous[i]);
            }
        }

        /// <summary>넣은 뒤 판정 전(duration 초 동안)인가.</summary>
        public static bool IsCounting(float t, float insertStart, float duration)
        {
            return insertStart >= 0f && t >= insertStart && t < insertStart + duration;
        }

        /// <summary>판정 중이면 화면에 띄울 남은 초(올림 — 5 → 1), 아니면 0.</summary>
        public static int CountdownSeconds(float t, float insertStart, float duration)
        {
            if (!IsCounting(t, insertStart, duration))
                return 0;

            return Math.Max(1, (int)Math.Ceiling((double)(insertStart + duration - t)));
        }

        /// <summary>판정할 시각이 되었는가 (기획서 "넣은후 5초후").</summary>
        public static bool IsResolved(float t, float insertStart, float duration)
        {
            return insertStart >= 0f && t >= insertStart + duration;
        }

        /// <summary>start 부터 duration 초 동안인가 (폭발 뒤 DAMAGED 화면 등). start 가 없으면(−1) false.</summary>
        public static bool IsShowing(float t, float start, float duration)
        {
            return start >= 0f && t >= start && t < start + duration;
        }

        /// <summary>start 부터 period 초마다 켜짐 · 꺼짐을 반복한다. 첫 구간은 켜짐. start 가 없거나 그 이전이면 꺼짐.</summary>
        public static bool IsBlinkOn(float t, float start, float period)
        {
            if (start < 0f || t < start || period <= 0f)
                return false;

            return (int)Math.Floor((t - start) / period) % 2 == 0;
        }

        /// <summary>폭발 섬광 진행 0 ~ 1 (커지는 비율). 구간 밖이면 −1.</summary>
        public static float FlashProgress(float t, float start, float duration)
        {
            if (duration <= 0f || !IsShowing(t, start, duration))
                return -1f;

            return (t - start) / duration;
        }

        /// <summary>폭발 반경 안인가 (경계 포함).</summary>
        public static bool InBlast(float distance, float radius)
        {
            return distance <= radius;
        }

        /// <summary>폭발 피해 = 최대 체력 × 비율 (LS5). 음수는 0.</summary>
        public static float BlastDamage(float maxHealth, float ratio)
        {
            return Math.Max(0f, maxHealth * ratio);
        }
    }
}
