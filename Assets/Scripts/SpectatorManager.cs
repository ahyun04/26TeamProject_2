using Fusion;
using Fusion.Addons.SimpleKCC;
using UnityEngine;

namespace LockdownProtocol.Networking
{
    [RequireComponent(typeof(PlayerHealth))]
    public class SpectatorManager : NetworkBehaviour
    {
        [SerializeField] private float spectatorDelaySeconds = 5f;
        [SerializeField] private NetworkObject spectatorPrefab; //체력과 스태미나가 없는 관전자

        [Networked] public NetworkBool IsSpectator { get; private set; }
        [Networked] internal NetworkObject spectatorObject { get; private set; } //복제된 관전자 참조
        [Networked] private TickTimer TransitionTimer { get; set; }

        private PlayerHealth health; //관전 전환을 요청한 플레이어의 생존 상태
        private NetworkObject spawnedSpectator; //호스트가 정리할 관전자

        public override void Spawned() //생존 상태 참조 연결
        {
            health = GetComponent<PlayerHealth>();
        }

        public override void FixedUpdateNetwork()
        {
            if (!HasStateAuthority || health == null || !health.IsDead) return;
            if (!IsSpectator && TransitionTimer.IsRunning && TransitionTimer.Expired(Runner))
                EnterSpectatorMode();
        }

        public void BeginSpectatorTransition()
        {
            if (!HasStateAuthority || health == null || !health.IsDead ||
                IsSpectator || TransitionTimer.IsRunning) return;
            if (spectatorPrefab == null)
            {
                Debug.LogError("[SpectatorManager] 관전자 프리팹이 연결되지 않았습니다.");
                return;
            }
            TransitionTimer = TickTimer.CreateFromSeconds(Runner, spectatorDelaySeconds);
        }

        private void EnterSpectatorMode()
        {
            SimpleKCC playerKcc = GetComponent<SimpleKCC>(); //사망 위치와 시선 유지
            Vector2 lookRotation = playerKcc.GetLookRotation(); //사망 전 시선
            spawnedSpectator = Runner.Spawn(spectatorPrefab, playerKcc.Position,
                Quaternion.Euler(0f, lookRotation.y, 0f), Object.InputAuthority);
            SimpleKCC spectatorKcc = spawnedSpectator.GetComponent<SimpleKCC>(); //관전자 이동 컨트롤러
            spectatorKcc.AddLookRotation(lookRotation - spectatorKcc.GetLookRotation(), -80f, 80f);
            spectatorObject = spawnedSpectator;
            IsSpectator = true;
            TransitionTimer = TickTimer.None;
        }

        public override void Despawned(NetworkRunner runner, bool hasState) //퇴장과 씬 전환 시 관전자 정리
        {
            if (runner.IsServer && spawnedSpectator != null && spawnedSpectator.IsValid)
                runner.Despawn(spawnedSpectator);
            spawnedSpectator = null;
        }
    }
}
