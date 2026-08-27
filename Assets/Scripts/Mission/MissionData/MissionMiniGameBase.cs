using Fusion;
using System;
using UnityEngine;

/// <summary>
/// [ 모든 미션 미니게임이 상속하는 부모 클래스 ] 
/// 
/// 모든 미션 미니게임이 공통으로 가져야 하는 실행 틀과
/// 개별 미니게임 하나의 완료/실패 요청을 처리
/// </summary>
public abstract class MissionMiniGameBase : NetworkBehaviour
{

    [SerializeField] private MissionData missionData;


    // 해당 미션을 받은 플레이어는 Collider On
    // 받지 못하면 Collider Off 구현
    [SerializeField] private MissionSystem missionSystem;
    [SerializeField] private Collider[] interactionColliders;


    public int MissionId => missionData.Id;

    private bool interactionInitialized;


    // 미션 완료 상태를 모든 플레이어에게 동기화
    [Networked, OnChangedRender(nameof(OnCompletedChanged))]
    private NetworkBool MissionCompleted { get; set; }


    // 외부에서 완료 여부 확인
    public bool IsCompleted => MissionCompleted;

    private bool failRequested;

    // 미션 완료됐다고 다른 시스템에 알려주는 이벤트
    public event Func<int, PlayerRef, bool> OnCompleteRequested;

    // PersonalAction 실패 알림
    public event Func<int, PlayerRef, bool> OnFailRequested;


    public virtual void StartMission() { }  // 미션 시작

    
    public virtual void StopMission() { }   // 미션 도중 멈췄을 때

    
    protected virtual void FinishMission() { }  // 미션 성공 후 마무리


    public override void Render()
    {
        if (interactionInitialized)
            return;

        if (missionSystem == null || !missionSystem.Initialized)
            return;

        RefreshLocalInteraction();

        interactionInitialized = true;
    }


    /// <summary>
    /// 현재 로컬 플레이어가 이 미션을 수행할 수 있는지 확인
    /// </summary>
    private void RefreshLocalInteraction()
    {
        bool canInteract = missionSystem.HasMission(MissionId, Runner.LocalPlayer);

        SetInteractionEnabled(canInteract);
    }


    /// <summary>
    /// 이 미션에 등록된 모든 상호작용 Collider의 활성 상태를 변경하고
    /// true면 감지/클릭 가능, false면 감지 자체가 되지 않는다
    /// </summary>
    private void SetInteractionEnabled(bool enabled)
    {
        foreach (Collider interactionCollider in interactionColliders)
        {
            if (interactionCollider != null)
                interactionCollider.enabled = enabled;
        }
    }


    /// <summary>
    /// 자식 미션이 완료 요청
    /// </summary>
    protected void RequestComplete()
    {
        bool isShared = missionData.RoleTarget == MissionRoleTarget.All &&
                    missionData.MissionType == MissionType.Shared;

        if (isShared && MissionCompleted) return;

        // Host라면 바로 완료 요청 보냄
        if (Object.HasStateAuthority)
        {
            Complete(Runner.LocalPlayer);
            return;
        }

        // Guest라면 RPC 호출로 완료 요청 보냄
        RPC_RequestComplete();
    }


    /// <summary>
    /// 자식 미션이 실패 요청
    /// </summary>
    protected void RequestFail()
    {
        if (failRequested)
            return;

        if (Object.HasStateAuthority)
        {
            failRequested = Fail(Runner.LocalPlayer);
            return;
        }

        failRequested = true;
        RPC_RequestFail();
    }


    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_RequestComplete(RpcInfo info = default)
    {
        Complete(info.Source);
    }


    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_RequestFail(RpcInfo info = default)
    {
        Fail(info.Source);
    }


    /// <summary>
    /// Host가 미션 완료 확정
    /// </summary>
    private void Complete(PlayerRef player)
    {
        if (!Object.HasStateAuthority || MissionCompleted) return;

        bool accepted = OnCompleteRequested?.Invoke(MissionId, player) ?? false;

        if (!accepted)
            return;

        // Shared 미션만 월드 오브젝트 자체를 완료 상태로 만든다.
        if (missionData.RoleTarget == MissionRoleTarget.All &&
            missionData.MissionType == MissionType.Shared)
        {
            MissionCompleted = true;

            FinishMission();
        }

        Debug.Log($" ID : {MissionId} 미션 완료");
    }


    /// <summary>
    /// Host가 실패 요청을 MissionSystem에 전달
    /// 실제 실패 상태는 MissionState가 관리
    /// </summary>
    private bool Fail(PlayerRef player)
    {
        if (!Object.HasStateAuthority)
            return false;

        bool accepted = OnFailRequested?.Invoke(MissionId, player) ?? false;

        if (!accepted)
            return false;

        Debug.Log($"ID : {MissionId} 미션 실패 요청 / Player : {player}");

        return true;
    }


    /// <summary>
    /// 동기화된 완료 상태 변경 시 Guest에서 결과 반영
    /// </summary>
    private void OnCompletedChanged()
    {
        if (Object.HasStateAuthority || !MissionCompleted) return;

        FinishMission();
    }

}