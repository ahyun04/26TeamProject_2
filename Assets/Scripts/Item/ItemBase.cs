using Fusion;
using UnityEngine;

public abstract class ItemBase : NetworkBehaviour, ITargetable
{
    [SerializeField] private ItemData data;

    private ItemWorldView worldView;


    [Networked, OnChangedRender(nameof(OnHolderChanged))]
    public NetworkObject HolderObject { get; private set; }
    [Networked, OnChangedRender(nameof(OnHolderChanged))]
    private Vector3 WorldPosition { get; set; }


    public ItemData Data => data;

    public NetworkObject TargetObject => Object;

    public bool IsHeld => HolderObject != null;


    public override void Spawned()
    {
        if (HasStateAuthority) WorldPosition = transform.position;
        worldView = GetComponent<ItemWorldView>();

        if (worldView == null)
            Debug.LogError($"[{name}] ItemWorldView가 없습니다.");

        ApplyHeldState();
    }


    public bool TryEquip(NetworkObject holder)
    {
        if (!HasStateAuthority)
            return false;

        if (holder == null)
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
        if (!IsHeld) transform.position = WorldPosition;
        if (worldView == null)
            worldView = GetComponent<ItemWorldView>();

        if (worldView == null)
        {
            Debug.LogError($"[{name}] ItemWorldView를 찾을 수 없음");
            return;
        }

        worldView.SetVisible(!IsHeld);
    }
}
