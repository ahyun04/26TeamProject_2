using System;
using System.Collections.Generic;
using Fusion;

namespace TrustNoOne.Missions
{
    /// <summary>
    /// [역할] 역할 배정 결과(시민/살인마 목록)와 MissionPool 을 받아 이번 판의 MissionPlan 을 만든다.
    ///        (호스트 전용 · 순수 C# · 네트워크/씬 비의존 · 랜덤 소스 주입)
    ///
    /// [기획서 근거] 다이어그램 6: 역할 배정 → MissionManager 초기화 → 단체 미션 → 개인 미션 → 개인 행동 목표
    ///  → 살인마 여부 확인 → 살인마 미션 → 동기화. 충돌 처리: "행동 목표 제외 → 새 목표 선택 → 충돌 검사 → 최종 지급".
    ///
    /// [생성 순서와 기획서 다이어그램의 차이] 이 클래스는 **살인마 미션을 행동 목표보다 먼저** 뽑는다.
    ///  살인마의 행동 목표도 자기 살인마 미션과 충돌하면 안 되는데, 행동 목표를 먼저 뽑으면 그 검사를 할 수 없기 때문이다.
    ///
    /// [정책]
    ///  - 개인/살인마 미션: 다른 플레이어와 겹치지 않게 뽑되, 풀이 모자라면 중복을 허용하고 경고한다.
    ///    (후보 부족으로 미션 0개인 플레이어가 생기는 것을 막는다)
    ///  - 행동 목표: **충돌만은 절대 타협하지 않는다**. 충돌 없는 후보가 없으면 지급하지 않고 경고한다.
    ///    행동 목표는 단체 미션과도 충돌 검사한다 (기획서엔 개인 미션만 언급되나, 단체 "발전기 수리" +
    ///    목표 "발전기 만지지 않기"는 원천 패배라 기획 의도로 보기 어렵다).
    ///  - 랜덤 소스를 주입받으므로 시드를 고정하면 같은 결과를 재현할 수 있다 (검증/디버깅용).
    /// </summary>
    public class MissionGenerator
    {
        private readonly MissionPool pool;
        private readonly ObjectiveFactory factory;
        private readonly Random random;

        public MissionGenerator(MissionPool pool, ObjectiveFactory factory, Random random)
        {
            this.pool = pool;
            this.factory = factory;
            this.random = random;
        }

        public MissionPlan Generate(IReadOnlyList<PlayerRef> citizens, IReadOnlyList<PlayerRef> killers)
        {
            MissionPlan plan = new MissionPlan();

            // 플레이어별로 "이미 받은 정의" 목록. 중복 방지와 충돌 검사의 기준이 된다.
            Dictionary<PlayerRef, List<MissionDefinition>> assigned = new Dictionary<PlayerRef, List<MissionDefinition>>();

            foreach (PlayerRef citizen in citizens)
                assigned[citizen] = new List<MissionDefinition>();

            foreach (PlayerRef killer in killers)
                assigned[killer] = new List<MissionDefinition>();

            // ① 단체 미션: 시민 전체가 공동 진행 (소유자 없음)
            List<MissionDefinition> teamDefinitions = Pick(
                plan, MissionCategory.Team, pool.TeamMissionCount,
                new HashSet<string>(), null, checkConflict: false, allowDuplicateFallback: false);

            foreach (MissionDefinition definition in teamDefinitions)
                AddObjective(plan, definition, PlayerRef.None, citizens);

            // 단체 미션은 시민 전원이 "받은 것"으로 기록 → 행동 목표 충돌 검사 대상 (정책 참고)
            foreach (PlayerRef citizen in citizens)
                assigned[citizen].AddRange(teamDefinitions);

            // ② 개인 미션: 시민마다
            HashSet<string> usedPersonalIds = new HashSet<string>();

            foreach (PlayerRef citizen in citizens)
            {
                List<MissionDefinition> picked = Pick(
                    plan, MissionCategory.Personal, pool.PersonalMissionsPerCitizen,
                    usedPersonalIds, assigned[citizen], checkConflict: false, allowDuplicateFallback: true);

                foreach (MissionDefinition definition in picked)
                {
                    assigned[citizen].Add(definition);
                    AddObjective(plan, definition, citizen, new[] { citizen });
                }
            }

            // ③ 살인마 미션: 살인마마다 (행동 목표보다 먼저 — 위 "순서 차이" 참고)
            HashSet<string> usedKillerIds = new HashSet<string>();

            foreach (PlayerRef killer in killers)
            {
                List<MissionDefinition> picked = Pick(
                    plan, MissionCategory.Killer, pool.KillerMissionsPerKiller,
                    usedKillerIds, assigned[killer], checkConflict: false, allowDuplicateFallback: true);

                foreach (MissionDefinition definition in picked)
                {
                    assigned[killer].Add(definition);
                    AddObjective(plan, definition, killer, new[] { killer });
                }
            }

            // ④ 개인 행동 목표: 모든 플레이어, 충돌 없는 것만
            HashSet<string> usedGoalIds = new HashSet<string>();

            foreach (PlayerRef player in EnumerateAll(citizens, killers))
            {
                List<MissionDefinition> picked = Pick(
                    plan, MissionCategory.ActionGoal, pool.ActionGoalsPerPlayer,
                    usedGoalIds, assigned[player], checkConflict: true, allowDuplicateFallback: true);

                foreach (MissionDefinition definition in picked)
                {
                    assigned[player].Add(definition);
                    AddObjective(plan, definition, player, new[] { player });
                }

                if (pool.ActionGoalsPerPlayer > 0 && picked.Count == 0)
                {
                    plan.Warnings.Add(
                        $"[행동 목표 미배정] {player}: 충돌 없는 후보가 없어 지급하지 않았습니다. 행동 목표 없음은 '성공'으로 간주됩니다.");
                }
            }

            return plan;
        }

