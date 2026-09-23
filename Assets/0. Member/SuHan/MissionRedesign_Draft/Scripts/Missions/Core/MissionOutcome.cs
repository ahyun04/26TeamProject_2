using System.Collections.Generic;
using Fusion;

namespace TrustNoOne.Missions
{
    /// <summary>
    /// [역할] 목표 목록을 보고 "지금 어떤 결과 상태인가"를 계산하는 읽기 전용 질의 모음. (상태를 바꾸지 않는다)
    ///
    /// [왜 Router 와 분리했는가]
    ///  Router 는 "이벤트를 전달하고 변경을 알린다"만 맡고, 승리/탈출 같은 "해석"은 여기에 모은다.
    ///  GameEndSystem 이 묻는 질문(단체 완료? 내 개인 미션 완료? 행동 목표 성공? 탈출 해금?)의 규칙이
    ///  전부 이 파일 안에 있어서, 기획이 바뀌면 여기만 고치면 된다.
    /// </summary>
    public static class MissionOutcome
    {
        /// <summary>
        /// 단체 미션이 전부 완료되었는가.
        /// 단체 미션이 하나도 없으면 false — 풀 설정 실수로 시작하자마자 시민이 이기는 것을 막는 안전장치
        /// (기존 CheckCitizenMissionsCompleted 의 hasCitizenMission 가드와 같은 의도).
        /// </summary>
        public static bool IsTeamCompleted(IReadOnlyList<IMissionObjective> objectives)
        {
            bool hasTeamMission = false;

            foreach (IMissionObjective objective in objectives)
            {
                if (objective.Definition.Category != MissionCategory.Team)
                    continue;

                hasTeamMission = true;

                if (objective.Status != ObjectiveStatus.Completed)
                    return false;
            }

            return hasTeamMission;
        }

        /// <summary>
        /// 그 시민의 개인 미션이 전부 완료되었는가. 개인 미션이 없으면 true(해당 조건 없음).
        /// 단체 미션과 달리 "없음"을 true 로 보는 이유: 개인 미션이 0개인 시민이 영원히 이길 수 없게 되는 것을 피하기 위해서.
        /// </summary>
        public static bool IsPersonalCompleted(IReadOnlyList<IMissionObjective> objectives, PlayerRef player)
        {
            foreach (IMissionObjective objective in objectives)
            {
                if (objective.Definition.Category != MissionCategory.Personal || objective.Owner != player)
                    continue;

                if (objective.Status != ObjectiveStatus.Completed)
                    return false;
            }

            return true;
        }

        /// <summary>
        /// 그 플레이어의 행동 목표가 모두 성공했는가.
        /// 행동 목표가 없으면 true — 생성 단계에서 충돌 때문에 못 받은 플레이어가 불이익을 받지 않게 한다
        /// (생성 시 경고 로그가 남는다).
        /// 종료 판정형(Avoid)은 FinalizePlayer 이후에야 Completed 가 되므로, 그 전에 호출하면 false 일 수 있다.
        /// </summary>
        public static bool IsActionGoalSuccess(IReadOnlyList<IMissionObjective> objectives, PlayerRef player)
        {
            foreach (IMissionObjective objective in objectives)
            {
                if (objective.Definition.Category != MissionCategory.ActionGoal || objective.Owner != player)
                    continue;

                if (objective.Status != ObjectiveStatus.Completed)
                    return false;
            }

            return true;
        }

        /// <summary>
        /// 탈출 해금 여부.
        ///  - escapeMissionId 가 지정되고 이번 판에 그 미션이 있으면: 그 미션 완료 시 해금 (기본 TM005 탈출구 개방)
        ///  - 지정이 비었거나 이번 판에 그 미션이 뽑히지 않았으면: 단체 미션 전부 완료 시 해금
        ///    (Pool 에서 랜덤으로 뽑으므로 탈출구 개방이 안 뽑힐 수 있다. 그때 탈출이 영원히 막히는 것을 방지)
        /// </summary>
        public static bool IsEscapeUnlocked(IReadOnlyList<IMissionObjective> objectives, string escapeMissionId)
        {
            if (!string.IsNullOrEmpty(escapeMissionId))
            {
                foreach (IMissionObjective objective in objectives)
                {
                    if (objective.Definition.Category == MissionCategory.Team &&
                        objective.Definition.Id == escapeMissionId)
                    {
                        return objective.Status == ObjectiveStatus.Completed;
                    }
                }
            }

            return IsTeamCompleted(objectives);
        }

        /// <summary>
        /// 시민 전체 합산 진행도 (HUD 진행바용). 단체 + 시민 개인 미션의 Progress/Required 합.
        /// 개인별 값이 아니라 합계만 공개하므로 "누가 얼마나 했는지"는 드러나지 않는다.
        /// </summary>
        public static void GetCitizenProgress(
            IReadOnlyList<IMissionObjective> objectives, out int current, out int required)
        {
            current = 0;
            required = 0;

            foreach (IMissionObjective objective in objectives)
            {
                MissionCategory category = objective.Definition.Category;

                if (category != MissionCategory.Team && category != MissionCategory.Personal)
                    continue;

                current += objective.Progress;
                required += objective.Required;
            }
        }
    }
}
