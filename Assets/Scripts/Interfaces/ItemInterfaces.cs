using Fusion;


// 플레이어가 바라볼 수 있는 대상
public interface ITargetable
{
    NetworkObject TargetObject { get; }
}


// 한 번 눌러서 상호작용하는 대상
public interface IInteractable
{
    void Interact();
}


// 누르고 있는 동안 상호작용하는 대상
public interface IHoldInteractable
{
    void BeginHold();
    void EndHold();
}


// 현재 아이템이 우클릭으로 사용 가능한 경우
public interface IItemUseHandler
{
    void UseAsStateAuthority(NetworkObject target);
}


// 현재 바라보는 대상에 따라 로컬 표시가 필요한 아이템
public interface IItemTargetHandler
{
    void SetLocalTarget(ITargetable target);

    void ClearLocalTarget();
}