        /// <summary>
        /// 카테고리에서 count 개를 랜덤으로 뽑는다.
        ///  1차: 다른 플레이어가 아직 안 받은 것 (usedIds 에 없는 것)
        ///  2차(allowDuplicateFallback): 1차로 부족하면 이미 다른 플레이어가 받은 것도 허용 + 경고
        ///  두 단계 모두 "이 플레이어가 이미 받은 것"과 "충돌하는 것(checkConflict)"은 제외한다.
        /// </summary>
        private List<MissionDefinition> Pick(
            MissionPlan plan,
            MissionCategory category,
            int count,
            HashSet<string> usedIds,
            IReadOnlyList<MissionDefinition> playerHas,
            bool checkConflict,
            bool allowDuplicateFallback)
        {
            List<MissionDefinition> result = new List<MissionDefinition>();

            if (count <= 0)
                return result;

            List<MissionDefinition> candidates = new List<MissionDefinition>();
            pool.GetByCategory(category, candidates);
            Shuffle(candidates);

            foreach (MissionDefinition candidate in candidates)
            {
                if (result.Count >= count)
                    break;

                if (usedIds.Contains(candidate.Id))
                    continue;

                if (IsEligible(candidate, result, playerHas, checkConflict))
                    result.Add(candidate);
            }

            if (result.Count < count && allowDuplicateFallback)
            {
                bool usedFallback = false;

                foreach (MissionDefinition candidate in candidates)
                {
                    if (result.Count >= count)
                        break;

                    if (IsEligible(candidate, result, playerHas, checkConflict))
                    {
                        result.Add(candidate);
                        usedFallback = true;
                    }
                }

                // 행동 목표는 같은 목표를 여럿이 받는 것이 자연스러워서 경고하지 않는다.
                if (usedFallback && category != MissionCategory.ActionGoal)
                    plan.Warnings.Add($"[{category} 중복 배정] 풀이 부족해 다른 플레이어와 같은 미션을 허용했습니다.");
            }

            if (result.Count < count && category != MissionCategory.ActionGoal)
                plan.Warnings.Add($"[{category} 부족] {count}개가 필요하지만 {result.Count}개만 배정할 수 있었습니다.");

            foreach (MissionDefinition definition in result)
                usedIds.Add(definition.Id);

            return result;
        }

        private static bool IsEligible(
            MissionDefinition candidate,
            List<MissionDefinition> chosenSoFar,
            IReadOnlyList<MissionDefinition> playerHas,
            bool checkConflict)
        {
            // 방금 뽑은 것과 같은 정의 재선택 금지
            foreach (MissionDefinition chosen in chosenSoFar)
            {
                if (chosen.Id == candidate.Id)
                    return false;
            }

            if (playerHas != null)
            {
                foreach (MissionDefinition has in playerHas)
                {
                    if (has.Id == candidate.Id)
                        return false;

                    if (checkConflict && MissionConflict.AreInConflict(candidate, has))
                        return false;
                }
            }

            // 같은 회차에 뽑은 것끼리도 충돌하면 안 된다 (행동 목표가 2개 이상일 때)
            if (checkConflict)
            {
                foreach (MissionDefinition chosen in chosenSoFar)
                {
                    if (MissionConflict.AreInConflict(candidate, chosen))
                        return false;
                }
            }

            return true;
        }

        private void AddObjective(
            MissionPlan plan, MissionDefinition definition, PlayerRef owner, IReadOnlyCollection<PlayerRef> participants)
        {
            IMissionObjective objective = factory.Create(definition, owner, participants);

            if (objective == null)
            {
                plan.Warnings.Add($"[목표 생성 실패] {definition.Id}: Kind={definition.Kind}, CustomKey='{definition.CustomObjectiveKey}' 를 만들 수 없어 건너뜁니다.");
                return;
            }

            plan.Objectives.Add(objective);
        }

        private static IEnumerable<PlayerRef> EnumerateAll(IReadOnlyList<PlayerRef> citizens, IReadOnlyList<PlayerRef> killers)
        {
            foreach (PlayerRef citizen in citizens)
                yield return citizen;

            foreach (PlayerRef killer in killers)
                yield return killer;
        }

        /// <summary>Fisher-Yates 셔플. 주입된 랜덤을 쓰므로 시드 고정 시 재현 가능.</summary>
        private void Shuffle(List<MissionDefinition> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = random.Next(i + 1);
                MissionDefinition temp = list[i];
                list[i] = list[j];
                list[j] = temp;
            }
        }
    }
}
