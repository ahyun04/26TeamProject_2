using System.Collections.Generic;
using Fusion;

/// <summary>
/// [테스트 전용 · 순수 규칙] 멀티 테스트에서 호스트가 미션을 배정할 때 시민 · 범인 목록을 나눈다.
///
/// [역할] 호스트와 접속한 사람 목록을 받아 MissionManager.InitializeMissions(시민, 범인)에 넘길 두 목록을 만든다.
///  - 호스트는 항상 시민
///  - F5(firstGuestIsKiller = false): 손님 전원 시민
///  - F6(firstGuestIsKiller = true): 번호(PlayerRef)가 가장 작은 손님 1명만 범인, 나머지 손님은 시민.
///    번호가 작은 손님 = 보통 먼저 들어온 손님 (나갔다 다시 들어오면 빈 번호를 다시 받을 수 있다 — 누가 범인인지는 배정 화면에 나온다)
/// [이유] 배정 규칙을 엔진 밖(dotnet)에서 테스트할 수 있게 화면 · 키 처리(MissionTestAssignmentPanel)와 분리한다.
///  손님이 없으면 배정하지 않는다 — 멀티 테스트는 손님이 있어야 의미가 있고, 혼자 테스트는 [혼자] 시작이 따로 있다.
/// [근거] 6a 명세 MT3 (사용자 결정 A — 호스트가 배정 때 손님 역할을 고른다), MT16
/// </summary>
public static class MissionTestRoles
{
    /// <param name="guests">접속한 사람 목록 — 호스트가 섞여 있어도 되고(빼고 센다) 같은 사람이 두 번 있어도 된다</param>
    /// <param name="citizens">결과: 시민 목록 (앞 내용은 지운다)</param>
    /// <param name="killers">결과: 범인 목록 (앞 내용은 지운다)</param>
    /// <returns>손님이 한 명도 없으면 false (두 목록은 빈 채로)</returns>
    public static bool Split(
        PlayerRef host, IEnumerable<PlayerRef> guests, bool firstGuestIsKiller,
        List<PlayerRef> citizens, List<PlayerRef> killers)
    {
        citizens.Clear();
        killers.Clear();

        List<PlayerRef> ordered = new List<PlayerRef>();

        foreach (PlayerRef guest in guests)
        {
            if (guest != host && !ordered.Contains(guest))
                ordered.Add(guest);
        }

        if (ordered.Count == 0)
            return false;

        ordered.Sort((a, b) => a.AsIndex.CompareTo(b.AsIndex));

        citizens.Add(host);

        for (int i = 0; i < ordered.Count; i++)
        {
            if (firstGuestIsKiller && i == 0)
                killers.Add(ordered[i]);
            else
                citizens.Add(ordered[i]);
        }

        return true;
    }
}
