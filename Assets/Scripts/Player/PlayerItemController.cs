using Fusion;
using LockdownProtocol.Lobby;
using LockdownProtocol.Networking;
using UnityEngine;

/// <summary>
/// 플레이어의 아이템 주머니(슬롯 2개), 장착, 사용, 1인칭 언제 무엇을 보여줄지
/// - 슬롯은 서버(State Authority)가 관리하고, 마우스 휠로 슬롯을 전환한다.
/// - CurrentItemObject는 "지금 손에 든 아이템"(= 선택 슬롯의 아이템)으로, 기존 코드와 같은 의미를 유지한다.
/// </summary>
public class PlayerItemController : NetworkBehaviour
{
    private const int SlotCount = 2; //주머니 슬롯 개수

    [Header("참조")]
    [SerializeField] private PlayerTargetDetector targetDetector;
    [SerializeField] private PlayerFirstPersonItemView firstPersonItemView;

    [Header("서버 검증")]
    [SerializeField] private float maxInteractDistance = 3f;

    // 현재 플레이어가 손에 들고 있는 네트워크 아이템 (선택 슬롯의 아이템)
    [Networked, OnChangedRender(nameof(OnCurrentItemChanged))]
    public NetworkObject CurrentItemObject { get; private set; }

    // 주머니 슬롯 (슬롯당 아이템 최대 1개) 과 현재 선택 슬롯 (0 = 1번, 1 = 2번)
    [Networked] private NetworkObject SlotItem0 { get; set; }
    [Networked] private NetworkObject SlotItem1 { get; set; }
    [Networked] public int SelectedSlotIndex { get; private set; }

    // 현재 아이템이 Target 변화에 반응하는 기능을 가지고 있을 경우 저장
    private IItemTargetHandler targetHandler;
    private PlayerHealth health;
    private KillManager combat; //집기와 공격을 한 입력 경로에서 구분
    private PlayerInteraction interaction; //미션 수행 중 아이템 획득 제한
    private NetworkObject displayedItem; //현재 1인칭 표시를 적용한 아이템
    private bool presentationInitialized; //장착 표시 초기화 여부
    private bool pickupPending; //서버 응답 전 중복 획득 요청 방지
    private float pickupDeadline; //응답 지연 시 입력 복구 시각
    internal bool isPickupPending => pickupPending; //획득 안내의 중복 표시 제한
    internal int selectedSlot => SelectedSlotIndex; //인벤토리 UI가 읽는 선택 슬롯

    internal bool tryGetEquippedWeapon(out ItemData weapon) //현재 플레이어가 실제로 소유한 무기 조회
    {
        weapon = null;
        if (Object == null || !Object.IsValid || CurrentItemObject == null || !CurrentItemObject.IsValid) return false;
        ItemBase item = CurrentItemObject.GetComponentInChildren<ItemBase>(true);
        if (item == null || item.HolderObject != Object || item.Data == null || !item.Data.IsWeapon) return false;
        weapon = item.Data;
        return true;
    }

    internal ItemData getSlotItemData(int index) //인벤토리 UI용: 슬롯에 든 아이템 정보 (없으면 null)
    {
        if (index < 0 || index >= SlotCount || Object == null || !Object.IsValid) return null;
        NetworkObject slotObject = getSlot(index);
        if (slotObject == null || !slotObject.IsValid) return null;
        ItemBase item = slotObject.GetComponentInChildren<ItemBase>(true);
        return item != null ? item.Data : null;
    }

    internal GameObject getSlotItemModel(int index) //인벤토리 UI: 아이콘이 없는 아이템의 미리보기를 만들 원본 모델
    {
        if (index < 0 || index >= SlotCount || Object == null || !Object.IsValid) return null;
        NetworkObject slotObject = getSlot(index);
        return slotObject != null && slotObject.IsValid ? slotObject.gameObject : null;
    }

    private NetworkObject getSlot(int index) //슬롯 번호로 보관 아이템 조회
    {
        return index == 0 ? SlotItem0 : SlotItem1;
    }

    private void setSlot(int index, NetworkObject item) //슬롯 번호로 보관 아이템 지정
    {
        if (index == 0) SlotItem0 = item;
        else SlotItem1 = item;
    }


