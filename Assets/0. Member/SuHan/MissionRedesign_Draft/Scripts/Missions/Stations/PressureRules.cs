namespace TrustNoOne.Missions
{
    /// <summary>피스톤 하나의 상태 (표시등 색과 판정에 쓴다). 2d 명세 PR11.</summary>
    public enum PistonState
    {
        Moving = 0,
        Stalled = 1,
        Locked = 2,
    }

    /// <summary>
    /// [역할] 압력 미니게임 규칙 (순수 C#). PressureStation 과 dotnet 테스트가 쓴다. 2d 명세 3-2.
    ///
    /// [높이 공식 — PR3] 높이(t) = PingPong((시계 − 누적 정지 시간) × 속도 + 위상) — 0 ↔ 1 을 오르내리는 삼각파.
    ///  모든 PC 가 같은 공식 · 같은 값으로 계산하므로 매 틱 높이를 보낼 필요가 없고, 과거 시각의 높이도 정확히 다시 구할 수 있다(지연 보정 판정).
    ///  멈춰 있으면 시계가 멈춘 시각에서 멈춘다. 재개할 때 누적 정지 시간에 멈춘 만큼 더해 멈춘 높이에서 이어진다(ResumeOffset).
    /// [PingPong 을 직접 구현한 이유] UnityEngine.Mathf.PingPong 은 Unity 밖(dotnet 테스트)에서 호출할 수 없다.
    /// [멈춤 판단 — PR6] 재개 이전 시각(t < resumedAt)도 "멈춰 있던 때"로 본다: 멈춘 동안 누른 요청이 재개 뒤에 도착하면
    ///  바뀐 공식 값으로 과거를 계산해 엉뚱한 판정이 나오므로 무시한다.
    /// [누른 시각 인정 범위 — PR4] 지금 기준 [−maxRewind, +maxAhead] 밖이면 지금 시각으로 판정한다 (시각 조작 방지).
    /// </summary>
    public static class PressureRules
    {
        public const float NotStalled = -1f;

        /// <summary>0 → 1 → 0 을 반복하는 삼각파 (주기 2).</summary>
        public static float PingPong01(float x)
        {
            float m = x % 2f;

            if (m < 0f)
                m += 2f;

            return m <= 1f ? m : 2f - m;
        }

        public static float HeightAt(float t, float speed, float phase, float timeOffset, float stallStart)
        {
            float clock = stallStart >= 0f && t >= stallStart ? stallStart : t;
            return PingPong01((clock - timeOffset) * speed + phase);
        }

        public static bool IsStalledAt(float t, float stallStart, float resumedAt)
        {
            return (stallStart >= 0f && t >= stallStart) || t < resumedAt;
        }

        public static float ResumeOffset(float timeOffset, float stallStart, float resumeTime)
        {
            return timeOffset + (resumeTime - stallStart);
        }

        public static bool InZone(float height, float zoneMin, float zoneMax)
        {
            return height >= zoneMin && height <= zoneMax;
        }

        public static float ClampPressTime(float pressTime, float now, float maxRewind, float maxAhead)
        {
            if (float.IsNaN(pressTime) || pressTime < now - maxRewind || pressTime > now + maxAhead)
                return now;

            return pressTime;
        }

        /// <param name="map">덮어쓸 연결 (버튼 → 피스톤). 0..n-1 순열이 된다</param>
        /// <param name="randomRange">randomRange(n) → 0 이상 n 미만의 정수</param>
        public static void ShuffleMapping(int[] map, System.Func<int, int> randomRange)
        {
            if (map == null || randomRange == null)
                return;

            for (int i = 0; i < map.Length; i++)
                map[i] = i;

            for (int i = map.Length - 1; i > 0; i--)
            {
                int j = randomRange(i + 1);
                int temp = map[i];
                map[i] = map[j];
                map[j] = temp;
            }
        }
    }
}
