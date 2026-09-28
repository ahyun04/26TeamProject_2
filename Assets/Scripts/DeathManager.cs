using Fusion;
using UnityEngine;

namespace LockdownProtocol.Networking
{
    [RequireComponent(typeof(PlayerHealth))]
    public class DeathManager : NetworkBehaviour
    {
        private PlayerHealth _health;
        private SpectatorManager _spectator;

        public override void Spawned()
        {
            _health = GetComponent<PlayerHealth>();
            _spectator = GetComponent<SpectatorManager>();
            _health.Died += HandleDeath;
            if (_health.IsDead) HandleDeath();
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            if (_health != null)
                _health.Died -= HandleDeath;
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
                _spectator?.BeginSpectatorTransition();
            }
        }
    }
}
