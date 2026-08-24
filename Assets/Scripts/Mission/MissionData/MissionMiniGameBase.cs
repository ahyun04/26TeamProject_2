using Fusion;
using System;
using UnityEngine;

/// <summary>
/// [ 모든 미션 미니게임이 상속하는 부모 클래스 ] 
/// 
/// 모든 미션 미니게임이 공통으로 가져야 하는 실행 틀과
/// 개별 미니게임 하나의 완료를 처리
/// </summary>
public abstract class MissionMiniGameBase : NetworkBehaviour
{

    [SerializeField] private MissionData missionData;

    public int MissionId => missionData.Id;


    // 미션 완료 상태를 모든 플레이어에게 동기화
    [Networked, OnChangedRender(nameof(OnCompletedChanged))]
    private NetworkBool MissionCompleted { get; set; }


    // 외부에서 완료 여부 확인
    public bool IsCompleted => MissionCompleted;


    // 미션 완료됐다고 다른 시스템에 알려주는 이벤트
    public event Action<int> OnCompleted;


    public virtual void StartMission() { }  // 미션 시작

    
    public virtual void StopMission() { }   // 미션 도중 멈췄을 때

    
    protected virtual void FinishMission() { }  // 미션 성공 후 마무리


    /// <summary>
    /// 자식 미션이 완료 요청
    /// </summary>
    protected void RequestComplete()
    {
        if (MissionCompleted) return;

        // Host라면 바로 완료 요청 보냄
        if (Object.HasStateAuthority)
        {
            Complete();
            return;
        }

        // Guest라면 RPC 호출로 완료 요청 보냄
        RPC_RequestComplete();
    }


    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_RequestComplete()
    {
        Complete();
    }


    /// <summary>
    /// Host가 미션 완료 확정
    /// </summary>
    private void Complete()
    {
        if (!Object.HasStateAuthority || MissionCompleted) return;

        MissionCompleted = true;

        FinishMission();

        // "미션 Id의 미니게임이 끝났다" 라고 외부에 알림
        OnCompleted?.Invoke(MissionId);

        Debug.Log($" ID : {MissionId} 미션 완료");
    }


    /// <summary>
    /// 동기화된 미션 완료 상태가 변경됐을 때
    /// Guest에서 완료 결과를 반영
    /// </summary>
    private void OnCompletedChanged()
    {
        if (Object.HasStateAuthority || !MissionCompleted) return;

        FinishMission();
    }

}