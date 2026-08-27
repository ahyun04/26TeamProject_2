using System.Collections.Generic;
using UnityEngine;

public class MissionAssignment
{
    private readonly MissionData[] missionPool;

    // 이번 게임에서 이미 배정된 Personal 미션 ID
    private readonly HashSet<int> assignedPersonalIds = new();


    public MissionAssignment(MissionData[] missionPool)
    {
        this.missionPool = missionPool;
    }


    /// <summary>
    /// 역할과 미션 종류에 맞는 MissionData를 랜덤 선택
    /// </summary>
    public List<MissionData> Pick(MissionRoleTarget roleTarget, MissionType missionType, int count)
    {
        List<MissionData> candidates = new();
        HashSet<int> missionIds = new();

        if (missionPool == null)
            return candidates;


        foreach (MissionData mission in missionPool)
        {
            if (mission == null)
                continue;

            if (mission.RoleTarget != roleTarget)
                continue;

            if (mission.MissionType != missionType)
                continue;


            // Personal 미션은 이미 다른 플레이어에게 배정된 ID 제외
            if (missionType == MissionType.Personal &&
                assignedPersonalIds.Contains(mission.Id))
            {
                continue;
            }

            // 같은 ID가 실수로 여러 번 들어간 경우 중복 방지
            if (!missionIds.Add(mission.Id))
                continue;

            candidates.Add(mission);
        }

        Shuffle(candidates);

        if (count < candidates.Count)
            candidates.RemoveRange(count, candidates.Count - count);


        // 실제 선택된 Personal 미션 ID를 기억
        if (missionType == MissionType.Personal)
        {
            foreach (MissionData mission in candidates)
                assignedPersonalIds.Add(mission.Id);
        }

        return candidates;
    }


    /// <summary>
    /// 새 게임 시작 시 Personal 미션 배정 기록 초기화
    /// </summary>
    public void Reset()
    {
        assignedPersonalIds.Clear();
    }


    /// <summary>
    /// MissionData 목록 순서를 랜덤으로 섞음
    /// </summary>
    private void Shuffle(List<MissionData> missions)
    {
        for (int i = missions.Count - 1; i > 0; i--)
        {
            int randomIndex = Random.Range(0, i + 1);

            (missions[i], missions[randomIndex]) =
                (missions[randomIndex], missions[i]);
        }
    }
}