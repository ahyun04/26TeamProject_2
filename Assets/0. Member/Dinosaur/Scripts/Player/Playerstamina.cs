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

    internal bool updateStamina(NetworkInputData input, bool canMove) //실제 스프린트 조건에 맞춰 소모와 회복을 처리
    {
        if (!HasStateAuthority && !HasInputAuthority)
            return false;

        bool isMoving = input.MoveDirection.sqrMagnitude > 0.0001f; //이동 입력 여부
        bool isSprinting = canMove && isMoving && HasStamina &&
                           input.IsPressed(InputButton.Sprint) && !input.IsPressed(InputButton.Crouch); //이번 틱의 달리기 여부

        if (isSprinting)
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
        return isSprinting;
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