    public override void Spawned()
    {
        health = GetComponent<PlayerHealth>();
        combat = GetComponent<KillManager>();
        interaction = GetComponent<PlayerInteraction>();
        if (HasStateAuthority && health != null)
        {
            health.Died += DropAllItems;
            health.Escaped += DropAllItems;
        }
        if (!HasInputAuthority)
            return;

        if (targetDetector == null)
            targetDetector = GetComponent<PlayerTargetDetector>();

        if (firstPersonItemView == null)
            firstPersonItemView = GetComponent<PlayerFirstPersonItemView>();

        if (targetDetector == null)
        {
            Debug.LogError("[PlayerItemController] PlayerTargetDetector가 없습니다.");
            return;
        }

        targetDetector.TargetChanged += OnTargetChanged;

        // Spawn 직후 현재 장착 상태를 직접 한 번 적용
        RefreshCurrentItem();
    }

    public override void FixedUpdateNetwork() //소모·삭제로 아이템이 사라져도 손 상태를 슬롯 기준으로 보정
    {
        if (!HasStateAuthority)
            return;

        if (CurrentItemObject != getSlot(SelectedSlotIndex))
            syncHand();
    }

    public override void Render() //아이템 오브젝트의 수신이 늦어져도 로컬 표시 갱신
    {
        if (HasInputAuthority && (!presentationInitialized || displayedItem != CurrentItemObject))
            RefreshCurrentItem();
    }


    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        if (hasState && HasStateAuthority)
            DropAllItems(); //연결 종료 등으로 사라질 때 서버가 주머니를 정리

        if (health != null)
        {
            health.Died -= DropAllItems;
            health.Escaped -= DropAllItems;
        }
        if (targetDetector != null)
            targetDetector.TargetChanged -= OnTargetChanged;

        targetHandler?.ClearLocalTarget();

        firstPersonItemView?.Clear();

