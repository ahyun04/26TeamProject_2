using Fusion;
using System;
using System.Collections.Generic;
using UnityEngine;

public class MissionSystem : NetworkBehaviour
{
    private const int MaxMissionCount = 64;


    [Header("전체 미션 데이터")]
    [SerializeField] private MissionData[] missionPool;


    [Header("게임당 미션 수")]
    [SerializeField] private int sharedMissionCount = 3;

    [SerializeField] private int citizenPersonalMissionCount = 2;
    [SerializeField] private int citizenPersonalActionMissionCount = 1;

    [SerializeField] private int killerPersonalMissionCount = 2;


    // 이번 게임에서 실제 사용 중인 미션 상태
    [Networked, Capacity(MaxMissionCount)]
    public NetworkArray<MissionState> Missions => default;


    [Networked]
    public int MissionCount { get; private set; }


    // 중복 초기화 방지
    [Networked]
    public NetworkBool Initialized { get; private set; }


    // 시민 승리 판정 등에 사용할 최종 미션 완료 상태
    [Networked]
    public NetworkBool CitizenMissionsCompleted { get; private set; }


    // Host의 GameFlowManager 등이 구독 가능
    public event Action OnCitizenMissionsCompleted;


    private MissionAssignment assignment;
    private MissionMiniGameBase[] miniGames;
    public bool IsReady => assignment != null;


    public override void Spawned()
    {
        if (!HasStateAuthority)
            return;

        assignment = new MissionAssignment(missionPool);

        SubscribeMiniGames();
    }


    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        if (!hasState)
            return;

