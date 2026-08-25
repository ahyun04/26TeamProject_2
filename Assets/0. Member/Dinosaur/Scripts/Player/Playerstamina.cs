using System;
using Fusion;
using UnityEngine;

/// <summary>
/// 플레이어 스태미너 관리. 체력과 달리 외부 요청이 아니라 자기 자신의 입력(스프린트 여부)에 반응해
/// 매 틱 스스로 증감을 계산한다. PlayerMovement는 HasStamina만 조회해 스프린트 허용 여부를 판단한다.
/// </summary>
[RequireComponent(typeof(NetworkObject))]
public class PlayerStamina : NetworkBehaviour
{
    [Header("Stamina Settings")]
    [SerializeField] private float maxStamina = 100f;
    [SerializeField] private float drainPerSecond = 20f;
    [SerializeField] private float regenPerSecond = 15f;
    [SerializeField] private float regenDelaySeconds = 1.5f;

    [Networked, OnChangedRender(nameof(HandleStaminaChanged))]
    public float CurrentStamina { get; private set; }

    [Networked] private float RegenDelayTimer { get; set; }

    public float MaxStamina => maxStamina;

    /// <summary>스태미너가 0이면 false. PlayerMovement가 스프린트 허용 여부를 판단할 때 참조.</summary>
    public bool HasStamina => CurrentStamina > 0f;

    public event Action<float, float> StaminaChanged; // (current, max)

    public override void Spawned()
    {
        if (Object.HasStateAuthority)
        {
            CurrentStamina = maxStamina;
        }
    }

    public override void FixedUpdateNetwork()
    {
        if (!GetInput(out NetworkInputData input))
            return;

        bool isMoving = input.MoveDirection.sqrMagnitude > 0.01f;
        bool wantsSprint = input.IsPressed(InputButton.Sprint) && isMoving;

        if (wantsSprint && HasStamina)
        {
            Drain(drainPerSecond * Runner.DeltaTime);
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
