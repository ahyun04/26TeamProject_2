using System;
using Fusion;
using UnityEngine;

/// <summary>
/// 탈출구 개방 조건을 판정한다: 게임 시간 2/3 경과 AND 탈출구 개방 미션 완료 (둘 다 필요).
/// </summary>
public class EscapeGateManager : NetworkBehaviour
{
    [SerializeField] private GameTimer gameTimer;

    [Networked] public NetworkBool IsMissionComplete { get; private set; }

    [Networked, OnChangedRender(nameof(HandleGateOpened))]
    public NetworkBool IsOpen { get; private set; }

    /// <summary>기획서에 명시된 이벤트 이름(OnEscapeOpened)을 그대로 사용한다.</summary>
    public event Action OnEscapeOpened;

    public override void Spawned()
    {
        if (gameTimer == null)
            gameTimer = FindFirstObjectByType<GameTimer>();
    }

    public override void FixedUpdateNetwork()
    {
        if (!Object.HasStateAuthority) return;
        if (IsOpen) return; // 이미 열렸으면 더 체크할 필요 없음

        if (IsTimeConditionMet() && IsMissionComplete)
        {
            IsOpen = true;
        }
    }

    private bool IsTimeConditionMet()
    {
        return gameTimer != null &&
               gameTimer.RemainingSeconds <= gameTimer.TotalDurationSeconds / 3f;
    }

    /// <summary>미션 시스템이 나오면 탈출구 개방 미션 완료 시 이 메서드를 호출하도록 연결한다.</summary>
    public void SetMissionComplete()
    {
        if (!Object.HasStateAuthority) return;
        IsMissionComplete = true;
    }

    private void HandleGateOpened()
    {
        if (IsOpen)
        {
            OnEscapeOpened?.Invoke();
            Debug.Log("[EscapeGateManager] 탈출구 개방됨");
        }
    }

    // ===== 테스트용, 나중에 삭제 =====
    private void Update()
    {
        if (Object == null || !Object.IsValid || !Object.HasStateAuthority) return;

        if (Input.GetKeyDown(KeyCode.O))
        {
            SetMissionComplete();
            Debug.Log("[EscapeGateManager] (테스트) 미션 완료 처리");
        }
    }
}
