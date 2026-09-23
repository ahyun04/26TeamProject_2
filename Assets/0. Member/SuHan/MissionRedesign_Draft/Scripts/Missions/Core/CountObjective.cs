using System.Collections.Generic;
using Fusion;

namespace TrustNoOne.Missions
{
    /// <summary>
    /// [역할] "Trigger 행동을 N번 하면 완료" (실시간 판정). 기획서의 미션 대부분이 이 형태다.
    ///        예) 발전기 5대 수리(단체), 발전기 수리 2회(개인), 의료 키트 사용, 의료실 방문, 시체 최초 발견(N=1).
    ///
    /// [단체 진행도] 참여자가 여러 명이면 누가 하든 같은 Progress 에 합산된다.
    ///  (기획서: "모든 시민의 행동이 하나의 진행도에 합산된다")
    ///
    /// [불변 조건]
    ///  - 완료/실패 후에는 아무것도 바꾸지 않는다 (한 번 확정된 결과는 되돌리지 않는다).
    ///  - Progress 는 Required 를 넘지 않는다.
    /// </summary>
    public class CountObjective : MissionObjectiveBase
    {
        public CountObjective(MissionDefinition definition, PlayerRef owner, IEnumerable<PlayerRef> participants)
            : base(definition, owner, participants)
        {
        }

        public override bool OnEvent(in MissionEvent e, PlayerActionLog log)
        {
            if (Status != ObjectiveStatus.InProgress)
                return false;

            if (e.Type != Definition.Trigger || !CanBeAdvancedBy(e.Actor))
                return false;

            Progress += e.Amount;

            if (Progress >= Required)
            {
                Progress = Required;
                Status = ObjectiveStatus.Completed;
            }

            return true;
        }
    }
}
