using Fusion;

namespace TrustNoOne.Missions
{
    /// <summary>
    /// [역할] 미션/행동 목표 하나의 "실행체". 호스트에서만 존재하며, 이벤트를 받아 진행도와 완료 여부를 판정한다.
    ///
    /// [왜 인터페이스인가] 판정 방식이 다른 목표(Count / Avoid / Custom)를 라우터가 같은 방식으로 다루기 위해서.
    ///  새 판정 방식이 필요하면(예: "3분 이상 함께 이동") 이 인터페이스만 구현해서 ObjectiveFactory 에 등록하면 된다.
    ///
    /// [핵심 규칙] 진행도/상태를 바꾸는 경로는 OnEvent, OnTick, OnPlayerFinalized 세 곳뿐이다.
    ///
    /// [시각(now)] 제한 시간 판정을 위해 호스트의 시뮬레이션 시각(초)을 받는다 (MissionManager 가 Runner.SimulationTime 을 넘김).
    ///  Core 는 Fusion 런타임을 모르므로 시각은 인자로만 들어온다 (stage1 명세 S6, 3-3).
    /// </summary>
    public interface IMissionObjective
    {
        MissionDefinition Definition { get; }

        /// <summary>개인 목표의 주인. 단체 목표는 PlayerRef.None.</summary>
        PlayerRef Owner { get; }

        int Progress { get; }
        int Required { get; }
        ObjectiveStatus Status { get; }

        /// <summary>제한 시간 마감 시각(초). 제한 시간이 없거나 지금 셀 필요가 없으면 음수.</summary>
        double Deadline { get; }

        /// <summary>이 목표를 진행시킬 자격이 있는 플레이어인가? (단체=시민 전원, 개인=주인만)</summary>
        bool CanBeAdvancedBy(PlayerRef actor);

        /// <summary>
        /// actor 가 eventType 행동을 하면 이 목표가 "진행된다"고 볼 수 있는가?
        /// 미션 오브젝트의 상호작용 허용 여부 판단에 쓴다 ("해당 미션이 없으면 표시하지 않음").
        /// Avoid형은 그 행동을 "하지 말아야" 하므로 항상 false.
        /// </summary>
        bool WantsEvent(PlayerRef actor, MissionEventType eventType);

        /// <summary>이벤트 처리. 진행도/상태가 바뀌었으면 true.</summary>
        bool OnEvent(in MissionEvent e, PlayerActionLog log, double now);

        /// <summary>시간 경과 처리. 제한 시간을 넘겨 진행도가 초기화됐으면 true.</summary>
        bool OnTick(double now);

        /// <summary>
        /// 그 플레이어의 행동이 더 이상 늘지 않는 시점(탈출/사망/시간 종료)에 호출된다.
        /// "끝까지 안 했으면 성공" 같은 종료 판정을 여기서 확정한다. 상태가 바뀌었으면 true.
        /// </summary>
        bool OnPlayerFinalized(PlayerRef player);
    }
}
