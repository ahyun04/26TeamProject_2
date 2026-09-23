using System.Collections.Generic;
using Fusion;

namespace TrustNoOne.Missions
{
    /// <summary>
    /// [역할] "Trigger 행동을 하면 안 된다" (게임 종료 판정 트랙).
    ///        예) 한 번도 달리지 않기, 거짓말탐지기 사용하지 않기, 누구에게도 아이템 주지 않기.
    ///
    /// [기획서 근거] "게임 종료 판정: 게임 종료 시 전체 플레이 기록을 기준으로 판정",
    ///  "개인 행동 목표를 미행했을 시 어떤 상황에 탈출을 해도 개인 패배".
    ///
    /// [판정]
    ///  - 위반(Trigger 행동) 발생 → 즉시 Failed (허용 횟수 0)
    ///  - 위반 없이 OnPlayerFinalized(주인) 호출 → Completed
    ///    (호출 시점 = 그 플레이어의 행동이 끝나는 순간: 탈출/사망/시간 종료)
    ///
    /// [Required 를 1 로 고정하는 이유] "N회" 개념이 없다. UI 에는 0/1 → 1/1 로 보이게 한다.
    /// </summary>
    public class AvoidObjective : MissionObjectiveBase
    {
        public AvoidObjective(MissionDefinition definition, PlayerRef owner, IEnumerable<PlayerRef> participants)
            : base(definition, owner, participants)
        {
        }

        public override int Required => 1;

        /// <summary>Avoid형은 그 행동을 "하지 말아야" 하므로, 그 행동을 유도(상호작용 허용)하지 않는다.</summary>
        public override bool WantsEvent(PlayerRef actor, MissionEventType eventType)
        {
            return false;
        }

        public override bool OnEvent(in MissionEvent e, PlayerActionLog log)
        {
            if (Status != ObjectiveStatus.InProgress)
                return false;

            if (e.Type != Definition.Trigger || !CanBeAdvancedBy(e.Actor))
                return false;

            Status = ObjectiveStatus.Failed;
            return true;
        }

        public override bool OnPlayerFinalized(PlayerRef player)
        {
            if (Status != ObjectiveStatus.InProgress || !CanBeAdvancedBy(player))
                return false;

            Progress = Required;
            Status = ObjectiveStatus.Completed;
            return true;
        }
    }
}
