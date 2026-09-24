using UnityEngine;

/// <summary>
/// 필드에 보이는 아이템의 모델과 Collider만 관리
/// </summary>
public class ItemWorldView : MonoBehaviour
{
    [Header("월드 표시")]
    [SerializeField] private Renderer[] renderers;

    [Header("월드 충돌")]
    [SerializeField] private Collider[] colliders;

    [Header("손 장착 표시")]
    [SerializeField] private Vector3 heldPositionOffset; //손 기준 장착 위치
    [SerializeField] private Vector3 heldRotationOffset; //손 기준 장착 회전

    private Transform heldHand; //현재 따라가는 손 위치


    private void Awake()
    {
        if (renderers == null || renderers.Length == 0)
            renderers = GetComponentsInChildren<Renderer>(true);

        if (colliders == null || colliders.Length == 0)
            colliders = GetComponentsInChildren<Collider>(true);
    }


    public void SetVisible(bool visible)
    {
        heldHand = null;
        setPresentation(visible, visible);
    }

    internal void setHeld(Transform hand, bool visible) //장착 중에는 모델 표시와 충돌을 분리
    {
        heldHand = hand;
        setPresentation(visible, false);
        followHand();
    }

    private void LateUpdate() //캐릭터 애니메이션 이후 손 위치를 반영
    {
        followHand();
    }

    private void followHand() //손의 위치와 회전에 아이템 배치
    {
        if (heldHand == null)
            return;

        transform.SetPositionAndRotation(
            heldHand.position + heldHand.rotation * heldPositionOffset,
            heldHand.rotation * Quaternion.Euler(heldRotationOffset));
    }

    private void setPresentation(bool visible, bool canInteract) //렌더러와 상호작용 충돌 상태 적용
    {
        foreach (Renderer itemRenderer in renderers)
        {
            if (itemRenderer != null)
                itemRenderer.enabled = visible;
        }

        foreach (Collider itemCollider in colliders)
        {
            if (itemCollider != null)
                itemCollider.enabled = canInteract;
        }
    }
}
