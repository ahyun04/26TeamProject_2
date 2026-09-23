using System;
using System.Collections.Generic;
using Fusion;

namespace TrustNoOne.Missions
{
    /// <summary>
    /// [역할] 이번 판의 모든 목표를 들고 있다가, 발행된 MissionEvent 를 목표들에게 전달한다.
    ///        (호스트 전용 · 네트워크 비의존)
    ///
    /// [처리 순서] Publish: ① 행동 기록 → ② 각 목표의 OnEvent.
    ///  기록을 먼저 하는 이유: Custom 목표가 OnEvent 안에서 "지금까지의 기록"을 참조할 수 있게 하기 위해서.
    ///
    /// [하나의 이벤트 → 여러 목표] 같은 이벤트가 단체 미션과 개인 미션에 동시에 반영될 수 있다
    ///  (예: 발전기 수리 1회가 단체 "5대"와 개인 "2회" 모두에 +1). 기획서에 반대 규칙이 없어 이렇게 정했다.
    ///
    /// [변경 통지] 상태가 바뀐 목표마다 ObjectiveChanged 를 발생시킨다. MissionManager 가 이걸 받아서 동기화한다.
    ///  라우터는 "무엇이 바뀌었나"만 알리고 "어떻게 동기화하나"는 모른다 → 네트워크와 판정이 분리된다.
    /// </summary>
    public class MissionEventRouter
    {
        private readonly List<IMissionObjective> objectives = new List<IMissionObjective>();

        public PlayerActionLog Log { get; } = new PlayerActionLog();

        public IReadOnlyList<IMissionObjective> Objectives => objectives;

        public event Action<IMissionObjective> ObjectiveChanged;

        /// <summary>새 판 시작: 목표 목록을 교체하고 기록을 비운다.</summary>
        public void Reset(IEnumerable<IMissionObjective> newObjectives)
        {
            objectives.Clear();
            objectives.AddRange(newObjectives);
            Log.Clear();
        }

        /// <summary>
        /// actor 가 eventType 행동을 했을 때 진행될 활성 목표가 하나라도 있는가.
        /// 미션 오브젝트가 "이 상호작용을 허용할지" 호스트에서 판단하는 데 쓴다.
        /// </summary>
        public bool HasActiveObjective(PlayerRef actor, MissionEventType eventType)
        {
            for (int i = 0; i < objectives.Count; i++)
            {
                if (objectives[i].WantsEvent(actor, eventType))
                    return true;
            }

            return false;
        }

        public void Publish(in MissionEvent e)
        {
            Log.Record(e);

            for (int i = 0; i < objectives.Count; i++)
            {
                IMissionObjective objective = objectives[i];

                if (objective.OnEvent(e, Log))
                    ObjectiveChanged?.Invoke(objective);
            }
        }

        /// <summary>그 플레이어의 행동이 끝났다 (탈출/사망/시간 종료). 종료 판정 목표를 확정한다.</summary>
        public void FinalizePlayer(PlayerRef player)
        {
            for (int i = 0; i < objectives.Count; i++)
            {
                IMissionObjective objective = objectives[i];

                if (objective.OnPlayerFinalized(player))
                    ObjectiveChanged?.Invoke(objective);
            }
        }

        /// <summary>목표를 가진 모든 플레이어를 확정한다 (시간 종료 등 게임 전체가 끝날 때).</summary>
        public void FinalizeAll()
        {
            HashSet<PlayerRef> owners = new HashSet<PlayerRef>();

            foreach (IMissionObjective objective in objectives)
            {
                if (!objective.Owner.IsNone)
                    owners.Add(objective.Owner);
            }

            foreach (PlayerRef owner in owners)
                FinalizePlayer(owner);
        }
    }
}
