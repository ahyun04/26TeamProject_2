using Fusion;
using UnityEngine;

public class PlayerItemController : NetworkBehaviour
{
    [Header("감지")]
    [SerializeField] private Camera playerCamera;
    [SerializeField] private float pickupDistance = 2f;
    [SerializeField] private LayerMask itemLayer;

    [Header("던지기")]
    [SerializeField] private float throwForce = 8f;

    private void Update()
    {
        // 내 캐릭터만 마우스 입력 가능 
        if (!HasInputAuthority)
            return;

        // 우클릭 = 잡기
        if (Input.GetMouseButtonDown(1))
        {
            TryPickup();
        }

        // 좌클릭 = 던지기
        if (Input.GetMouseButtonDown(0))
        {
            RPC_TryThrow(playerCamera.transform.forward);
        }
    }


    private void TryPickup()
    {
        Ray ray = new Ray(
            playerCamera.transform.position,
            playerCamera.transform.forward
        );

        if (!Physics.Raycast(ray, out RaycastHit hit, pickupDistance, itemLayer))
            return;


        NetworkItem item =
            hit.collider.GetComponentInParent<NetworkItem>();

        if (item == null)
            return;

        RPC_TryPickup(item.Object);
    }


    // 플레이어 → 호스트에게 잡기 요청
    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    private void RPC_TryPickup(NetworkObject itemObject)
    {
        if (itemObject == null)
            return;

        NetworkItem item =
            itemObject.GetComponent<NetworkItem>();

        if (item == null)
            return;

        // 이미 누군가 들고 있음
        if (item.IsHeld)
            return;

        // 너무 먼 거리에서 해킹처럼 잡는 것 방지
        float distance =
            Vector3.Distance(
                transform.position,
                item.transform.position
            );

        if (distance > pickupDistance + 0.5f)
            return;

        item.Pickup(Object);
    }


    // 플레이어 → 호스트에게 던지기 요청
    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    private void RPC_TryThrow(Vector3 direction)
    {
        NetworkItem[] items =
            FindObjectsOfType<NetworkItem>();

        foreach (NetworkItem item in items)
        {
            // 내가 들고 있는 물건 발견
            if (item.Holder == Object)
            {
                item.Throw(direction, throwForce);
                return;
            }
        }
    }
}