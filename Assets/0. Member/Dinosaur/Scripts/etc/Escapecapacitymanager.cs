using Fusion;
using LockdownProtocol.Networking;
using UnityEngine;

/// <summary>
/// 탈출 가능 인원을 관리한다.
///
/// 게임 시작 시 1~현재 시민 수 사이에서 랜덤으로 결정하고, 공동 미션 완료/추리 이벤트 성공 시
/// IncreaseCapacity()로 증가시킨다. 미션/추리 시스템이 아직 없어 지금은 이 메서드를
/// 외부(테스트 코드 등)에서 수동으로만 호출 가능하다 - 정식 시스템이 나오면 그쪽에서 연결하면 된다.
///
/// 씬에 배치된 NetworkObject(예: GameManager)에 부착하는 것을 전제로 한다 - 플레이어 개인이 아니라
/// 게임 전체에 하나만 존재해야 하는 데이터이기 때문이다.
/// </summary>
public class EscapeCapacityManager : NetworkBehaviour
{
    [Networked] public int EscapeCapacity { get; private set; }
    [Networked] public int EscapeCount { get; private set; }

    /// <summary>남은 탈출 가능 인원. 별도로 동기화하지 않고 이미 동기화된 두 값에서 계산한다.</summary>
    public int EscapeAvailable => Mathf.Max(0, EscapeCapacity - EscapeCount);

    public override void Spawned()
    {
        if (!Object.HasStateAuthority) return;

        int citizenCount = CountCitizens();
        EscapeCapacity = citizenCount > 0 ? Random.Range(1, citizenCount + 1) : 0;
        EscapeCount = 0;

        Debug.Log($"[EscapeCapacityManager] 탈출 가능 인원 결정: {EscapeCapacity} (시민 {citizenCount}명 중)");
    }

    /// <summary>공동 미션 완료, 추리 이벤트 성공 등에서 호출해 탈출 가능 인원을 늘린다.</summary>
    public void IncreaseCapacity(int amount = 1)
    {
        if (!Object.HasStateAuthority) return;
        if (amount <= 0) return;

        int citizenCount = CountCitizens();
        EscapeCapacity = Mathf.Min(citizenCount, EscapeCapacity + amount);

        Debug.Log($"[EscapeCapacityManager] 탈출 가능 인원 증가: {EscapeCapacity}");
    }

    /// <summary>EscapeManager가 탈출 성공을 확정할 때 호출한다.</summary>
    public void ConsumeSlot()
    {
        if (!Object.HasStateAuthority) return;

        EscapeCount++;
    }

    private int CountCitizens()
    {
        int count = 0;
        foreach (var player in Runner.ActivePlayers)
        {
            var playerObj = Runner.GetPlayerObject(player);
            if (playerObj == null) continue;

            var killManager = playerObj.GetComponent<KillManager>();
            if (killManager != null && !killManager.IsMurderer)
            {
                count++;
            }
        }
        return count;
    }
}