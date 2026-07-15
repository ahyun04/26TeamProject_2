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
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            if (_health != null)
                _health.Died -= HandleDeath;
        }

        private void HandleDeath()
        {
            // 클라 전용 연출
            PlayDeathAnimation();
            ScreenFadeOut();
            DisableInputUI();

            // 서버 전용 
            if (Object.HasStateAuthority)
            {
                SpawnBody();
                DropItems();
                _spectator.BeginSpectatorTransition();
            }
        }

        // Animator 세팅되면 구현
        private void PlayDeathAnimation()
        {
            Debug.Log($"[DeathManager] {name} 사망 애니메이션 재생 (미구현)");
        }

        // 화면 암전 UI 생기면 구현
        private void ScreenFadeOut()
        {
            Debug.Log($"[DeathManager] {name} 화면 암전 (미구현)");
        }

        private void DisableInputUI()
        {
            Debug.Log($"[DeathManager] {name} 입력 UI 비활성화 (미구현)");
        }

        // 시체 프리팹 생기면 구현
        private void SpawnBody()
        {
            Debug.Log($"[DeathManager] {name} 시체 생성 (미구현) - 서버 전용");
        }

        // 인벤토리 시스템 기획/구현 후 연결
        private void DropItems()
        {
            Debug.Log($"[DeathManager] {name} 아이템 드랍 (미구현) - 서버 전용");
        }
    }
}