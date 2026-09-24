namespace TrustNoOne.Missions
{
    /// <summary>
    /// [역할] 필터 청소 규칙 (순수 C#). FilterStation 이 쓴다. 2c 명세 3-3.
    ///  - 흡입 시작 가능: 번호 범위 안 / 아직 안 치운 먼지 / 지금 빨아들이는 먼지가 없음 (한 번에 하나 — 결정 F3)
    ///  - 전부 청소: 완료 조건 (기획서 "모두 제거 시 완료")
    /// [먼지 0개면 AllCleaned = false] 먼지가 연결되지 않은 잘못된 프리팹이 즉시 완료되는 것을 막는다 (BreakerRules · WiringRules 와 같음).
    /// </summary>
    public static class FilterRules
    {
        public const int NoDust = -1;

        public static bool CanStartSuction(bool[] cleaned, int suckingIndex, int index)
        {
            if (cleaned == null || index < 0 || index >= cleaned.Length)
                return false;

            if (suckingIndex != NoDust)
                return false;

            return !cleaned[index];
        }

        public static bool AllCleaned(bool[] cleaned)
        {
            if (cleaned == null || cleaned.Length == 0)
                return false;

            foreach (bool done in cleaned)
            {
                if (!done)
                    return false;
            }

            return true;
        }
    }
}
