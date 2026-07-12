using Fusion;
using UnityEngine;

/// <summary>
/// 단체 미션과 개인 미션을 등록하고
/// 미션 진행도와 완료 상태를 네트워크로 관리
/// </summary>
public class MissionSystem : NetworkBehaviour
{
    [Header("미션 목록")]
    [SerializeField] private MissionData[] common;
    [SerializeField] private MissionData[] personal;

    // 등록 가능한 미션 최대 갯수
    private const int MaxCount = 20;

    // 현재 등록된 모든 미션 상태
    [Networked, Capacity(MaxCount)]
    public NetworkArray<MissionState> Missions => default;

    // 등록 가능한 현재 미션 갯수
    [Networked] public int Count { get; set; }

    // 단체 미션 초기화 여부
    [Networked] public NetworkBool Initialized { get; set; }


    /// <summary>
    /// 단체 미션 등록한다
    /// </summary>
    public override void Spawned()
    {
        // 호스트만 미션 초기화
        if (!Object.HasStateAuthority || Initialized)
            return;

        // 공통 미션 등록
        foreach (MissionData data in common)
            Add(data, PlayerRef.None, false);

        Initialized = true;
    }

    /// <summary>
    /// 플레이어마다 다른 개인 미션 배정한다
    /// </summary>
    public void AssignPersonal(PlayerRef player)
    {
        // 호스트만 배정, 이미 개인 미션이 있으면 종료
        if (!Object.HasStateAuthority || HasPersonal(player))
            return;

        // 아직 배정되지 않은 개인 미션 검색
        foreach (MissionData data in personal)
        {
            if (data == null || IsAssigned(data.MissionId))
                continue;

            Add(data, player, true);
            return;
        }

        Debug.LogWarning("배정 가능한 개인 미션 없음");
    }

    /// <summary>
    /// 플레이어가 이미 개인 미션을 가지고 있는지 확인한다
    /// </summary>
    private bool HasPersonal(PlayerRef player)
    {
        for (int i = 0; i < Count; i++)
        {
            MissionState mission = Missions[i];

            if (mission.IsPersonal && mission.Owner == player)
                return true;
        }

        return false;
    }

    /// <summary>
    /// 개인 미션이 다른 플레이어에게 배정됐는지 확인한다
    /// </summary>
    private bool IsAssigned(int id)
    {
        for (int i = 0; i < Count; i++)
        {
            MissionState mission = Missions[i];

            if (mission.IsPersonal && mission.MissionId == id)
                return true;
        }

        return false;
    }

    /// <summary>
    /// 새로운 공통 또는 개인 미션을 네트워크 배열에 등록한다
    /// </summary>
    private void Add(MissionData data, PlayerRef owner, bool isPersonal)
    {
        if (data == null)
            return;

        // 네트워크 배열 최대 크기 확인
        if (Count >= MaxCount)
        {
            Debug.LogWarning($"미션은 최대 {MaxCount}개까지 등록 가능");
            return;
        }

        // 미션의 초기 상태 생성
        Missions.Set(Count, new MissionState
        {
            MissionId = data.MissionId,
            Owner = owner,
            CurrentCount = 0,
            IsPersonal = isPersonal,
            IsCompleted = false
        });

        Count++;

        Debug.Log(
        $"{(isPersonal ? "개인" : "단체")} 미션 등록: " +
        $"{data.MissionName}, 담당자: {owner}");

    }

    /// <summary>
    /// 미션 진행도를 증가시키고 목표에 도달하면 완료 처리한다
    /// </summary>
    public void AddProgress(int id, PlayerRef player, int amount = 1)
    {
        // 호스트만 진행도를 변경하며, 0 이하는 허용하지 않음
        if (!Object.HasStateAuthority || amount <= 0)
            return;

        // 미션 원본 데이터 검색
        MissionData data = GetData(id);

        if (data == null)
            return;

        // 등록된 미션 중 ID가 같은 미션 검색
        for (int i = 0; i < Count; i++)
        {
            MissionState mission = Missions[i];

            if (mission.MissionId != id)
                continue;

            // 개인 미션은 담당 플레이어만 진행 가능
            if (mission.IsPersonal && mission.Owner != player)
                continue;

            // 이미 완료된 미션은 진행하지 않음
            if (mission.IsCompleted)
                return;

            // 목표 횟수를 넘지 않도록 진행도 증가
            mission.CurrentCount = Mathf.Min(
                mission.CurrentCount + amount,
                data.TargetCount
            );

            // 목표 횟수에 도달하면 완료 처리
            mission.IsCompleted =
                mission.CurrentCount >= data.TargetCount;

            // 변경된 구조체를 네트워크 배열에 다시 저장
            Missions.Set(i, mission);
            return;
        }
    }

    /// <summary>
    /// 미션 Id에 해당하는 MissionData를 반환한다
    /// </summary>
    public MissionData GetData(int id)
    {
        // 단체 미션에서 검색
        foreach (MissionData data in common)
        {
            if (data != null && data.MissionId == id)
                return data;
        }

        // 개인 미션에서 검색
        foreach (MissionData data in personal)
        {
            if (data != null && data.MissionId == id)
                return data;
        }

        return null;
    }
}