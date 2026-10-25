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
    [Networked, OnChangedRender(nameof(OnHolderChanged))]
    public NetworkBool IsInHand { get; private set; } //소유자의 주머니 중 현재 손에 든 슬롯인지 (아니면 주머니에 숨겨 둠)


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

        IsInHand = true; //손에 들지 여부는 소유자의 인벤토리가 이어서 SetInHand로 확정
        HolderObject = holder;

        ApplyHeldState();

        return true;
    }


    public void Unequip(Vector3 extraOffset = default)
    {
        if (!HasStateAuthority)
            return;

        if (HolderObject != null)
            WorldPosition = HolderObject.transform.position + Vector3.up * 0.3f + extraOffset;
        IsInHand = false;
        HolderObject = null;

        ApplyHeldState();
    }


    /// <summary>소유자의 선택 슬롯이 바뀔 때 호스트가 호출 - 손에 든 아이템만 모델을 보여 준다.</summary>
    internal void SetInHand(bool inHand)
    {
        if (!HasStateAuthority)
            return;

        if (IsInHand == inHand)
            return;

        IsInHand = inHand;
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

        //주머니에만 있는 아이템(IsInHand false)은 모두에게 숨긴다. 내 화면의 손 아이템은 1인칭 모델이 따로 처리한다.
        worldView.setHeld(hand, IsInHand && !displayedHolder.HasInputAuthority && hand != null);
    }
}