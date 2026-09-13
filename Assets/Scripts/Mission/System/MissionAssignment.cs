using System.Collections.Generic;
using UnityEngine;

public class MissionAssignment
{
    private readonly MissionData[] missionPool;


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


            // 같은 ID가 실수로 여러 번 들어간 경우 중복 방지
            if (!missionIds.Add(mission.Id))
                continue;

            candidates.Add(mission);
        }

        Shuffle(candidates);

        count = Mathf.Max(0, count);
        if (count < candidates.Count)
            candidates.RemoveRange(count, candidates.Count - count);


        return candidates;
    }


    /// <summary>
    /// 배정 기록을 유지하지 않는다. 기존 호출부 호환을 위해 유지한다.
    /// </summary>
    public void Reset()
    {
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
