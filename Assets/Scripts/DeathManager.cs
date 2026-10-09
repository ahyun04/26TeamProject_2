using Fusion;
using Fusion.Addons.SimpleKCC;
using UnityEngine;

namespace LockdownProtocol.Networking
{
    [RequireComponent(typeof(PlayerHealth))]
    public class DeathManager : NetworkBehaviour
    {
        private PlayerHealth _health;
        private SpectatorManager _spectator;
        private PlayerStamina stamina; //대기실 부활 시 회복할 스태미나

        public override void Spawned()
        {
            _health = GetComponent<PlayerHealth>();
            _spectator = GetComponent<SpectatorManager>();
            stamina = GetComponent<PlayerStamina>();
            _health.Died += HandleDeath;
            _health.revived += handleRevived;
            if (_health.IsDead) HandleDeath();
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            if (_health != null)
            {
                _health.Died -= HandleDeath;
                _health.revived -= handleRevived;
            }
        }

        private void HandleDeath()
        {
            if (HasInputAuthority)
            {
                GetComponent<PlayerInteraction>()?.cancelInteraction();
                GetComponent<PlayerTargetDetector>()?.clearTarget();
                GetComponent<PlayerFirstPersonItemView>()?.Clear();
            }

            //아이템 드롭은 PlayerItemController, 사망 모션은 PlayerAnimation이 처리한다.
            if (Object.HasStateAuthority)
            {
                if (Runner.SceneManager != null && Runner.SceneManager.MainRunnerScene.name == "StandBy" &&
                    _health.tryRevive())
                {
                    stamina?.restoreAfterRevive();
                    return;
                }
                _spectator?.BeginSpectatorTransition();
            }
        }

        private void handleRevived() //부활한 캐릭터의 시뮬레이션과 로컬 이동 복원
        {
            if (HasStateAuthority || HasInputAuthority)
                GetComponent<SimpleKCC>()?.SetActive(true);
        }
    }
}
