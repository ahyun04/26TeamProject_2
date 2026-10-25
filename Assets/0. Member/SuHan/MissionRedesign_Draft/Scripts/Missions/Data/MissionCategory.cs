namespace TrustNoOne.Missions
{
    /// <summary>
    /// [역할] 미션의 종류. 기획서 "미션 종류" 표와 1:1 대응한다.
    ///
    /// [왜 대상 역할(RoleTarget) 필드를 따로 두지 않는가]
    ///  카테고리가 곧 대상이다 (Team=시민 전체, Personal=시민 개인, ActionGoal=전원, Killer=살인마).
    ///  필드가 두 개면 "Killer 카테고리인데 대상은 시민" 같은 모순 조합이 만들어질 수 있다.
    /// </summary>
    public enum MissionCategory
    {
        /// <summary>단체 미션: 시민 전체가 하나의 공동 진행도를 채운다. 시민 승리/탈출 조건과 연결.</summary>
        Team = 0,

        /// <summary>개인 미션: 시민 개인 목표. 다른 플레이어에게 비공개(게임 종료 후 공개).</summary>
        Personal = 1,

        /// <summary>개인 행동 목표: 모든 플레이어가 받는 숨겨진 목표. 승패 조건이 아니라 "개인 승리"의 전제 조건.</summary>
        ActionGoal = 2,

        /// <summary>살인마 미션: 살인 능력 활성화. (기획서 본문 미작성 → 확장 지점만 열어둠)</summary>
        Killer = 3,
    }

    /// <summary>
    /// [역할] 목표(Objective)의 판정 방식. 기획서의 "실시간 판정 / 게임 종료 판정" 두 트랙을 표현한다.
    /// </summary>
    public enum ObjectiveKind
    {
        /// <summary>Trigger 행동을 RequiredCount번 하면 즉시 완료 (실시간 판정). 예: 발전기 5대 수리, 의료실 방문.</summary>
        Count = 0,

        /// <summary>
        /// Trigger 행동을 하면 즉시 실패, 끝까지 안 하면 그 플레이어의 행동이 끝나는 시점(탈출/사망/시간 종료)에 성공 (종료 판정).
        /// 예: 한 번도 달리지 않기, 거짓말탐지기 사용하지 않기, 아이템 주지 않기.
        /// </summary>
        Avoid = 1,

        /// <summary>
        /// 시간·상태가 필요한 특수 목표. IMissionObjective 서브클래스를 ObjectiveFactory에 등록해서 쓴다.
        /// 예: "특정 플레이어와 3분 이상 함께 이동". (이번 범위에서는 구현체 없이 확장 지점만)
        /// </summary>
        Custom = 2,
    }

    /// <summary>
    /// [역할] 제한 시간을 언제부터 셀지. 단체 미션 기획서의 두 가지 규칙을 표현한다.
    /// </summary>
    public enum TimeLimitMode
    {
        /// <summary>진행할 때마다 다시 센다. 예) 발전기: 1대 고칠 때마다 "다음 발전기까지 2분".</summary>
        SinceLastProgress = 0,

        /// <summary>첫 진행부터 센다. 예) 생명 유지 장치: "제한 시간 안에 밸브 4개 모두".</summary>
        SinceFirstProgress = 1,
    }

    /// <summary>
    /// [역할] 단체 미션 난이도 (9b 명세 DF5 — 미션 난이도 설정 기획). 풀의 난이도별 배정이 이 값으로 목록을 나눈다.
    ///  쉬움 = 밸브 · 차단기 · 전선 · 필터, 보통 = 안테나 · 발전기 · 코드, 어려움 = 압력 · 장비 조립 · 생명 유지.
    /// [없음] 난이도별 개수에 세지 않는다 — 방화문처럼 "항상 포함"인 미션 (배정 기획 "일반 미션 개수에 포함하지 않고").
    /// [주의] ScriptableObject 에 정수로 저장된다 — 값을 바꾸거나 재정렬하지 말 것.
    /// </summary>
    public enum MissionDifficulty
    {
        None = 0,
        Easy = 1,
        Normal = 2,
        Hard = 3,
    }
}
