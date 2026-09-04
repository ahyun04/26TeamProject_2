using System;
using Fusion;
using UnityEngine;

public class GameTimer : NetworkBehaviour
{
    [SerializeField, Min(0f)] private float gameDurationSeconds = 600f;
    [SerializeField] private RoleAssignment roleAssignment;

    [Networked] private TickTimer Countdown { get; set; }
    [Networked] public NetworkBool IsStarted { get; private set; }
    [Networked] public NetworkBool IsTimeUp { get; private set; }

    public float TotalDurationSeconds => gameDurationSeconds;

    public float RemainingSeconds
    {
        get
        {
            if (Object == null || !Object.IsValid || Runner == null)
                return gameDurationSeconds;

            if (IsTimeUp)
                return 0f;

            if (!Countdown.IsRunning)
                return gameDurationSeconds;

            return Mathf.Max(0f, Countdown.RemainingTime(Runner) ?? 0f);
        }
    }

    public event Action OnTimeExpired;

    public override void Spawned()
    {
        if (!HasStateAuthority)
            return;

        if (roleAssignment == null)
            roleAssignment = FindFirstObjectByType<RoleAssignment>();

        if (roleAssignment == null)
            return;

        roleAssignment.OnRolesAssigned += StartTimer;

        if (roleAssignment.Initialized)
            StartTimer();
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        if (roleAssignment != null)
            roleAssignment.OnRolesAssigned -= StartTimer;
    }

    private void StartTimer()
    {
        if (!HasStateAuthority || IsStarted)
            return;

        IsStarted = true;
        IsTimeUp = false;

        if (gameDurationSeconds <= 0f)
        {
            IsTimeUp = true;
            Countdown = TickTimer.None;
            OnTimeExpired?.Invoke();
            return;
        }

        Countdown = TickTimer.CreateFromSeconds(Runner, gameDurationSeconds);
    }

    public override void FixedUpdateNetwork()
    {
        if (!HasStateAuthority || !IsStarted || IsTimeUp || !Countdown.IsRunning)
            return;

        if (!Countdown.Expired(Runner))
            return;

        IsTimeUp = true;
        Countdown = TickTimer.None;
        OnTimeExpired?.Invoke();
    }
}
