using System;
using Fusion;
using UnityEngine;

public class GameTimer : NetworkBehaviour
{
    [SerializeField, Min(0f)] private float gameDurationSeconds = 600f;

    [Networked] private TickTimer Countdown { get; set; }
    [Networked] public NetworkBool IsTimeUp { get; private set; }

    public float TotalDurationSeconds => gameDurationSeconds;

    public float RemainingSeconds
    {
        get
        {
            if (IsTimeUp)
                return 0f;

            if (Runner == null)
                return gameDurationSeconds;

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

        IsTimeUp = false;
        Countdown = TickTimer.CreateFromSeconds(Runner, gameDurationSeconds);
    }

    public override void FixedUpdateNetwork()
    {
        if (!HasStateAuthority || IsTimeUp || !Countdown.IsRunning)
            return;

        if (!Countdown.Expired(Runner))
            return;

        IsTimeUp = true;
        Countdown = TickTimer.None;
        OnTimeExpired?.Invoke();
    }
}
