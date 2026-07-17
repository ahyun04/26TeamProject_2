using Fusion;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 역할별 미션 데이터 분류,
/// 중복 없는 미션 랜덤 선택,
/// 단체, 개인미션 생성,
/// 진행도 변경
/// 미션 데이터 검색
/// 담당 스크립트
/// </summary>
public class MissionSystem : NetworkBehaviour
{
    private const int MaxMissionCount = 64;

    [SerializeField] private MissionData[] missionPool;

    [SerializeField, Min(0)]
    private int commonMissionCount = 2;

    [SerializeField, Min(0)]
    private int citizenPersonalMissionCount = 2;

    [SerializeField, Min(0)]
    private int killerPersonalMissionCount = 1;

    [Networked, Capacity(MaxMissionCount)]
    public NetworkArray<MissionRuntimeState> Missions => default;

    [Networked] public int MissionCount { get; set; }

    [Networked] public NetworkBool IsInitialized { get; set; }

    private System.Random random;

    public override void Spawned()
    {
        if (Object.HasStateAuthority)
        {
            random = new System.Random();
        }
    }

    public void InitializeMissions(Dictionary<PlayerRef, PlayerRole> playerRoles)
    {
        if (!Object.HasStateAuthority || IsInitialized)
            return;


        if (playerRoles == null || !ValidateMissionPool())
            return;


        if (random == null)
            random = new System.Random();

        ClearMissions();

        AssignRandomMissions(
            MissionKind.Common,
            commonMissionCount,
            PlayerRef.None);

        foreach (KeyValuePair<PlayerRef, PlayerRole> pair in playerRoles)
        {
            PlayerRef player = pair.Key;
            PlayerRole role = pair.Value;

            if (role == PlayerRole.Citizen)
            {
                AssignRandomMissions(
                    MissionKind.CitizenPersonal,
                    citizenPersonalMissionCount,
                    player);
            }
            else if (role == PlayerRole.Killer)
            {
                AssignRandomMissions(
                    MissionKind.KillerPersonal,
                    killerPersonalMissionCount,
                    player);
            }
        }

        IsInitialized = true;
    }

    private void AssignRandomMissions(
        MissionKind missionKind,
        int assignCount,
        PlayerRef owner)
    {
        List<MissionData> candidates =
            GetMissionCandidates(missionKind);

        int validCount =
            Mathf.Min(assignCount, candidates.Count);

        if (assignCount > candidates.Count)
        {
            Debug.LogWarning(
                $"{missionKind} 미션 데이터가 부족합니다. " +
                $"{assignCount}개 중 {validCount}개만 배정합니다.");
        }

        for (int i = 0; i < validCount; i++)
        {
            if (MissionCount >= MaxMissionCount)
            {
                Debug.LogError("미션 최대 저장 개수를 초과했습니다.");

                return;
            }

            int randomIndex = random.Next(candidates.Count);

            MissionData missionData = candidates[randomIndex];

            candidates.RemoveAt(randomIndex);

            AddMission(missionData, owner);
        }
    }

    private List<MissionData> GetMissionCandidates(MissionKind missionKind)
    {
        List<MissionData> candidates = new List<MissionData>();

        foreach (MissionData missionData in missionPool)
        {
            if (missionData == null)
            {
                continue;
            }

            if (missionData.MissionKind != missionKind)
            {
                continue;
            }

            candidates.Add(missionData);
        }

        return candidates;
    }

    private void AddMission(MissionData missionData, PlayerRef owner)
    {
        MissionRuntimeState state = new MissionRuntimeState
        {
            MissionId = missionData.MissionId,

            MissionKind = missionData.MissionKind,

            Owner = owner, Progress = 0,

            TargetCount = missionData.TargetCount
        };

        Missions.Set(MissionCount, state);
        MissionCount++;
    }

    public bool AddProgress(int missionId, PlayerRef player, int amount = 1)
    {
        if (!Object.HasStateAuthority || 
            !IsInitialized || amount <= 0)
            return false;
     

        for (int i = 0; i < MissionCount; i++)
        {
            MissionRuntimeState state = Missions.Get(i);

            if (state.MissionId != missionId)
                continue;

            bool isCommonMission =
                state.MissionKind ==
                MissionKind.Common;

            bool isOwner = state.Owner == player;

            if (!isCommonMission && !isOwner)
                continue;

            if (state.IsCompleted)
                return false;
          

            state.Progress = Mathf.Min(
                state.Progress + amount,
                state.TargetCount);

            Missions.Set(i, state);

            return true;
        }

        return false;
    }

    public bool TryGetMissionData(int missionId, out MissionData result)
    {
        foreach (MissionData missionData in missionPool)
        {
            if (missionData == null)
            {
                continue;
            }

            if (missionData.MissionId == missionId)
            {
                result = missionData;
                return true;
            }
        }

        result = null;
        return false;
    }

    public void ResetMissions()
    {
        if (!Object.HasStateAuthority)
            return;

        ClearMissions();
        IsInitialized = false;
    }

    private void ClearMissions()
    {
        for (int i = 0; i < MissionCount; i++)
        {
            Missions.Set(i, default(MissionRuntimeState));
        }

        MissionCount = 0;
    }

    private bool ValidateMissionPool()
    {
        if (missionPool == null || missionPool.Length == 0)
        {
            Debug.LogError("Mission Pool에 미션 데이터가 없습니다.");

            return false;
        }

        HashSet<int> missionIds = new HashSet<int>();

        bool hasValidMission = false;

        foreach (MissionData missionData in missionPool)
        {
            if (missionData == null)
                continue;
        

            hasValidMission = true;

            if (missionData.MissionId <= 0)
            {
                Debug.LogError($"{missionData.name}의 Mission ID는 " + "1 이상이어야 합니다.");

                return false;
            }

            if (!missionIds.Add(missionData.MissionId))
            {
                Debug.LogError($"중복된 Mission ID가 있습니다: " + $"{missionData.MissionId}");

                return false;
            }
        }

        if (!hasValidMission)
        {
            Debug.LogError("유효한 미션 데이터가 없습니다.");

            return false;
        }

        return true;
    }
}