using Fusion;
using LockdownProtocol.Lobby;
using UnityEngine;

/// <summary>
/// 플레이어의 아이템 장착, 사용, 1인칭 언제 무엇을 보여줄지
/// </summary>
public class PlayerItemController : NetworkBehaviour
{
    [Header("참조")]
    [SerializeField] private PlayerTargetDetector targetDetector;
    [SerializeField] private PlayerFirstPersonItemView firstPersonItemView;

    [Header("서버 검증")]
    [SerializeField] private float maxInteractDistance = 3f;

    // 현재 플레이어가 장착하고 있는 네트워크 아이템
    [Networked, OnChangedRender(nameof(OnCurrentItemChanged))]
    public NetworkObject CurrentItemObject { get; private set; }

    // 현재 아이템이 Target 변화에 반응하는 기능을 가지고 있을 경우 저장
    private IItemTargetHandler targetHandler;
    private PlayerHealth health;
    private NetworkObject displayedItem; //현재 1인칭 표시를 적용한 아이템
    private bool presentationInitialized; //장착 표시 초기화 여부


    public override void Spawned()
    {
        health = GetComponent<PlayerHealth>();
        if (HasStateAuthority && health != null)
        {
            health.Died += DropCurrentItem;
            health.Escaped += DropCurrentItem;
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

    public override void Render() //아이템 오브젝트의 수신이 늦어져도 로컬 표시 갱신
    {
        if (HasInputAuthority && (!presentationInitialized || displayedItem != CurrentItemObject))
            RefreshCurrentItem();
    }


    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        if (health != null)
        {
            health.Died -= DropCurrentItem;
            health.Escaped -= DropCurrentItem;
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
        if (Object == null || !Object.IsValid || !HasInputAuthority || (health != null && !health.CanAct) ||
            (LobbyRoomUI.Instance != null && LobbyRoomUI.Instance.BlocksPlayerInput))
            return;

        HandleItemInput();
    }


    /// <summary>
    /// 아이템 관련 로컬 입력 처리
    /// </summary>
    private void HandleItemInput()
    {
        bool leftDown = Input.GetMouseButtonDown(0);
        bool rightDown = Input.GetMouseButtonDown(1);
        if (Input.GetKeyDown(KeyCode.G) && CurrentItemObject != null)
        {
            RequestUnequipItem();
            return;
        }

        // 아무것도 들고 있지 않을 때 좌클릭 = 장착
        if (leftDown && CurrentItemObject == null)
        {
            TryEquipTargetItem();
            return;
        }

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
        if (health != null && !health.CanAct) return;
        if (CurrentItemObject != null)
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

        CurrentItemObject = itemObject;
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

    internal void DropCurrentItem()
    {
        if (!HasStateAuthority) return;
        if (CurrentItemObject == null)
            return;

        ItemBase item =
        CurrentItemObject.GetComponentInChildren<ItemBase>(true);

        if (item != null)
            item.Unequip();

        CurrentItemObject = null;
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
