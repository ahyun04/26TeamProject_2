namespace TrustNoOne.Missions
{
    /// <summary>전선 색. 값은 옛 WireColor 와 같다 → 변환기가 숫자 그대로 옮긴다 (2b 명세 W5).</summary>
    public enum WiringColor
    {
        Red = 0,
        Blue = 1,
        Green = 2,
        Yellow = 3,
    }

    /// <summary>
    /// [역할] 전선 연결 규칙 (순수 C#). WiringStation 이 쓴다. 2b 명세 2-6.
    ///  - 색 섞기: 시작점 네 개에 네 색을 한 번씩 무작위로 (옛 WiringMission.InitializeWires 와 같은 Fisher–Yates)
    ///  - 연결 가능: 번호 범위 안 / 시작점이 아직 안 이어짐 / 도착점이 비어 있음 / 두 색이 같음 (기획서 "같은 색 선끼리 연결")
    ///  - 전부 연결: 레버를 당길 수 있는 조건 (결정 W1)
    ///
    /// [이름을 WiringColor 로 한 이유] 옛 WireColor 는 전역 이름이라 같은 이름이면 전역 네임스페이스의 변환기에서 옛 타입이 잡힌다 (W5).
    /// [난수를 함수로 받는 이유] Unity 밖(dotnet 테스트)에서 결정적인 값을 넣기 위해 (BreakerRules 와 같음).
    /// </summary>
    public static class WiringRules
    {
        public const int WireCount = 4;
        public const int NotConnected = -1;

        /// <param name="startColors">덮어쓸 시작점 색 (WiringColor 값)</param>
        /// <param name="randomRange">randomRange(n) → 0 이상 n 미만의 정수</param>
        public static void ShuffleColors(int[] startColors, System.Func<int, int> randomRange)
        {
            if (startColors == null || randomRange == null)
                return;

            for (int i = 0; i < startColors.Length; i++)
                startColors[i] = i;

            for (int i = startColors.Length - 1; i > 0; i--)
            {
                int j = randomRange(i + 1);
                int temp = startColors[i];
                startColors[i] = startColors[j];
                startColors[j] = temp;
            }
        }

        public static bool CanConnect(int[] startColors, int[] connectedEnds, int[] endColors, int start, int end)
        {
            if (startColors == null || connectedEnds == null || endColors == null)
                return false;

            if (start < 0 || start >= startColors.Length || start >= connectedEnds.Length)
                return false;

            if (end < 0 || end >= endColors.Length)
                return false;

            if (connectedEnds[start] != NotConnected)
                return false;

            for (int i = 0; i < connectedEnds.Length; i++)
            {
                if (connectedEnds[i] == end)
                    return false;
            }

            return startColors[start] == endColors[end];
        }

        public static bool AllConnected(int[] connectedEnds)
        {
            if (connectedEnds == null || connectedEnds.Length == 0)
                return false;

            foreach (int end in connectedEnds)
            {
                if (end == NotConnected)
                    return false;
            }

            return true;
        }
    }
}
