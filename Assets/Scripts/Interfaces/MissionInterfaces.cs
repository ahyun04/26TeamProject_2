using System;

public interface IMissionComplete   // 완료했다는 알림
{
    bool IsCompleted { get; }
    event Action OnCompleted;
}

public interface IMissionMiniGame : IMissionComplete    // 미션 미니게임 규칙
{
    void StartMission();
    void StopMission();
}