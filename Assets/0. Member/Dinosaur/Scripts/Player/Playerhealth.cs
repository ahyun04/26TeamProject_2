using System;
using Fusion;
using UnityEngine;

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

    /// <summary>
    /// 탈출 성공 상태. 사망과는 독립적인 별개의 종료 상태다 - 탈출한 플레이어는
    /// 죽은 것도 아니고 생존 중인 것도 아닌, "게임에서 이탈해 승리한" 세 번째 상태를 갖는다.
    /// </summary>
    [Networked, OnChangedRender(nameof(HandleEscapeStateChanged))]
    public NetworkBool IsEscaped { get; private set; }

    public float MaxHealth => maxHealth;
    private GameEndSystem gameEndSystem;

    internal bool CanAct
    {
        get
        {
            if (Object == null || !Object.IsValid || IsDead || IsEscaped) return false;
            if (gameEndSystem == null) gameEndSystem = FindFirstObjectByType<GameEndSystem>();
            return gameEndSystem == null || gameEndSystem.Object == null || !gameEndSystem.Object.IsValid || !gameEndSystem.IsGameEnded;
        }
    }

    /// <summary>UI, 사운드, 이펙트 등 외부 시스템이 구독해 체력 변화에 반응할 수 있도록 노출.</summary>
    public event Action<float, float> HealthChanged; // (current, max)
    public event Action Died;
    public event Action Escaped;

    public override void Spawned()
    {
        // 초기값은 State Authority만 설정한다. 다른 클라이언트는 네트워크로 값을 복제받는다.
        if (Object.HasStateAuthority)
        {
            CurrentHealth = maxHealth;
            IsDead = false;
            IsEscaped = false;
        }
    }

    public void ApplyDamage(float amount, PlayerRef source = default)
    {
        if (!Object.HasStateAuthority) return;
        if (IsDead || IsEscaped || amount <= 0f) return;

        CurrentHealth = Mathf.Max(0f, CurrentHealth - amount);

        if (CurrentHealth <= 0f)
        {
            IsDead = true;
            Died?.Invoke();
        }
    }

    public void Heal(float amount)
    {
        if (!Object.HasStateAuthority) return;
        if (IsDead || IsEscaped || amount <= 0f) return;

        CurrentHealth = Mathf.Min(maxHealth, CurrentHealth + amount);
    }

    private void HandleHealthChanged()
    {
        HealthChanged?.Invoke(CurrentHealth, maxHealth);
    }

    private void HandleDeathStateChanged()
    {
        if (IsDead && !Object.HasStateAuthority)
        {
            Died?.Invoke();
        }
    }

    private void HandleEscapeStateChanged()
    {
        if (IsEscaped && !Object.HasStateAuthority)
        {
            Escaped?.Invoke();
        }
    }

    /// 다현 추가 - 즉사 처리 전용 일반 데미지와 구분해 사망 원인을 명확히 남기기 위해 분리
    public void Kill(PlayerRef source = default)
    {
        if (!Object.HasStateAuthority) return;
        if (IsDead || IsEscaped) return;

        CurrentHealth = 0f;
        IsDead = true;
        Died?.Invoke();
    }

    /// <summary>
    /// 탈출 성공 처리. EscapeManager가 탈출 가능 조건을 전부 검증한 뒤 호출하는 것을 전제로 하므로,
    /// 여기서는 "죽어있지 않은지"만 이중 확인한다. 기획서의 "탈출 직전 사망 -> 사망 처리 우선" 규칙이
    /// 이 체크로 자연스럽게 지켜진다 - 같은 틱에 Kill()이 먼저 호출됐다면 IsDead가 true가 되어 있어
    /// Escape()가 막힌다.
    /// </summary>
    public void Escape()
    {
        if (!Object.HasStateAuthority) return;
        if (IsDead || IsEscaped) return;

        IsEscaped = true;
        Escaped?.Invoke();
    }

}
