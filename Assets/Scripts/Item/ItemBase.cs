using Fusion;
using UnityEngine;

public abstract class ItemBase : NetworkBehaviour, ITargetable
{
    [SerializeField] private ItemData data;

    private ItemWorldView worldView;
    private NetworkObject displayedHolder; //현재 화면에 반영한 소유자
    private bool presentationInitialized; //장착 표시 초기화 여부


    [Networked, OnChangedRender(nameof(OnHolderChanged))]
    public NetworkObject HolderObject { get; private set; }
    [Networked, OnChangedRender(nameof(OnHolderChanged))]
    private Vector3 WorldPosition { get; set; }
    [Networked, OnChangedRender(nameof(OnHolderChanged))]
    private Quaternion worldRotation { get; set; } //내려놓은 아이템의 회전


    public ItemData Data => data;

    public NetworkObject TargetObject => Object;

    public bool IsHeld => HolderObject != null;


    public override void Spawned()
    {
        if (HasStateAuthority)
        {
            WorldPosition = transform.position;
            worldRotation = transform.rotation;
        }
        worldView = GetComponent<ItemWorldView>();

        if (worldView == null)
            Debug.LogError($"[{name}] ItemWorldView가 없습니다.");

        ApplyHeldState();
    }

    public override void Render() //소유자 오브젝트가 늦게 생성된 경우에도 장착 표시 갱신
    {
        if (!presentationInitialized || displayedHolder != HolderObject)
            ApplyHeldState();
    }


    public bool TryEquip(NetworkObject holder)
    {
        if (!HasStateAuthority)
            return false;

        if (holder == null || !holder.IsValid || holder.Runner != Runner)
            return false;

        if (HolderObject != null)
            return false;

        HolderObject = holder;

        ApplyHeldState();

        return true;
    }


    public void Unequip()
    {
        if (!HasStateAuthority)
            return;

        if (HolderObject != null)
            WorldPosition = HolderObject.transform.position + Vector3.up * 0.3f;
        HolderObject = null;

        ApplyHeldState();
    }


    private void OnHolderChanged()
    {
        ApplyHeldState();
    }


    private void ApplyHeldState()
    {
        if (worldView == null)
            worldView = GetComponent<ItemWorldView>();

        if (worldView == null)
        {
            Debug.LogError($"[{name}] ItemWorldView를 찾을 수 없음");
            return;
        }

        displayedHolder = HolderObject;
        presentationInitialized = true;
        if (displayedHolder == null)
        {
            worldView.SetVisible(true);
            transform.SetPositionAndRotation(WorldPosition, worldRotation);
            return;
        }

        PlayerItemHoldPoint holdPoint = displayedHolder.GetComponent<PlayerItemHoldPoint>(); //소유자의 손 위치
        Transform hand = holdPoint != null ? holdPoint.HandPoint : null; //모델이 따라갈 장착 지점
        if (hand == null)
            Debug.LogError($"[{name}] 소유자의 PlayerItemHoldPoint가 연결되지 않았습니다.");

        worldView.setHeld(hand, !displayedHolder.HasInputAuthority && hand != null);
    }
}
