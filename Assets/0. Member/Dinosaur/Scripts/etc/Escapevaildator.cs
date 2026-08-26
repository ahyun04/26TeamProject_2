using LockdownProtocol.Networking;

/// <summary>탈출 요청이 왜 거부됐는지를 나타낸다. UI/로그에서 실패 사유를 보여줄 때 쓴다.</summary>
public enum EscapeValidationResult
{
    Success,
    NotCitizen,      // 시민이 아님 (살인마)
    NotAlive,        // 사망 또는 이미 탈출함
    GateNotOpen,     // 탈출구가 아직 개방되지 않음
    NoCapacity       // 탈출 가능 인원이 남아있지 않음
}

/// <summary>
/// 탈출 가능 조건 5가지 중 "탈출 구역 진입"을 제외한 4가지를 검증한다.
/// (구역 진입 여부는 물리적 트리거 판정이라 호출하는 쪽(EscapeManager)이 이미 확인한 뒤에
/// 이 함수를 부르는 것을 전제로 한다.)
///
/// 상태를 갖지 않는 순수 로직이라 static class로 만들었다 - 씬에 배치할 필요도,
/// FindObjectOfType으로 찾을 필요도 없다.
/// </summary>
public static class EscapeValidator
{
    public static EscapeValidationResult Validate(
        PlayerHealth health,
        KillManager killManager,
        bool isGateOpen,
        EscapeCapacityManager capacity)
    {
        // 1. 역할 확인 - 시민만 가능
        if (killManager != null && killManager.IsMurderer)
        {
            return EscapeValidationResult.NotCitizen;
        }

        // 2. 생존 상태 확인
        if (health.IsDead || health.IsEscaped)
        {
            return EscapeValidationResult.NotAlive;
        }

        // 3. 탈출구 개방 확인
        if (!isGateOpen)
        {
            return EscapeValidationResult.GateNotOpen;
        }

        // 4. 탈출 가능 인원 확인
        if (capacity == null || capacity.EscapeAvailable <= 0)
        {
            return EscapeValidationResult.NoCapacity;
        }

        return EscapeValidationResult.Success;
    }
}