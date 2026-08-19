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


    private void Awake()
    {
        if (renderers == null || renderers.Length == 0)
            renderers = GetComponentsInChildren<Renderer>(true);

        if (colliders == null || colliders.Length == 0)
            colliders = GetComponentsInChildren<Collider>(true);
    }


    public void SetVisible(bool visible)
    {
        foreach (Renderer itemRenderer in renderers)
        {
            if (itemRenderer != null)
                itemRenderer.enabled = visible;
        }

        foreach (Collider itemCollider in colliders)
        {
            if (itemCollider != null)
                itemCollider.enabled = visible;
        }
    }
}