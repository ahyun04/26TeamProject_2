using System;
using Fusion;
using UnityEngine;

/// <summary>
/// 플레이어 스태미너 관리. 체력과 달리 외부 요청이 아니라 자기 자신의 입력(스프린트 여부)에 반응해
/// PlayerMovement가 매 틱 실제 이동 가능 상태와 입력을 전달하며 스프린트 허용 여부를 함께 계산한다.
/// </summary>
[RequireComponent(typeof(NetworkObject))]
public class PlayerStamina : NetworkBehaviour
{
    [Header("Stamina Settings")]
    [SerializeField] private float maxStamina = 100f;
    [SerializeField] private float drainPerSecond = 20f;
    [SerializeField, Min(0f)] private float healthDrainPerSecond = 10f; //스태미나 소진 후 초당 체력 소모량
    [SerializeField] private float regenPerSecond = 15f;
    [SerializeField] private float regenDelaySeconds = 1.5f;

    [Networked, OnChangedRender(nameof(HandleStaminaChanged))]
    public float CurrentStamina { get; private set; }

    [Networked] private float RegenDelayTimer { get; set; }

    private PlayerHealth health; //달리기에 사용할 체력

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
