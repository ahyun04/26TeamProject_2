using Fusion;
using UnityEngine;

/// <summary>
/// 청소기 아이템 고유 기능 담
/// </summary>
public class VacuumItem : ItemBase, IItemUseHandler, IItemTargetHandler
{

    [Header("청소기")]
    [SerializeField] private Transform mouth;

    [Header("흡입")]
    [SerializeField] private float suctionDuration = 0.6f;


    private VacuumDust currentDust;


    public Transform Mouth => mouth;


    /// <summary>
    /// 로컬 플레이어가 바라보는 대상 변경
    /// </summary>
    public void SetLocalTarget(ITargetable target)
    {
        VacuumDust newDust = target as VacuumDust;


        if (currentDust == newDust)
            return;


        // 이전 먼지 Outline 끄기
        if (currentDust != null)
            currentDust.SetOutline(false);


        currentDust = newDust;


        // 새로운 먼지 Outline 켜기
        if (currentDust != null)
            currentDust.SetOutline(true);
    }


    /// <summary>
    /// 장착 해제 등에 의해
    /// Target 표시를 제거
    /// </summary>
    public void ClearLocalTarget()
    {
        if (currentDust != null)
            currentDust.SetOutline(false);

        currentDust = null;
    }


    /// <summary>
    /// Host에서 실제 청소기 사용
    /// </summary>
    public void UseAsStateAuthority(NetworkObject target)
    {
        if (!HasStateAuthority)
            return;

        if (target == null)
            return;


        VacuumDust dust =
            target.GetComponentInChildren<VacuumDust>(true);


        if (dust == null)
            return;


        dust.TryBeginSuction(
            Object,
            suctionDuration
        );
    }

    public Transform GetLocalSuctionMouth()
    {
        if (HolderObject == null)
            return null;

        if (!HolderObject.HasInputAuthority)
            return null;

        PlayerFirstPersonItemView firstPersonView =
            HolderObject.GetComponent<PlayerFirstPersonItemView>();

        if (firstPersonView == null)
            return null;

        FirstPersonVacuumView vacuumView =
            firstPersonView.GetCurrentView<FirstPersonVacuumView>();

        if (vacuumView == null)
            return null;

        return vacuumView.Mouth;
    }
}