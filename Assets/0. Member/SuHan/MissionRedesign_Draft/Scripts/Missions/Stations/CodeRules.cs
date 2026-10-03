using System;

namespace TrustNoOne.Missions
{
    /// <summary>
    /// [역할] 코드 순서 맞추기 규칙 (순수 C#). CodeStation 과 dotnet 테스트가 쓴다. 3a 명세 3-1.
    ///  숫자는 0 ~ 8 로 다룬다 (버튼 1 ~ 9 = 부품 번호 0 ~ 8).
    ///
    /// [점등 공식 — C3] 빨간 버튼을 누른 시각(showStart)부터 leadIn 만큼 쉬고, 숫자마다 onTime 켜짐 + gapTime 꺼짐.
    ///  마지막 숫자 뒤에는 꺼짐이 없다 — 마지막 숫자가 꺼지는 순간(ShowEnd)부터 입력을 받는다 (기획서 "모두 점등되면 불이 꺼지고 입력 대기").
    ///  모든 PC 가 같은 공식 · 같은 값으로 계산하므로 "지금 켜진 숫자"를 동기화할 필요가 없다.
    /// [판정 — C1] 입력 4개가 점등 순서와 순서까지 같아야 성공. 4개 미만 · 순서 틀림 · 점등 안 된 숫자는 모두 실패 (기획서 실패 조건 세 가지).
    /// [뽑기 — C4] 서로 다른 숫자 4개 (기획서 예시 3 → 8 → 1 → 6).
    /// </summary>
    public static class CodeRules
    {
        public const int SequenceLength = 4;
        public const int DigitCount = 9;

        /// <summary>"시작 안 함 / 없음"을 뜻하는 시각 값 (점등 시작 · 빨간불 시작).</summary>
        public const float NotStarted = -1f;

        /// <summary>buffer 에 0 ~ 8 중 서로 다른 숫자를 채운다 (부분 셔플).</summary>
        /// <param name="randomBelow">randomBelow(n) → 0 이상 n 미만의 정수</param>
        public static void DrawSequence(int[] buffer, Func<int, int> randomBelow)
        {
            if (buffer == null || randomBelow == null)
                return;

            int[] pool = new int[DigitCount];

            for (int i = 0; i < DigitCount; i++)
                pool[i] = i;

            int count = Math.Min(buffer.Length, DigitCount);

            for (int i = 0; i < count; i++)
            {
                int j = i + randomBelow(DigitCount - i);
                int temp = pool[i];
                pool[i] = pool[j];
                pool[j] = temp;
                buffer[i] = pool[i];
            }
        }

        /// <summary>마지막 숫자가 꺼지는 시각 = 입력을 받기 시작하는 시각.</summary>
        public static float ShowEnd(float showStart, float leadIn, float onTime, float gapTime)
        {
            return showStart + leadIn + SequenceLength * onTime + (SequenceLength - 1) * gapTime;
        }

        /// <summary>그 시각에 켜진 순서 번호(0 ~ 3). 쉼 · 꺼짐 구간 · 끝난 뒤 · 대기면 −1.</summary>
        public static int LitIndexAt(float t, float showStart, float leadIn, float onTime, float gapTime)
        {
            float period = onTime + gapTime;

            if (showStart < 0f || onTime <= 0f || period <= 0f)
                return -1;

            float local = t - showStart - leadIn;

            if (local < 0f)
                return -1;

            int slot = (int)Math.Floor(local / period);

            if (slot >= SequenceLength)
                return -1;

            return local - slot * period < onTime ? slot : -1;
        }

        public static bool IsInputOpen(float t, float showStart, float leadIn, float onTime, float gapTime)
        {
            return showStart >= 0f && t >= ShowEnd(showStart, leadIn, onTime, gapTime);
        }

        public static bool IsFailFlash(float t, float failStart, float duration)
        {
            return failStart >= 0f && t >= failStart && t < failStart + duration;
        }

        public static bool Matches(int[] sequence, int[] entered, int enteredCount)
        {
            if (sequence == null || entered == null || enteredCount != SequenceLength ||
                sequence.Length < SequenceLength || entered.Length < SequenceLength)
                return false;

            for (int i = 0; i < SequenceLength; i++)
            {
                if (sequence[i] != entered[i])
                    return false;
            }

            return true;
        }
    }
}