        UnsubscribeMiniGames();
    }


    /// <summary>
    /// 역할 배정이 끝난 뒤 Host가 한 번 호출
    /// </summary>
    public void InitializeMissions(IReadOnlyList<PlayerRef> citizens, IReadOnlyList<PlayerRef> killers)
    {
        if (!HasStateAuthority || Initialized)
            return;

        MissionCount = 0;
        CitizenMissionsCompleted = false;


        // 공통 단체 미션
        AddMissions(
            assignment.Pick(
                MissionRoleTarget.All,
                MissionType.Shared,
                sharedMissionCount),
            PlayerRef.None
        );


        // 시민 개인 미션
        foreach (PlayerRef citizen in citizens)
        {
            AddMissions(
                assignment.Pick(
                    MissionRoleTarget.Citizen,
                    MissionType.Personal,
                    citizenPersonalMissionCount),
                citizen
            );

            AddMissions(
                assignment.Pick(
                    MissionRoleTarget.Citizen,
                    MissionType.PersonalAction,
                    citizenPersonalActionMissionCount),
                citizen
            );
        }


        // 살인마 개인 미션
        foreach (PlayerRef killer in killers)
        {
            AddMissions(
                assignment.Pick(
                    MissionRoleTarget.Killer,
                    MissionType.Personal,
                    killerPersonalMissionCount),
                killer
            );
        }


        Initialized = true;

        CheckCitizenMissionsCompleted();

        Debug.Log($"미션 초기화 완료 : {MissionCount}개");
    }


    /// <summary>
    /// 선택된 MissionData를 Networked MissionState로 등록
    /// </summary>
    private void AddMissions(List<MissionData> missions, PlayerRef owner)
    {
        foreach (MissionData mission in missions)
        {
            if (MissionCount >= MaxMissionCount)
            {
                Debug.LogWarning("등록 가능한 최대 미션 수를 초과");
                return;
            }

            MissionState state = new MissionState(
                mission.Id,
                owner,
                mission.RoleTarget,
                mission.MissionType,
                mission.RequiredCount
            );

            Missions.Set(MissionCount, state);

            Debug.Log(
                     $"[미션 배정] " +
                     $"플레이어: {owner} / " +
                     $"미션: {mission.MissionName} / " +
                     $"ID: {mission.Id} / " +
                     $"종류: {mission.MissionType} / " +
                     $"역할: {mission.RoleTarget}"
                    );

            MissionCount++;
        }
    }


    public MissionData GetMissionData(int missionId)
    {
        if (missionPool == null)
            return null;

        foreach (MissionData mission in missionPool)
        {
            if (mission != null && mission.Id == missionId)
                return mission;
        }

        return null;
    }

    /// <summary>
    /// 해당 플레이어가 이 미션을 수행할 수 있는지 확인
    /// </summary>
    public bool HasMission(int missionId, PlayerRef player)
    {
        if (!Initialized)
            return false;

        for (int i = 0; i < MissionCount; i++)
        {
            MissionState state = Missions[i];

            if (state.MissionId != missionId)
                continue;

            if (state.MissionType == MissionType.Shared)
                return true;

            return state.Owner == player;
        }

        return false;
    }


    /// <summary>
    /// MissionMiniGameBase가 완료됐을 때 호출
    /// </summary>
    private bool OnMiniGameCompleteRequested(int missionId, PlayerRef player)
    {
        return AddProgress(missionId, player);
    }


    /// <summary>
    /// MissionMiniGameBase에서 PersonalAction 실패 요청을 받음
    /// </summary>
    private bool OnMiniGameFailed(int missionId, PlayerRef player)
    {
        return FailPersonalAction(missionId, player);
    }


    /// <summary>
    /// 해당 플레이어가 수행한 미션 진행도 증가
    /// </summary>
    private bool AddProgress(int missionId, PlayerRef player)
    {
        if (!HasStateAuthority || !Initialized)
            return false;

        for (int i = 0; i < MissionCount; i++)
        {
            MissionState state = Missions[i];

            if (state.MissionId != missionId)
                continue;

            // 개인 미션이라면 배정받은 플레이어만 진행 가능
            if (state.MissionType != MissionType.Shared && state.Owner != player)
                continue;


            if (state.Status != MissionStatus.InProgress)
                return false;


            state.AddProgress();

            Missions.Set(i, state);

            Debug.Log(
                $"Mission {missionId} : " +
                $"{state.Progress}/{state.RequiredCount}"
            );


            CheckCitizenMissionsCompleted();

            return true;
        }

        return false;
    }


    /// <summary>
    /// 시민이 완료해야 하는 모든 미션이 끝났는지 확인
    /// </summary>
    private void CheckCitizenMissionsCompleted()
    {
        bool hasCitizenMission = false;

        for (int i = 0; i < MissionCount; i++)
        {
            MissionState state = Missions[i];

            // 단체미션과 시민 개인미션만 전체 진행도에 포함한다
            bool isShared = state.RoleTarget == MissionRoleTarget.All &&
                            state.MissionType == MissionType.Shared;


            bool isCitizenPersonal = state.RoleTarget == MissionRoleTarget.Citizen &&
                                     state.MissionType == MissionType.Personal;

            // 시민 전체 진행도에 포함하지 않는 미션은 건너뜀
            if (!isShared && !isCitizenPersonal)
                continue;

            hasCitizenMission = true;

            if (!state.IsCompleted)
                return;
        }


        if (!hasCitizenMission || CitizenMissionsCompleted)
            return;


        CitizenMissionsCompleted = true;

        OnCitizenMissionsCompleted?.Invoke();

        Debug.Log("시민 전체 미션 완료");
    }


    /// <summary>
    /// 시민 개인행동 미션의 완료 여부를 확인
    /// </summary>
    public bool IsPersonalActionCompleted(PlayerRef player)
    {
        if (!Initialized)
            return false;

        bool hasPersonalAction = false;

        for (int i = 0; i < MissionCount; i++)
        {
            MissionState state = Missions[i];

            if (state.Owner != player)
                continue;

            if (state.RoleTarget != MissionRoleTarget.Citizen)
                continue;

            if (state.MissionType != MissionType.PersonalAction)
                continue;

            hasPersonalAction = true;

            if (!state.IsCompleted)
                return false;
        }

        return hasPersonalAction;
    }


    /// <summary>
    /// 시민 개인 행동 미션이 실패했을 때
    /// </summary>
    private bool FailPersonalAction(int missionId, PlayerRef player)
    {
        if (!HasStateAuthority || !Initialized)
            return false;

        for (int i = 0; i < MissionCount; i++)
        {
            MissionState state = Missions[i];

            if (state.MissionId != missionId)
                continue;

            if (state.Owner != player)
                continue;

            if (state.RoleTarget != MissionRoleTarget.Citizen)
                continue;

            if (state.MissionType != MissionType.PersonalAction)
                continue;

            // 이미 성공 또는 실패했다면 결과 변경 금지
            if (state.Status != MissionStatus.InProgress)
                return false;

            state.Fail();

            Missions.Set(i, state);

            Debug.Log($"PersonalAction 실패 : {missionId} / Player : {player}");

            return true;
        }

        return false;
    }


    /// <summary>
    /// Scene에 존재하는 미션 미니게임의 완료 이벤트 구독
    /// </summary>
    private void SubscribeMiniGames()
    {
        miniGames = FindObjectsOfType<MissionMiniGameBase>(true);

        foreach (MissionMiniGameBase miniGame in miniGames)
        {
            miniGame.OnCompleteRequested += OnMiniGameCompleteRequested;
            miniGame.OnFailRequested += OnMiniGameFailed;
        }
            
    }


    /// <summary>
    /// 이벤트 중복 구독 방지
    /// </summary>
    private void UnsubscribeMiniGames()
    {
        if (miniGames == null)
            return;

        foreach (MissionMiniGameBase miniGame in miniGames)
        {
            if (miniGame == null)
                continue;

            miniGame.OnCompleteRequested -= OnMiniGameCompleteRequested;
            miniGame.OnFailRequested -= OnMiniGameFailed;
        }
    }

}