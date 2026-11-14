using Fusion;
using TrustNoOne.Missions;
using UnityEngine;

[RequireComponent(typeof(NetworkObject))]
public class CctvDoorComponent : NetworkBehaviour
{
    [SerializeField] private FireDoorVisual visual; //위아래 문짝 연출과 문짝 충돌
    [SerializeField, Min(0.1f)] private float travelSeconds = 1f; //한 방향으로 완전히 움직이는 시간

    [Networked] private NetworkBool targetClosed { get; set; } //호스트가 확정한 닫힘 목표 상태
    [Networked] private TickTimer travelTimer { get; set; } //모든 참가자가 공유하는 이동 종료 틱

    private bool spawned; //네트워크 상태 접근 여부

    internal bool isClosed => spawned && targetClosed;

    public override void Spawned() //라운드 시작 시 통로를 연 상태로 초기화
    {
        if (HasStateAuthority)
        {
            targetClosed = false;
            travelTimer = TickTimer.None;
        }
        spawned = true;
        applyPose();
    }

    internal void toggle() //호스트만 방향을 바꾸고 현재 위치에서 이어서 움직임
    {
        if (!spawned || Object == null || !Object.IsValid || !HasStateAuthority) return;

        float closed = getClosedProgress();
        targetClosed = !targetClosed;
        float remaining = targetClosed ? 1f - closed : closed;
        travelTimer = remaining > 0f
            ? TickTimer.CreateFromSeconds(Runner, remaining * travelSeconds)
            : TickTimer.None;
        applyPose();
    }

    public override void FixedUpdateNetwork() //시뮬레이션에서도 문짝 위치와 충돌 갱신
    {
        applyPose();
    }

    public override void Render() //다른 참가자 화면에서도 같은 문 상태 표시
    {
        applyPose();
    }

    private float getClosedProgress() //남은 틱을 양쪽 이동에 공통으로 적용
    {
        float remaining = Mathf.Clamp01(travelTimer.RemainingTime(Runner).GetValueOrDefault() / travelSeconds);
        return targetClosed ? 1f - remaining : remaining;
    }

    private void applyPose() //같은 문짝에 연결된 충돌체도 함께 이동
    {
        if (spawned) visual?.ShowOpen(1f - getClosedProgress());
    }

    public override void Despawned(NetworkRunner runner, bool hasState) //씬 전환 뒤 네트워크 상태 접근 차단
    {
        spawned = false;
    }
}
