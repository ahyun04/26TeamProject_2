using Fusion;
using UnityEngine;

/// <summary>
/// 플레이어의 상호작용 입력을 받고
/// 현재 바라보는 대상에게 상호작용을 요청
/// </summary>
public class PlayerInteraction : NetworkBehaviour
{
    [SerializeField] private PlayerTargetDetector targetDetector;

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
        if (Input.GetMouseButtonDown(0) &&
            targetDetector.CurrentTarget is IInteractable interactable)
            interactable.Interact();
    }
}