        targetHandler = null;
        displayedItem = null;
        presentationInitialized = false;
    }


    private void Update()
    {
        if (Object == null || !Object.IsValid || !HasInputAuthority || (health != null && !health.CanAct) || GameAudio.blocksPlayerInput ||
            Cursor.lockState != CursorLockMode.Locked || SessionDisconnectUIComponent.IsOpen ||
            (LobbyRoomUI.Instance != null && LobbyRoomUI.Instance.BlocksPlayerInput))
            return;

        HandleItemInput();
    }


    /// <summary>
    /// 아이템 관련 로컬 입력 처리
    /// </summary>
    private void HandleItemInput()
    {
        if (pickupPending && Time.unscaledTime >= pickupDeadline) pickupPending = false;

        // 마우스 휠 위·아래 모두 반대 슬롯으로 한 번 전환 (연속 입력은 입력마다 전환)
        if (Input.mouseScrollDelta.y != 0f) RequestSwitchSlot();

        handleItemInput(Input.GetMouseButtonDown(0), Input.GetMouseButtonDown(1), Input.GetKeyDown(KeyCode.G));
    }

    private void handleItemInput(bool leftDown, bool rightDown, bool dropDown) //클릭 시작 시 장착 상태로 한 동작만 선택
    {
        if (dropDown && CurrentItemObject != null)
        {
            RequestUnequipItem();
            return;
        }

        // 선택한 슬롯이 비어 있을 때(손에 아무것도 없을 때) 좌클릭 = 장착
        if (leftDown && CurrentItemObject == null)
        {
            if (interaction != null && interaction.isInteracting) return;
            TryEquipTargetItem();
            return;
        }

        if (leftDown) combat?.requestWeaponAttack();

        // 아이템을 들고 있을 때 우클릭 = 즉시 사용
        if (rightDown && CurrentItemObject != null)
        {
            TryUseCurrentItem();
        }
    }


    /// <summary>
    /// 현재 바라보고 있는 아이템 장착 시도
    /// </summary>
    private void TryEquipTargetItem()
    {
        if (targetDetector == null)
            return;

        if (!(targetDetector.CurrentTarget is ItemBase item))
            return;

        if (item.TargetObject == null)
            return;

        RequestEquipItem(item.TargetObject);
    }


    /// <summary>
    /// 아이템 장착을 Host에게 요청
    /// </summary>
    public void RequestEquipItem(NetworkObject itemObject)
    {
        if (!HasInputAuthority)
            return;

        if (itemObject == null)
            return;

        if (pickupPending) return;
        pickupPending = true;
        pickupDeadline = Time.unscaledTime + 5f;
        RPC_RequestEquipItem(itemObject.Id);
    }


    /// <summary>
    /// 현재 아이템 장착 해제를 Host에게 요청
    /// </summary>
    public void RequestUnequipItem()
    {
        if (!HasInputAuthority)
            return;

        RPC_RequestUnequipItem();
    }


    /// <summary>
    /// 슬롯 전환을 Host에게 요청 (미션 수행 중에는 요청하지 않는다)
    /// </summary>
    public void RequestSwitchSlot()
    {
        if (!HasInputAuthority)
            return;

        if (interaction != null && interaction.isInteracting)
            return;

        if (RoomManager.Instance != null && RoomManager.Instance.CurrentRoomState == RoomManager.RoomState.Starting)
            return;

        RPC_RequestSwitchSlot();
    }


    /// <summary>
    /// 현재 아이템 기능 사용 시도
    /// </summary>
    private void TryUseCurrentItem()
    {
        if (CurrentItemObject == null)
            return;

        if (!HasCurrentItemUseHandler())
            return;

        ITargetable target =
            targetDetector != null
                ? targetDetector.DetectNow()
                : null;

        NetworkId targetId = default;

        if (target != null &&
            target.TargetObject != null)
        {
            targetId = target.TargetObject.Id;
        }

        RPC_RequestUseItem(targetId);
    }


    /// <summary>
    /// Host가 아이템 장착 요청 검증
    /// </summary>
    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    private void RPC_RequestEquipItem(NetworkId itemId)
    {
        tryEquipItem(itemId);
        RPC_CompletePickupRequest();
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.InputAuthority)]
    private void RPC_CompletePickupRequest() //서버 판정이 끝나면 획득 입력 복구
    {
        pickupPending = false;
    }

    private int findFreeSlot() //선택 슬롯이 비어 있으면 그 슬롯, 아니면 다른 빈 슬롯, 둘 다 차 있으면 -1
    {
        int selected = SelectedSlotIndex;
        if (getSlot(selected) == null) return selected;

        int other = selected == 0 ? 1 : 0;
        return getSlot(other) == null ? other : -1;
    }

    private void tryEquipItem(NetworkId itemId) //빈 슬롯과 거리 검증을 서버에서 일괄 수행
    {
        if (health != null && !health.CanAct) return;
        if (RoomManager.Instance != null && RoomManager.Instance.CurrentRoomState == RoomManager.RoomState.Starting) return;

        int slot = findFreeSlot();
        if (slot < 0) //주머니가 가득 차면 기존 아이템을 덮어쓰지 않고 거부
            return;

        if (!Runner.TryFindObject(itemId, out NetworkObject itemObject))
            return;

        ItemBase item =
            itemObject.GetComponentInChildren<ItemBase>(true);

        if (item == null)
            return;

        if (!IsWithinInteractDistance(itemObject))
            return;

        if (!item.TryEquip(Object))
            return;

        setSlot(slot, itemObject);
        syncHand();
    }


    /// <summary>
    /// Host가 슬롯 전환 검증 후 반대 슬롯을 선택 (빈 슬롯도 선택 가능, 아이템은 버리거나 소모하지 않음)
    /// </summary>
    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    private void RPC_RequestSwitchSlot()
    {
        if (health != null && !health.CanAct) return;
        if (RoomManager.Instance != null && RoomManager.Instance.CurrentRoomState == RoomManager.RoomState.Starting) return;

        SelectedSlotIndex = SelectedSlotIndex == 0 ? 1 : 0;
        syncHand();
    }


    /// <summary>
    /// 호스트: 선택 슬롯 기준으로 손에 든 아이템과 각 아이템의 표시 상태를 맞춘다.
    /// </summary>
    private void syncHand()
    {
        if (!HasStateAuthority) return;

        int selected = SelectedSlotIndex;
        for (int i = 0; i < SlotCount; i++)
        {
            NetworkObject slotObject = getSlot(i);
            if (slotObject == null) continue;

            ItemBase slotItem = slotObject.GetComponentInChildren<ItemBase>(true);
            if (slotItem != null) slotItem.SetInHand(i == selected);
        }

        NetworkObject handObject = getSlot(selected);
        if (CurrentItemObject != handObject)
            CurrentItemObject = handObject;
    }


    /// <summary>
    /// Host가 장착 해제
    /// </summary>
    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    private void RPC_RequestUnequipItem()
    {
        if (health != null && !health.CanAct) return;
        DropCurrentItem();
    }

    internal void DropCurrentItem() //손에 든(선택 슬롯) 아이템만 내려놓는다
    {
        if (!HasStateAuthority) return;

        int selected = SelectedSlotIndex;
        NetworkObject handObject = getSlot(selected);
        if (handObject == null)
        {
            syncHand();
            return;
        }

        ItemBase item = handObject.GetComponentInChildren<ItemBase>(true);
        if (item != null)
            item.Unequip();

        setSlot(selected, null);
        syncHand();
    }

    internal void DropAllItems() //사망·탈출·연결 종료 시 두 슬롯의 아이템을 모두 내려놓는다
    {
        if (!HasStateAuthority) return;

        for (int i = 0; i < SlotCount; i++)
        {
            NetworkObject slotObject = getSlot(i);
            if (slotObject == null) continue;

            ItemBase item = slotObject.GetComponentInChildren<ItemBase>(true);
            if (item != null)
                item.Unequip(transform.right * (0.3f * i)); //같은 자리에 겹치지 않도록 살짝 띄운다

            setSlot(i, null);
        }

        syncHand();
    }


    /// <summary>
    /// Host가 현재 아이템 사용 요청 검증 후 실행
    /// </summary>
    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority, TickAligned = false)]
    private void RPC_RequestUseItem(NetworkId targetId)
    {
        if (health != null && !health.CanAct) return;
        if (CurrentItemObject == null)
            return;

        NetworkObject targetObject = null;

        if (targetId != default)
        {
            if (!Runner.TryFindObject(targetId, out targetObject))
                return;

            if (!IsWithinInteractDistance(targetObject))
                return;
        }

        IItemUseHandler useHandler =
            FindInterface<IItemUseHandler>(CurrentItemObject.gameObject);

        if (useHandler == null)
            return;

        useHandler.UseAsStateAuthority(targetObject);
    }


    /// <summary>
    /// 현재 아이템에 우클릭 기능이 있는지 확인
    /// </summary>
    private bool HasCurrentItemUseHandler()
    {
        if (CurrentItemObject == null)
            return false;

        IItemUseHandler useHandler =
            FindInterface<IItemUseHandler>(CurrentItemObject.gameObject);

        return useHandler != null;
    }


    /// <summary>
    /// CurrentItemObject가 변경되면 로컬 표현 갱신
    /// </summary>
    private void OnCurrentItemChanged()
    {
        if (!HasInputAuthority)
            return;

        RefreshCurrentItem();
    }


    /// <summary>
    /// 현재 장착 아이템의 로컬 기능과 1인칭 모델 갱신
    /// </summary>
    private void RefreshCurrentItem()
    {
        if (presentationInitialized && displayedItem == CurrentItemObject)
            return;

        displayedItem = CurrentItemObject;
        presentationInitialized = true;
        // 이전 아이템의 Outline 같은 로컬 효과 제거
        targetHandler?.ClearLocalTarget();

        targetHandler = null;

        // 이전 1인칭 아이템 제거
        firstPersonItemView?.Clear();

        if (CurrentItemObject == null)
            return;

        ItemBase item =
            CurrentItemObject.GetComponentInChildren<ItemBase>(true);

        if (item == null)
            return;

        // 내 화면에만 1인칭 아이템 표시
        firstPersonItemView?.Show(item.Data);

        // 현재 아이템이 Target 반응 기능을 가지고 있는지 확인
        targetHandler =
            FindInterface<IItemTargetHandler>(
                CurrentItemObject.gameObject
            );

        if (targetDetector == null)
            return;

        // 이미 무언가를 보고 있다면 바로 현재 아이템에 전달
        targetHandler?.SetLocalTarget(
            targetDetector.CurrentTarget
        );
    }


    /// <summary>
    /// 바라보는 대상이 변경되면 현재 아이템에 전달
    /// </summary>
    private void OnTargetChanged(ITargetable target)
    {
        targetHandler?.SetLocalTarget(target);
    }


    /// <summary>
    /// Host에서 플레이어와 대상 사이 거리 검증
    /// </summary>
    private bool IsWithinInteractDistance(NetworkObject targetObject)
    {
        if (targetObject == null)
            return false;

        float distance =
            Vector3.Distance(
                transform.position,
                targetObject.transform.position
            );

        return distance <= maxInteractDistance;
    }


    /// <summary>
    /// GameObject 내부에서 특정 인터페이스 구현체 검색
    /// </summary>
    private T FindInterface<T>(GameObject root) where T : class
    {
        MonoBehaviour[] behaviours =
            root.GetComponentsInChildren<MonoBehaviour>(true);

        foreach (MonoBehaviour behaviour in behaviours)
        {
            if (behaviour is T result)
                return result;
        }

        return null;
    }
}