using Fusion;
using TrustNoOne.Missions;
using UnityEngine;

/// <summary>
/// [테스트 전용] F 한 번으로 "이 행동을 했다"는 사실을 바로 알리는 큐브.
///  예) 의료 키트 사용, 아이템 제작, 아이템 전달, 거짓말 탐지기 사용, 시체 발견.
///
/// [MissionInteractable 과 다른 점]
///  이 행동들은 실제 게임에서 미션 오브젝트가 아니라 아이템 시스템·거짓말 탐지기 등 다른 시스템이 알릴 사실이다.
///  그래서 "해당 미션이 있어야만 상호작용 가능"이라는 제한이 없어야 한다.
///  특히 "누구에게도 아이템 주지 않기" 같은 행동 목표를 **어기는 것**도 테스트해야 하므로,
///  미션과 무관하게 언제든 누를 수 있게 했다.
///
/// [수행자] RPC 의 RpcInfo.Source 로 정한다 (클라이언트가 남의 이름을 쓸 수 없음 — MissionInteractable 과 같은 규칙).
/// </summary>
public class TestActionCube : NetworkBehaviour, ITargetable, IHoldInteractable
{
    [SerializeField] private MissionEventType actionEvent = MissionEventType.None;
    [SerializeField] private int targetId;

    private MissionManager manager;

    public NetworkObject TargetObject => Object;

    public override void Spawned()
    {
        manager = FindFirstObjectByType<MissionManager>();
    }

    public void BeginHold()
    {
        RPC_Perform();
    }

    public void EndHold() { }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority, HostMode = RpcHostMode.SourceIsHostPlayer)]
    private void RPC_Perform(RpcInfo info = default)
    {
        if (manager == null)
            manager = FindFirstObjectByType<MissionManager>();

        if (manager == null || info.Source.IsNone)
            return;

        manager.Publish(new MissionEvent(actionEvent, info.Source, targetId));

        Debug.Log($"[TestActionCube] 행동 발생: {actionEvent} (by {info.Source})");
    }
}
