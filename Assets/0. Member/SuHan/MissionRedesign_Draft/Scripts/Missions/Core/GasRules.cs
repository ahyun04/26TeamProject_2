namespace TrustNoOne.Missions
{
    /// <summary>
    /// [역할] 누수 가스 피해의 판정 규칙 (엔진 없음 — dotnet 테스트 T47). 배관 장치(PipeLeakStation)가 호스트에서 1초마다 쓴다.
    ///  - InGas: 새는 곳에서 수평 거리 radius 안이고 높이 차가 maxHeightDiff 안인가
    ///  - DamageAboveFloor: 줄일 체력 — 최저 체력 아래로는 내려가지 않게 (최저 이하면 0 → 피해 · 피격 알림 없음)
    ///  - CanHitAgain: 한 사람이 다시 맞을 때가 됐나 (여러 배관에 걸려도 간격마다 한 번)
    /// [이유] 기획서에 없는 기능이라 숫자가 바뀔 가능성이 크다 → 숫자 규칙을 한 곳에 모으고 엔진 밖에서 시험한다 (작업 규칙).
    /// [근거] 14a 명세 GD2 (수평 1.2m — 사용자 결정 A), GD4 (5 · 최저 10 — 사용자 결정 B), GD7 (높이 차 2m), GD8 (1초에 한 번), GD13
    /// </summary>
    public static class GasRules
    {
        /// <summary>가스 속인가. dx · dy · dz = 플레이어 위치 − 새는 곳 위치.</summary>
        public static bool InGas(float dx, float dy, float dz, float radius, float maxHeightDiff)
        {
            if (dy > maxHeightDiff || dy < -maxHeightDiff)
                return false;

            return dx * dx + dz * dz <= radius * radius;
        }

        /// <summary>이번에 줄일 체력: min(amount, 현재 − 최저). 현재가 최저 이하면 0 (GD4 — 가스로는 죽지 않음).</summary>
        public static float DamageAboveFloor(float currentHealth, float amount, float minHealth)
        {
            if (amount <= 0f || currentHealth <= minHealth)
                return 0f;

            float room = currentHealth - minHealth;
            return amount < room ? amount : room;
        }

        /// <summary>
        /// 다시 맞을 때가 됐나 (GD8): 처음이거나, 마지막 피해에서 interval 이 지났거나, 시간이 되돌아갔으면(새 판 — 네트워크 시각이 처음부터) true.
        /// </summary>
        public static bool CanHitAgain(bool hasHit, float lastHitTime, float now, float interval)
        {
            if (!hasHit || now < lastHitTime)
                return true;

            // 틱 시각의 작은 오차로 1초 간격이 한 틱 밀리지 않게
            return now - lastHitTime >= interval - 0.0001f;
        }
    }
}
