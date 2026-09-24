using Fusion;
using UnityEngine;

//별도 사용 기능 없이 공통 장착·해제 동작을 사용하는 아이템
[RequireComponent(typeof(NetworkObject), typeof(ItemWorldView))]
public sealed class PickupItemComponent : ItemBase
{
}
