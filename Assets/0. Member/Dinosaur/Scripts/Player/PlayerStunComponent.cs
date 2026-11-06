using Fusion;
using UnityEngine;

[RequireComponent(typeof(PlayerHealth))]
public class PlayerStunComponent : NetworkBehaviour
{
    [SerializeField] private ElectricEffectComponent effect; //스턴 중 표시할 로컬 전기 연출
    [Networked] private TickTimer stunTimer { get; set; } //호스트가 확정하고 복제하는 스턴 종료 시각

    private PlayerHealth health; //생존 상태와 부활 알림
    private bool spawned; //Spawn 이전 네트워크 속성 접근 방지

    internal bool isStunned => spawned && Object != null && Object.IsValid &&
        health.canActWithoutCctv && !stunTimer.ExpiredOrNotRunning(Runner); //행동 제한에 사용할 현재 상태

    public override void Spawned() //권한별 상태 초기화와 생명주기 연결
    {
        health = GetComponent<PlayerHealth>();
        if (HasStateAuthority) stunTimer = TickTimer.None;
        spawned = true;
        health.Died += clearStun;
        health.Escaped += clearStun;
        health.revived += clearStun;
    }

    internal void applyStun(float seconds) //재피격은 현재 시각부터 갱신하고 시간을 합산하지 않음
    {
        if (!spawned || !HasStateAuthority || !health.canActWithoutCctv ||
            seconds <= 0f || float.IsNaN(seconds) || float.IsInfinity(seconds)) return;
        stunTimer = TickTimer.CreateFromSeconds(Runner, seconds);
    }

    public override void Render() //동기화된 상태에 맞춰 각 화면에서 전기 연출만 처리
    {
        if (effect != null) effect.setLooping(isStunned);
    }

    private void clearStun() //사망·탈출·부활 시 남은 제한과 연출 제거
    {
        if (spawned && Object != null && Object.IsValid && HasStateAuthority) stunTimer = TickTimer.None;
        effect?.stop();
    }

    public override void Despawned(NetworkRunner runner, bool hasState) //씬 전환과 퇴장 시 구독 및 로컬 효과 정리
    {
        spawned = false;
        if (health != null)
        {
            health.Died -= clearStun;
            health.Escaped -= clearStun;
            health.revived -= clearStun;
        }
        effect?.stop();
    }

    private void OnDisable() //오브젝트 비활성화 시 남은 파티클 제거
    {
        effect?.stop();
    }
}
