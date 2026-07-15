using System;
using Fusion;
using UnityEngine;

namespace LockdownProtocol.Networking
{
    /// <summary>
    /// 플레이어 체력 관리. 값 변경은 반드시 State Authority(Host)에서만 일어난다.
    /// 데미지를 주는 쪽(함정, 공격 판정 등)이 이미 권한을 가진 컨텍스트에서 호출한다는 전제이며,
    /// 이 클래스는 이중 방어로 내부에서도 HasStateAuthority를 다시 확인한다.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    public class PlayerHealth : NetworkBehaviour
    {
        [Header("Health Settings")]
        [SerializeField] private float maxHealth = 100f;

        [Networked, OnChangedRender(nameof(HandleHealthChanged))]
        public float CurrentHealth { get; private set; }

        [Networked, OnChangedRender(nameof(HandleDeathStateChanged))]
        public NetworkBool IsDead { get; private set; }

        public float MaxHealth => maxHealth;

        /// <summary>UI, 사운드, 이펙트 등 외부 시스템이 구독해 체력 변화에 반응할 수 있도록 노출.</summary>
        public event Action<float, float> HealthChanged; // (current, max)
        public event Action Died;

        public override void Spawned()
        {
            // 초기값은 State Authority만 설정한다. 다른 클라이언트는 네트워크로 값을 복제받는다.
            if (Object.HasStateAuthority)
            {
                CurrentHealth = maxHealth;
                IsDead = false;
            }
        }

        public void ApplyDamage(float amount, PlayerRef source = default)
        {
            if (!Object.HasStateAuthority) return;
            if (IsDead || amount <= 0f) return;

            CurrentHealth = Mathf.Max(0f, CurrentHealth - amount);

            if (CurrentHealth <= 0f)
            {
                IsDead = true;
            }
        }

        public void Heal(float amount)
        {
            if (!Object.HasStateAuthority) return;
            if (IsDead || amount <= 0f) return;

            CurrentHealth = Mathf.Min(maxHealth, CurrentHealth + amount);
        }

        private void HandleHealthChanged()
        {
            HealthChanged?.Invoke(CurrentHealth, maxHealth);
        }

        private void HandleDeathStateChanged()
        {
            if (IsDead)
            {
                Died?.Invoke();
            }
        }

        /// 다현 추가 - 즉사 처리 전용 일반 데미지와 구분해 사망 원인을 명확히 남기기 위해 분리
        public void Kill(PlayerRef source = default)
        {
            if (!Object.HasStateAuthority) return;
            if (IsDead) return;

            CurrentHealth = 0f;
            IsDead = true;
        }

        // 테스트용, 나중에 삭제
        private void Update()
        {
            if (Object == null || !Object.IsValid) return;
            if (Object.HasStateAuthority && Input.GetKeyDown(KeyCode.K))
            {
                Kill();
            }
        }
    }
}