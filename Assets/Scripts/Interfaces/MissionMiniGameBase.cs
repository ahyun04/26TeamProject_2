using Fusion;
using System;

public abstract class MissionMiniGameBase : NetworkBehaviour, IMissionMiniGame
{
    public bool IsCompleted { get; private set; }
    public event Action OnCompleted;

    public virtual void StartMission()
    {
    }

    public virtual void StopMission()
    {
    }

    protected void Complete()
    {
        if (IsCompleted) return;

        IsCompleted = true;
        OnCompleted?.Invoke();
    }
}