using Fusion;
using System;

/// <summary>
/// IMissionMiniGame 인터페이스에 함수가 많으면
/// 각각의 미션 스크립트에 함수 다 써야해서 
/// 코드가 길어지기 때문에
/// abstract class로 반복되는 것을 하나로 묶음
/// </summary>
public abstract class MissionMiniGameBase : NetworkBehaviour, IMissionMiniGame
{
    [Networked, OnChangedRender(nameof(OnOnCompletedChanged))]
    private NetworkBool NetworkCompleted { get; set; }

    public bool IsCompleted => NetworkCompleted;
    public event Action OnCompleted;


    public virtual void StartMission()
    {
    }

    public virtual void StopMission()
    {
    }

    protected void Complete()
    {
        if (!Object.HasStateAuthority || NetworkCompleted) return;

        NetworkCompleted = true;
        OnCompleted?.Invoke();
    }

    private void OnOnCompletedChanged()
    {
        if (Object.HasStateAuthority || !NetworkCompleted) return;

        OnCompleted?.Invoke();
    }
}