namespace TrustNoOne.Missions
{
    /// <summary>
    /// [역할] 차단기 레버 규칙 (순수 C#): 시작 상태 섞기, 전부 켜졌는지 판정. BreakerStation 이 쓴다.
    ///
    /// [섞기 규칙] 레버마다 50% 로 켜짐/꺼짐. 우연히 전부 켜짐이 나오면 무작위 한 칸을 끈다 → 항상 할 일이 남는다.
    ///  근거: 2a 명세 결정 P2 (옛 코드는 전부 꺼짐으로 시작 → 매번 같은 6번 클릭이라 단조로움).
    /// [난수를 함수로 받는 이유] Unity Random 은 Unity 밖(dotnet 테스트)에서 쓸 수 없고, 테스트에서 결정적인 값을 넣어야 해서.
    /// [레버 0개면 AllOn = false] 레버가 하나도 연결되지 않은 잘못된 프리팹이 첫 클릭에 바로 완료되는 것을 막는다.
    /// </summary>
    public static class BreakerRules
    {
        /// <param name="states">덮어쓸 레버 상태 (true = 켜짐)</param>
        /// <param name="randomRange">randomRange(n) → 0 이상 n 미만의 정수</param>
        public static void Shuffle(bool[] states, System.Func<int, int> randomRange)
        {
            if (states == null || states.Length == 0 || randomRange == null)
                return;

            for (int i = 0; i < states.Length; i++)
                states[i] = randomRange(2) == 1;

            if (AllOn(states))
                states[randomRange(states.Length)] = false;
        }

        public static bool AllOn(bool[] states)
        {
            if (states == null || states.Length == 0)
                return false;

            foreach (bool isOn in states)
            {
                if (!isOn)
                    return false;
            }

            return true;
        }
    }
}
