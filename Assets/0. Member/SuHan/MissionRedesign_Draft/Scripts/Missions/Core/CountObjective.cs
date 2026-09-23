using System.Collections.Generic;
using Fusion;

namespace TrustNoOne.Missions
{
    /// <summary>
    /// [역할] "Trigger 행동을 N번 하면 완료" (실시간 판정). 기획서의 미션 대부분이 이 형태다.
    ///        예) 발전기 3대 수리(단체), 밸브 잠그기(개인).
    ///
    /// [단체 진행도] 참여자가 여러 명이면 누가 하든 같은 Progress 에 합산된다.
    ///
    /// [제한 시간] (단체 미션 기획서 "발전기 1대 수리 후 2분 안에 다음 발전기, 못 하면 초기화")
    ///  - 정의에 제한 시간이 있고, 진행이 1 이상이고, 아직 진행 중일 때만 마감이 있다.
    ///  - 마감 = (마지막 진행 시각 또는 첫 진행 시각) + 제한 시간 (TimeLimitMode)
    ///  - OnTick 에서 마감을 넘기면 진행도를 0 으로 되돌린다. 실패(Failed)가 아니라 "처음부터 다시"다.
    ///
    /// [불변 조건]
    ///  - 완료/실패 후에는 아무것도 바꾸지 않는다 (시간이 지나도 완료는 유지).
    ///  - Progress 는 Required 를 넘지 않는다.
    /// </summary>
    public class CountObjective : MissionObjectiveBase
    {
        private double firstProgressTime = -1d;
        private double lastProgressTime = -1d;

        public CountObjective(MissionDefinition definition, PlayerRef owner, IEnumerable<PlayerRef> participants)
            : base(definition, owner, participants)
        {
        }

        public override double Deadline
        {
            get
            {
                if (!Definition.HasTimeLimit || Status != ObjectiveStatus.InProgress || Progress <= 0)
                    return -1d;

                double start = Definition.TimeLimitMode == TimeLimitMode.SinceFirstProgress
                    ? firstProgressTime
                    : lastProgressTime;

                return start + Definition.TimeLimitSeconds;
            }
        }

        public override bool OnEvent(in MissionEvent e, PlayerActionLog log, double now)
        {
            if (Status != ObjectiveStatus.InProgress)
                return false;

            if (e.Type != Definition.Trigger || !CanBeAdvancedBy(e.Actor))
                return false;

            Progress += e.Amount;

            if (firstProgressTime < 0d)
                firstProgressTime = now;

            lastProgressTime = now;

            if (Progress >= Required)
            {
                Progress = Required;
                Status = ObjectiveStatus.Completed;
            }

            return true;
        }

        public override bool OnTick(double now)
        {
            double deadline = Deadline;

            if (deadline < 0d || now < deadline)
                return false;

            Progress = 0;
            firstProgressTime = -1d;
            lastProgressTime = -1d;
            return true;
        }
    }
}
