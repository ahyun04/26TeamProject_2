using Fusion;
using UnityEngine;


/// <summary>
/// 여러 먼지를 묶고 미션 완료 조건 판단
/// </summary>
public class FilterCleaningMission : MissionMiniGameBase
{
    [Header("청소할 먼지")]
    [SerializeField] private VacuumDust[] dusts;


    public override void Spawned()
    {
        foreach (VacuumDust dust in dusts)
        {
            if (dust != null)
                dust.Collected += OnDustCollected;
        }
    }


    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        foreach (VacuumDust dust in dusts)
        {
            if (dust != null)
                dust.Collected -= OnDustCollected;
        }
    }


    /// <summary>
    /// 먼지 하나가 청소되었을 때 호출
    /// </summary>
    private void OnDustCollected(PlayerRef player)
    {
        if (!HasStateAuthority || IsCompleted)
            return;

        if (ActivePlayer == PlayerRef.None)
            SetActivePlayer(player);

        if (ActivePlayer != player)
            return;

        if (!AreAllDustsCleaned())
            return;

        RequestComplete();
    }


    /// <summary>
    /// 모든 먼지가 청소됐는지 확인
    /// </summary>
    private bool AreAllDustsCleaned()
    {
        if (dusts == null || dusts.Length == 0)
            return false;

        foreach (VacuumDust dust in dusts)
        {
            if (dust == null || !dust.IsCleaned)
                return false;
        }

        return true;
    }
}