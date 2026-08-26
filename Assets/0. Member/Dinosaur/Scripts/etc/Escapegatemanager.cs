using System;
using Fusion;
using UnityEngine;

/// <summary>
/// 탈출구 개방 조건을 판정한다: 게임 시간 2/3 경과 AND 탈출구 개방 미션 완료 (둘 다 필요).
///
/// 게임 타이머 시스템과 미션 시스템이 아직 없어, 이 클래스가 자체적으로 간단한 타이머를 갖고
/// 미션 완료는 임시로 수동 설정(SetMissionComplete)만 가능하다. 정식 시스템이 나오면
/// 타이머는 그쪽 값을 참조하도록, 미션 완료는 그쪽이 SetMissionComplete()를 호출하도록
/// 교체하면 된다.
///
/// EscapeCapacityManager와 마찬가지로 GameManager 같은 씬 배치 NetworkObject에 부착한다.
/// </summary>
public class EscapeGateManager : NetworkBehaviour
{
    [Header("Timer (임시 - 정식 게임 타이머 시스템 나오면 교체)")]
    [SerializeField] private float totalGameDurationSeconds = 600f; // 10분 기준 임시값

    [Networked] private TickTimer GameTimer { get; set; }
    [Networked] public NetworkBool IsMissionComplete { get; private set; }

    [Networked, OnChangedRender(nameof(HandleGateOpened))]
    public NetworkBool IsOpen { get; private set; }

    /// <summary>기획서에 명시된 이벤트 이름(OnEscapeOpened)을 그대로 사용한다.</summary>
    public event Action OnEscapeOpened;

    public override void Spawned()
    {
        if (Object.HasStateAuthority)
        {
            GameTimer = TickTimer.CreateFromSeconds(Runner, totalGameDurationSeconds);
        }
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
        // TickTimer는 "남은 시간" 기준이므로, 전체의 2/3 지점은
        // "남은 시간이 전체의 1/3 이하로 줄었을 때"로 계산한다.
        float? remaining = GameTimer.RemainingTime(Runner);
        if (remaining == null) return true; // 타이머가 이미 만료됨 = 시간 조건 만족

        return remaining.Value <= totalGameDurationSeconds / 3f;
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