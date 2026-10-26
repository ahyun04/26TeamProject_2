using Fusion;
using UnityEngine;

public abstract class ItemBase : NetworkBehaviour, ITargetable
{
    [SerializeField] private ItemData data;

    [Header("내려놓기")]
    [SerializeField] private LayerMask dropGroundLayers = 1; //플레이어가 서는 지면의 충돌 레이어
    [SerializeField, Min(0f)] private float dropRayHeight = 1f; //발 위치 위에서 지면 검사를 시작할 높이
    [SerializeField, Min(0.01f)] private float dropGroundDistance = 10f; //발 위치 아래로 지면을 찾을 최대 거리

    private ItemWorldView worldView;
    private NetworkObject displayedHolder; //현재 화면에 반영한 소유자
    private bool presentationInitialized; //장착 표시 초기화 여부
    private RaycastHit[] dropGroundHits = new RaycastHit[16]; //자신과 다른 아이템을 제외할 지면 검사 결과


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
            WorldPosition = getDropPosition(HolderObject.transform.position + extraOffset);
        IsInHand = false;
        HolderObject = null;

        ApplyHeldState();
    }

    private Vector3 getDropPosition(Vector3 position) //호스트의 물리 씬에서 지면을 찾고 아이템 아랫면을 맞춤
    {
        float rayHeight = Mathf.Max(0f, dropRayHeight); //지면보다 위에 둘 검사 시작점
        Vector3 origin = position + Vector3.up * rayHeight; //두 슬롯의 가로 배치 간격은 유지
        PhysicsScene physicsScene = Runner.GetPhysicsScene(); //현재 Fusion 세션의 충돌 정보
        int hitCount; //검사 범위의 모든 충돌 결과 수
        while (true)
        {
            hitCount = physicsScene.Raycast(origin, Vector3.down, dropGroundHits,
                rayHeight + Mathf.Max(0.01f, dropGroundDistance), dropGroundLayers, QueryTriggerInteraction.Ignore);
            if (hitCount < dropGroundHits.Length) break;
            System.Array.Resize(ref dropGroundHits, dropGroundHits.Length * 2);
        }

        float nearest = float.PositiveInfinity; //발 아래에서 가장 가까운 유효 지면
        for (int index = 0; index < hitCount; index++)
        {
            RaycastHit hit = dropGroundHits[index]; //지면 후보
            if (hit.collider == null || hit.distance >= nearest || hit.normal.y <= 0f ||
                hit.collider.transform.IsChildOf(HolderObject.transform) ||
                hit.collider.GetComponentInParent<ItemBase>() != null ||
                hit.collider.GetComponentInParent<PlayerMovement>() != null) continue;
            nearest = hit.distance;
            position.y = hit.point.y;
        }

        position.y -= getDropBottomOffset();
        return position;
    }

    private float getDropBottomOffset() //내려놓을 회전과 크기를 기준으로 모델 아랫면의 높이 계산
    {
        float bottom = float.PositiveInfinity; //아이템 루트 기준 최저 높이
        Matrix4x4 dropTransform = Matrix4x4.TRS(Vector3.zero, worldRotation, transform.lossyScale); //손 장착 회전을 제외한 바닥 표시 자세
        foreach (Renderer itemRenderer in GetComponentsInChildren<Renderer>())
        {
            Bounds bounds = itemRenderer.localBounds; //렌더러가 꺼진 장착 상태에서도 읽을 수 있는 모델 범위
            Matrix4x4 toDropSpace = dropTransform * transform.worldToLocalMatrix * itemRenderer.localToWorldMatrix; //자식 모델의 크기와 피벗 반영
            Vector3 min = bounds.min; //모델 범위의 최소 좌표
            Vector3 max = bounds.max; //모델 범위의 최대 좌표
            for (int corner = 0; corner < 8; corner++)
            {
                Vector3 point = new Vector3((corner & 1) == 0 ? min.x : max.x,
                    (corner & 2) == 0 ? min.y : max.y, (corner & 4) == 0 ? min.z : max.z); //회전 후 최저점을 찾을 모서리
                bottom = Mathf.Min(bottom, toDropSpace.MultiplyPoint3x4(point).y);
            }
        }
        return float.IsPositiveInfinity(bottom) ? 0f : bottom;
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
