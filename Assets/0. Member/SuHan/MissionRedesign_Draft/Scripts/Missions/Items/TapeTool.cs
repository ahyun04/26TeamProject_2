namespace TrustNoOne.Missions
{
    /// <summary>
    /// [역할] 테이프 아이템 (13a — 배관 누수 차단 TG012). 손에 들고 배관의 새는 곳을 조준한 채 F 를 누르고 있으면 그 자리가 막힌다.
    ///  판정은 모두 배관 장치(PipeLeakStation)가 한다 — 호스트가 "그 사람이 손에 테이프를 들었나"(MissionItems.TryGetHeld)만 본다.
    /// [우클릭 기능 없음] 기획서 "테이프를 찾아 … 상호작용 키를 누르면 조작이 진행된다" — 이 게임의 누르고 있기 키는 F (PL2).
    /// [써도 없어지지 않음] 도구다 (PL3 — 사용자 결정 A). 2분 실패로 초기화돼도 그대로 남는다.
    /// [팀원 코드] ItemBase(줍기 · 장착 · 월드 표시)는 상속만 한다. 아이템 번호 · 1인칭 모델은 ItemData(TapeData, 번호 26)에서 온다.
    /// [근거] 13a 명세 PL2 · PL3 · PL12 · PL20
    /// </summary>
    public class TapeTool : ItemBase
    {
    }
}
