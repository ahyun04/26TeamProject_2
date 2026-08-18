using Fusion;
using UnityEngine;

[DefaultExecutionOrder(1000)]
[RequireComponent(typeof(NetworkObject))]
[RequireComponent(typeof(Rigidbody))]
public class NetworkItem : NetworkBehaviour
{
    [Header("화면에 보이는 모델")]
    [SerializeField] private Transform visualRoot;

    private Rigidbody rb;

    // 현재 이 물건을 들고 있는 플레이어
    [Networked] public NetworkObject Holder { get; private set; }

    // 현재 누군가 들고 있는가?
    public bool IsHeld => Holder != null;


    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
    }


    // 실제 네트워크/물리 위치
    public override void FixedUpdateNetwork()
    {
        if (!HasStateAuthority)
            return;

        if (Holder == null)
            return;

        PlayerItemHoldPoint holdPoint =
            Holder.GetComponent<PlayerItemHoldPoint>();

        if (holdPoint == null)
            return;

        Transform hand = holdPoint.HandPoint;

        if (hand == null)
            return;

        rb.position = hand.position;
        rb.rotation = hand.rotation;
    }


    // 내 화면에 보이는 모델은 매 프레임 손 위치를 따라감
    public override void Render()
    {
        if (Holder == null)
            return;

        // 내가 들고 있는 물건만 직접 보정
        if (!Holder.HasInputAuthority)
            return;

        PlayerItemHoldPoint holdPoint =
            Holder.GetComponent<PlayerItemHoldPoint>();

        if (holdPoint == null || visualRoot == null)
            return;

        Transform hand = holdPoint.HandPoint;

        if (hand == null)
            return;

        visualRoot.position = hand.position;
        visualRoot.rotation = hand.rotation;
    }


    public void Pickup(NetworkObject player)
    {
        if (!HasStateAuthority)
            return;

        if (Holder != null)
            return;

        Holder = player;

        rb.velocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        rb.isKinematic = true;
    }


    public void Throw(Vector3 direction, float force)
    {
        if (!HasStateAuthority)
            return;

        if (Holder == null)
            return;

        Holder = null;

        rb.isKinematic = false;

        rb.velocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;

        rb.AddForce(
            direction.normalized * force,
            ForceMode.Impulse
        );
    }
}