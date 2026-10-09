using System;
using Fusion;
using UnityEngine;

/// <summary>
/// 플레이어 스태미너 관리. PlayerMovement가 실제 이동 입력과 점프 실행을 전달하며
/// 스프린트·점프·공격에 사용할 스태미나 및 부족한 자원의 체력 소모를 처리한다.
/// </summary>
[RequireComponent(typeof(NetworkObject))]
public class PlayerStamina : NetworkBehaviour
{
    [Header("Stamina Settings")]
    [SerializeField] private float maxStamina = 100f;
    [SerializeField] private float drainPerSecond = 20f;
    [SerializeField, Min(0f)] private float healthDrainPerSecond = 10f; //스태미나 소진 후 초당 체력 소모량
    [SerializeField, Min(0f)] private float jumpStaminaCost = 15f; //점프 한 번에 필요한 스태미나
    [SerializeField, Min(0f)] private float attackStaminaCost = 25f; //공격 한 번에 필요한 스태미나
    [SerializeField] private float regenPerSecond = 15f;
    [SerializeField] private float regenDelaySeconds = 1.5f;

    [Networked, OnChangedRender(nameof(HandleStaminaChanged))]
    public float CurrentStamina { get; private set; }

    [Networked] private float RegenDelayTimer { get; set; }

    private PlayerHealth health; //스태미나가 부족할 때 사용할 체력

    public float MaxStamina => maxStamina;

    /// <summary>사용할 스태미너가 남아 있는지 확인.</summary>
    public bool HasStamina => CurrentStamina > 0f;

    public event Action<float, float> StaminaChanged; // (current, max)

    public override void Spawned()
    {
        health = GetComponent<PlayerHealth>();
        if (Object.HasStateAuthority)
        {
            CurrentStamina = maxStamina;
        }
    }

    internal bool updateStamina(NetworkInputData input, bool canMove) //실제 스프린트 조건에 맞춰 소모와 회복을 처리
    {
        if (!HasStateAuthority && !HasInputAuthority)
            return false;

        bool isMoving = input.MoveDirection.sqrMagnitude > 0.0001f; //이동 입력 여부
        bool canSprint = HasStamina || (health != null && health.CurrentHealth > 0f); //체력이 남으면 달리기 지속
        bool isSprinting = canMove && isMoving && canSprint && (health == null || health.CanAct) &&
                           input.IsPressed(InputButton.Sprint) && !input.IsPressed(InputButton.Crouch); //이번 틱의 달리기 여부

        if (isSprinting)
        {
            float staminaCost = drainPerSecond * Runner.DeltaTime; //이번 틱의 스태미나 소모량
            float healthSprintSeconds = HasStamina
                ? (drainPerSecond > 0f ? Mathf.Max(0f, staminaCost - CurrentStamina) / drainPerSecond : 0f)
                : Runner.DeltaTime; //스태미나로 감당하지 못한 달리기 시간
            Drain(staminaCost);
            if (healthSprintSeconds > 0f)
                health?.consumeSprintHealth(healthDrainPerSecond * healthSprintSeconds);
            RegenDelayTimer = regenDelaySeconds;
        }
        else if (RegenDelayTimer > 0f)
        {
            RegenDelayTimer -= Runner.DeltaTime;
        }
        else
        {
            Regen(regenPerSecond * Runner.DeltaTime);
        }
        return isSprinting && (health == null || !health.IsDead);
    }

    internal bool tryConsumeJumpStamina() //점프 자원을 소모하고 생존 여부를 반환
    {
        if ((!HasStateAuthority && !HasInputAuthority) || (health != null && !health.CanAct))
            return false;

        float healthCost = Mathf.Max(0f, jumpStaminaCost - CurrentStamina); //스태미나로 감당하지 못한 점프 소모량
        bool survivesJump = health == null || health.CurrentHealth > healthCost; //클라이언트도 치명적인 점프를 미리 차단
        Drain(jumpStaminaCost);
        RegenDelayTimer = regenDelaySeconds;
        if (healthCost > 0f)
            health?.ApplyDamage(healthCost);
        return survivesJump && (health == null || !health.IsDead);
    }

    internal bool tryConsumeAttackStamina() //호스트가 공격 자원을 소모하고 생존 여부를 반환
    {
        if (!HasStateAuthority || health == null || !health.CanAct)
            return false;

        float healthCost = Mathf.Max(0f, attackStaminaCost - CurrentStamina); //스태미나로 감당하지 못한 공격 소모량
        Drain(attackStaminaCost);
        RegenDelayTimer = regenDelaySeconds;
        if (healthCost > 0f)
            health.ApplyDamage(healthCost);
        return health.CanAct;
    }

    internal void restoreAfterRevive() //호스트의 부활 후 최대 스태미나와 회복 대기 초기화
    {
        if (!HasStateAuthority) return;
        CurrentStamina = maxStamina;
        RegenDelayTimer = 0f;
    }

    private void Drain(float amount)
    {
        CurrentStamina = Mathf.Max(0f, CurrentStamina - amount);
    }

    private void Regen(float amount)
    {
        CurrentStamina = Mathf.Min(maxStamina, CurrentStamina + amount);
    }

    private void HandleStaminaChanged()
    {
        StaminaChanged?.Invoke(CurrentStamina, maxStamina);
    }
}
