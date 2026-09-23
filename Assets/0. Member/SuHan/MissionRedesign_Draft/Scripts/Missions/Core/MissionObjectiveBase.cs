using System.Collections.Generic;
using Fusion;

namespace TrustNoOne.Missions
{
    /// <summary>
    /// [역할] IMissionObjective 구현체들이 공통으로 쓰는 뼈대.
    ///        "누가 진행시킬 수 있는가(참여자)" 규칙을 한 곳에 모아둔다.
    ///
    /// [참여자(participants) 규칙]
    ///  - 단체 목표: 참여자 = 배정 시점의 시민 전원 (살인마는 단체 미션을 진행시킬 수 없다)
    ///  - 개인 목표: 참여자 = 주인 한 명
    ///  → CanBeAdvancedBy 하나로 두 경우가 같은 코드로 처리된다.
    ///
    /// [기본값] 제한 시간이 없는 목표는 Deadline 음수, OnTick 은 아무것도 하지 않는다.
    /// </summary>
    public abstract class MissionObjectiveBase : IMissionObjective
    {
        private readonly HashSet<PlayerRef> participants;

        protected MissionObjectiveBase(MissionDefinition definition, PlayerRef owner, IEnumerable<PlayerRef> participants)
        {
            Definition = definition;
            Owner = owner;
            this.participants = new HashSet<PlayerRef>(participants);
            Status = ObjectiveStatus.InProgress;
        }

        public MissionDefinition Definition { get; }
        public PlayerRef Owner { get; }
        public int Progress { get; protected set; }
        public ObjectiveStatus Status { get; protected set; }

        public virtual int Required => Definition.RequiredCount;

        public virtual double Deadline => -1d;

        public bool CanBeAdvancedBy(PlayerRef actor)
        {
            return participants.Contains(actor);
        }

        public virtual bool WantsEvent(PlayerRef actor, MissionEventType eventType)
        {
            return Status == ObjectiveStatus.InProgress
                && Definition.Trigger == eventType
                && CanBeAdvancedBy(actor);
        }

        public abstract bool OnEvent(in MissionEvent e, PlayerActionLog log, double now);

        public virtual bool OnTick(double now)
        {
            return false;
        }

        public virtual bool OnPlayerFinalized(PlayerRef player)
        {
            return false;
        }
    }
}
