using Fusion;
using UnityEngine;

/// <summary>
/// 플레이어의 상호작용 입력을 받고
/// 현재 바라보는 대상에게 상호작용을 요청
/// </summary>
public class PlayerInteraction : NetworkBehaviour
{
    [SerializeField] private PlayerTargetDetector targetDetector;

    private IHoldInteractable activeHoldTarget;


    public override void Spawned()
    {
        enabled = HasInputAuthority;

        if (!enabled)
            return;

        if (targetDetector == null)
            targetDetector = GetComponent<PlayerTargetDetector>();

        if (targetDetector == null)
        {
            Debug.LogError("[PlayerInteraction] PlayerTargetDetector가 없습니다.");
            enabled = false;
        }
    }

    private void Update()
    {
        HandleClickInteraction();

        HandleHoldInteraction();

    }

    /// <summary>
    /// 좌클릭 한 번으로 사용하는 상호작용 처리
    /// </summary>
    private void HandleClickInteraction()
    {
        if (!Input.GetMouseButtonDown(0))
            return;

        if (targetDetector.CurrentTarget is IInteractable interactable)
            interactable.Interact();
    }


    /// <summary>
    /// F키를 누르고 있는 동안 사용하는 상호작용 처리
    /// </summary>
    private void HandleHoldInteraction()
    {
        // F를 누른 상태에서 다른 곳을 바라보면 기존 상호작용 중지
        if (activeHoldTarget != null &&
            !ReferenceEquals(targetDetector.CurrentTarget, activeHoldTarget))
            EndHoldInteraction();

        // F를 처음 눌렀을 때
        if (Input.GetKeyDown(KeyCode.F) &&
            targetDetector.CurrentTarget is IHoldInteractable holdInteractable)
        {
            activeHoldTarget = holdInteractable;

            activeHoldTarget.BeginHold();
        }

        // F를 뗐을 때
        if (Input.GetKeyUp(KeyCode.F))
            EndHoldInteraction();
    }


    /// <summary>
    /// 현재 진행 중인 홀드 상호작용 종료
    /// </summary>
    private void EndHoldInteraction()
    {
        if (activeHoldTarget == null)
            return;

        activeHoldTarget.EndHold();

        activeHoldTarget = null;
    }


    private void OnDisable()
    {
        EndHoldInteraction();
    }
}