using System;
using Fusion;
using LockdownProtocol.Networking;
using UnityEngine;

/// <summary>
/// 탈출 관련 게임 종료 조건을 검사한다.
///
/// 정식 게임 종료 시스템(승리 UI, 결과 화면 등)이 아직 없어, 여기서는 "조건이 성립했다"는
/// 이벤트만 쏘는 뼈대만 만든다. 게임 종료 시스템이 나오면 이 이벤트를 구독해서 실제
/// 결과 화면 전환 등을 처리하면 된다.
///
/// EscapeCapacityManager와 같은 GameManager 오브젝트에 부착한다.
/// EscapeInteractionController가 탈출을 확정할 때마다 CheckGameEndCondition()을 호출한다.
/// </summary>
public class EscapeResultManager : NetworkBehaviour
{
    private EscapeCapacityManager _capacityManager;

    /// <summary>탈출 가능 인원이 다 찼을 때 발생 (더 이상 탈출로는 승리할 시민이 없음).</summary>
    public event Action OnEscapeCapacityFull;

    /// <summary>남아있는 시민이 전부 탈출했거나 사망했을 때 발생 (게임 종료 시점 후보).</summary>
    public event Action OnAllCitizensResolved;

    public override void Spawned()
    {
        _capacityManager = GetComponent<EscapeCapacityManager>();
    }

    /// <summary>탈출이 하나 성사될 때마다 호출한다.</summary>
    public void CheckGameEndCondition()
    {
        if (!Object.HasStateAuthority) return;

        if (_capacityManager != null && _capacityManager.EscapeAvailable <= 0)
        {
            OnEscapeCapacityFull?.Invoke();
            Debug.Log("[EscapeResultManager] 탈출 가능 인원 소진");
        }

        if (AreAllCitizensResolved())
        {
            OnAllCitizensResolved?.Invoke();
            Debug.Log("[EscapeResultManager] 모든 시민이 탈출/사망 - 게임 종료 조건 검사 필요");
        }
    }

    private bool AreAllCitizensResolved()
    {
        foreach (var player in Runner.ActivePlayers)
        {
            var playerObj = Runner.GetPlayerObject(player);
            if (playerObj == null) continue;

            var killManager = playerObj.GetComponent<KillManager>();
            if (killManager != null && killManager.IsMurderer) continue; // 시민만 확인

            var health = playerObj.GetComponent<PlayerHealth>();
            if (health == null) continue;

            if (!health.IsDead && !health.IsEscaped)
            {
                return false; // 아직 활동 중인 시민이 있음
            }
        }
        return true;
    }
}