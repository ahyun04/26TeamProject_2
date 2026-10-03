using System;
using Fusion;
using UnityEngine;

/// <summary>
/// 플레이어가 현재 무엇을 바라보고 있는지만 감지
/// </summary>
[DefaultExecutionOrder(100)] //카메라 각도(PlayerCameraController.LateUpdate)가 정해진 뒤에 감지
public class PlayerTargetDetector : NetworkBehaviour
{
    [Header("감지")]
    [SerializeField] private Camera playerCamera;
    [SerializeField] private float detectDistance = 2f;
    [SerializeField] private LayerMask targetLayer;

    public ITargetable CurrentTarget { get; private set; }

    public event Action<ITargetable> TargetChanged;

    private Collider lastCollider;
    private ITargetable lastResolvedTarget;
    private PlayerHealth health; //사망과 탈출 후 상호작용 표시 차단


    public override void Spawned()
    {
        health = GetComponent<PlayerHealth>();
        enabled = HasInputAuthority;

        if (!enabled)
            return;

        if (playerCamera == null)
            playerCamera = GetComponentInChildren<Camera>(true);

        if (playerCamera == null)
            Debug.LogError("[PlayerTargetDetector] Player Camera가 없습니다.");
    }


    //Update 에서는 Fusion 틱이 처리된 프레임에 카메라가 화면과 다른 위치(보간 전)에 있어 조준 대상이 프레임마다 바뀔 수 있다
    //보간이 끝나고 카메라 각도까지 정해진 LateUpdate 에서 감지해 화면에 보이는 그대로 판정한다
    private void LateUpdate()
    {
        DetectNow();
    }


    /// <summary>
    /// 현재 카메라가 바라보는 대상을 즉시 검사하고 반환
    /// </summary>
    public ITargetable DetectNow()
    {
        if (Object == null || !Object.IsValid || (health != null && !health.CanAct))
        {
            clearTarget();
            return null;
        }
        if (playerCamera == null)
            return null;

        if (!TryGetAimRay(out Ray ray))
            return null;

        if (Physics.Raycast(
            ray,
            out RaycastHit hit,
            detectDistance,
            targetLayer,
            QueryTriggerInteraction.Collide))
        {
            ITargetable target = ResolveTarget(hit.collider);

            SetTarget(target);

            return target;
        }

        ClearColliderCache();

        SetTarget(null);

        return null;
    }


    private ITargetable ResolveTarget(Collider collider)
    {
        if (collider == lastCollider)
            return lastResolvedTarget;

        lastCollider = collider;

        lastResolvedTarget = null;

        MonoBehaviour[] behaviours =
            collider.GetComponentsInParent<MonoBehaviour>(true);

        foreach (MonoBehaviour behaviour in behaviours)
        {
            if (behaviour is ITargetable target)
            {
                lastResolvedTarget = target;

                break;
            }
        }

        return lastResolvedTarget;
    }


    private void SetTarget(ITargetable newTarget)
    {
        if (ReferenceEquals(CurrentTarget, newTarget))
            return;

        CurrentTarget = newTarget;

        TargetChanged?.Invoke(CurrentTarget);
    }


    private void ClearColliderCache()
    {
        lastCollider = null;

        lastResolvedTarget = null;
    }

    internal void clearTarget() //상호작용 대상과 안내 표시 초기화
    {
        ClearColliderCache();
        SetTarget(null);
    }


    public bool TryGetAimRay(out Ray ray)
    {
        if (playerCamera == null)
        {
            ray = default;
            return false;
        }

        ray = new Ray(playerCamera.transform.position, playerCamera.transform.forward);

        return true;
    }
}
