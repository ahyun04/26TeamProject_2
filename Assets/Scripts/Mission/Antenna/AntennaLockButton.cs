using Fusion;
using UnityEngine;

/// <summary>
/// 안테나 미션의 고정 버튼
///
/// 플레이어가 좌클릭하면 AntennaMission에
/// 최종 고정을 요청한다
/// </summary>
public class AntennaLockButton : MonoBehaviour, ITargetable, IInteractable
{
    [SerializeField] private AntennaMission antennaMission;


    public NetworkObject TargetObject =>
        antennaMission != null ?
        antennaMission.Object :
        null;


    private void Awake()
    {
        if (antennaMission == null)
            antennaMission = GetComponentInParent<AntennaMission>();
    }


    /// <summary>
    /// PlayerInteraction의 좌클릭으로 호출
    /// </summary>
    public void Interact()
    {
        if (antennaMission == null || antennaMission.IsCompleted)
            return;

        antennaMission.RequestLock();
    }
}