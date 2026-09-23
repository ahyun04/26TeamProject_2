namespace TrustNoOne.Missions
{
    /// <summary>
    /// [역할] 게임에서 "일어난 사실"의 종류. 미션은 이 종류에 반응하고, 행동 목표는 이 종류를 금지/요구한다.
    ///
    /// [왜 enum인가]
    ///  - RPC로 실어 보내기 쉽고 문자열 비교 비용이 없다.
    ///  - 충돌 검사(Requires/Forbids)가 enum 집합 교차 연산으로 끝난다.
    ///
    /// [기획서 근거] 단체 미션 Pool(TM001~TM007), 개인 미션 Pool(PM001~PM008),
    ///  개인 행동 목표 예시(달리기·아이템 전달·회복 아이템 보관·거짓말탐지기 미사용·시체 최초 발견·지역 방문).
    ///
    /// [주의] 이 값은 ScriptableObject에 정수로 직렬화된다.
    ///  새 행동을 추가할 때는 기존 값을 바꾸거나 재정렬하지 말고, 해당 그룹의 빈 번호나 맨 끝에 추가할 것.
    /// </summary>
    public enum MissionEventType
    {
        None = 0,

        // ── 단체 미션(TM) 계열: 미션 오브젝트가 완료 시 발행 ──
        GeneratorRepaired = 1,        // TM001 발전기 수리
        PowerConnected = 2,           // TM002 전원 연결 (PM006과 공유)
        SecuritySystemRestored = 3,   // TM003 보안 시스템 복구
        ServerRebooted = 4,           // TM004 서버 재부팅
        ExitOpened = 5,               // TM005 탈출구 개방
        CoolingRestored = 6,          // TM006 냉각 시스템 복구
        AntennaRestored = 7,          // TM007 통신 안테나 복구

        // ── 개인 미션(PM) 계열 ──
        MedKitUsed = 20,              // PM002 의료 키트 사용
        ItemCrafted = 21,             // PM003 아이템 제작
        MedicalRoomVisited = 22,      // PM004 의료실 방문
        ItemGiven = 23,               // PM005 아이템 전달
        ServerChecked = 24,           // PM007 서버 점검
        DoorOpened = 25,              // PM008 문 개방

        // ── 행동 기록 계열: 플레이어 스크립트가 발행 (행동 목표/탐지기 단서용) ──
        PlayerRan = 40,               // "한 번도 달리지 않기"
        LieDetectorUsed = 41,         // "거짓말 탐지기 사용하지 않기"
        CorpseFirstFound = 42,        // "시체 최초 발견"
        AreaVisited = 43,             // "특정 지역 방문" (TargetId = 지역 ID)

        // ── 새 기획서 개인 미션 (Trust No One 개인 미션 기획서) — 미니게임 오브젝트가 완료 시 발행 ──
        ValveClosed = 60,             // PS001 밸브 잠그기
        BreakerRestored = 61,         // PS002 차단기 올리기
        WiresConnected = 62,          // PS003 전선 연결하기
        AntennaAligned = 63,          // PS004 안테나 방향 맞추기
        PressureStabilized = 64,      // PS005 압력 수치 맞추기
        FilterCleaned = 65,           // PS006 필터 청소하기

        // ── 새 기획서 단체 미션 (Trust No One 단체 미션 기획서). 발전기는 기존 GeneratorRepaired(1) ──
        SecurityCodeEntered = 70,     // TG002 코드 순서 맞추기
        EquipmentAssembled = 71,      // TG003 고장난 장비 조립
        LifeSupportRestored = 72,     // TG004 생명 유지 장치 복구
        FireDoorOpened = 73,          // TG005 대형 방화문 열기
    }
}
