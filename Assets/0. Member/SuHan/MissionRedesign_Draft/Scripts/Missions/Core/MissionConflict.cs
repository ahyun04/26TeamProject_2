namespace TrustNoOne.Missions
{
    /// <summary>
    /// [역할] 두 미션 정의가 서로 충돌하는지 판단한다.
    ///
    /// [기획서 근거] "개인 행동 목표는 개인 미션과 충돌해서는 안 된다.
    ///   개인 미션: 아이템 전달 / 개인 행동 목표: 누구에게도 아이템을 주지 말아라 → 충돌 발생"
    ///
    /// [규칙] a 가 금지하는 행동을 b 가 요구하거나, 그 반대이면 충돌이다.
    ///   (Requires/Forbids 는 MissionDefinition 이 Kind 로부터 자동 유도 + 수동 보강한 값)
    /// </summary>
    public static class MissionConflict
    {
        public static bool AreInConflict(MissionDefinition a, MissionDefinition b)
        {
            if (a == null || b == null)
                return false;

            return Forbids(a, b) || Forbids(b, a);
        }

        private static bool Forbids(MissionDefinition forbidder, MissionDefinition requirer)
        {
            foreach (MissionEventType forbidden in forbidder.EnumerateForbids())
            {
                if (requirer.RequiresEvent(forbidden))
                    return true;
            }

            return false;
        }
    }
}
