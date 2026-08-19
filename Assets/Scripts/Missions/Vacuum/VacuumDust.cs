using Fusion;
using UnityEngine;

/// <summary>
/// 먼지 상태/흡입 
/// </summary>
public class VacuumDust : NetworkBehaviour, ITargetable
{

    [Header("먼지")]
    [SerializeField] private GameObject visualRoot;
    [SerializeField] private GameObject outlineRoot;
    [SerializeField] private Collider dustCollider;


    [Networked]
    private NetworkBool IsSucking { get; set; }

    [Networked]
    private NetworkBool IsCollected { get; set; }

    [Networked]
    private NetworkObject SuctionItemObject { get; set; }

    [Networked]
    private Vector3 SuctionStartPosition { get; set; }

    [Networked]
    private float SuctionProgress { get; set; }

    [Networked]
    private float SuctionDuration { get; set; }


    private Vector3 startScale;

    private NetworkObject cachedVacuumObject;
    private VacuumItem cachedVacuum;


    public NetworkObject TargetObject => Object;


    public override void Spawned()
    {
        if (visualRoot != null)
            startScale = visualRoot.transform.localScale;

        ApplyState();
    }


    /// <summary>
    /// Outline은 네트워크 동기화하지 않음,
    /// 바라보는 플레이어 화면에서만 실행
    /// </summary>
    public void SetOutline(bool active)
    {
        if (outlineRoot == null)
            return;


        // 이미 빨리고 있거나 없어진 먼지는
        // Outline을 표시하지 않음
        if (IsSucking || IsCollected)
        {
            outlineRoot.SetActive(false);

            return;
        }


        outlineRoot.SetActive(active);
    }


    /// <summary>
    /// Host가 먼지 흡입을 시작함
    /// </summary>
    public bool TryBeginSuction(NetworkObject vacuumObject, float duration)
    {
        if (!HasStateAuthority)
            return false;

        if (IsSucking || IsCollected)
            return false;

        if (vacuumObject == null)
            return false;


        VacuumItem vacuum =
            vacuumObject.GetComponentInChildren<VacuumItem>(true);


        if (vacuum == null)
            return false;


        SuctionItemObject = vacuumObject;

        SuctionStartPosition = transform.position;

        SuctionDuration = Mathf.Max(0.05f, duration);

        SuctionProgress = 0f;

        IsSucking = true;


        return true;
    }


    public override void FixedUpdateNetwork()
    {
        // 중요한 게임 상태는 Host만 결정
        if (!HasStateAuthority)
            return;

        if (!IsSucking)
            return;

        if (IsCollected)
            return;


        if (SuctionItemObject == null)
        {
            CancelSuction();
            return;
        }


        SuctionProgress +=
            Runner.DeltaTime / SuctionDuration;


        if (SuctionProgress < 1f)
            return;


        SuctionProgress = 1f;

        IsSucking = false;

        IsCollected = true;

        SuctionItemObject = null;
    }


    public override void Render()
    {
        ApplyState();

        if (!IsSucking || IsCollected)
            return;

        if (visualRoot == null)
            return;

        VacuumItem vacuum = GetVacuum();

        if (vacuum == null)
            return;

        Transform suctionMouth = vacuum.GetLocalSuctionMouth();

        if (suctionMouth == null)
            return;

        float t = Mathf.SmoothStep(
            0f,
            1f,
            SuctionProgress
        );

        visualRoot.transform.position = Vector3.Lerp(
            SuctionStartPosition,
            suctionMouth.position,
            t
        );

        visualRoot.transform.localScale = Vector3.Lerp(
            startScale,
            Vector3.zero,
            t
        );
    }


    private VacuumItem GetVacuum()
    {
        if (cachedVacuumObject == SuctionItemObject)
            return cachedVacuum;


        cachedVacuumObject = SuctionItemObject;

        cachedVacuum = null;


        if (cachedVacuumObject != null)
        {
            cachedVacuum =
                cachedVacuumObject
                    .GetComponentInChildren<VacuumItem>(true);
        }


        return cachedVacuum;
    }


    private void ApplyState()
    {
        if (outlineRoot != null &&
            (IsSucking || IsCollected))
        {
            outlineRoot.SetActive(false);
        }


        if (dustCollider != null)
        {
            dustCollider.enabled =
                !IsSucking &&
                !IsCollected;
        }


        if (visualRoot != null)
        {
            visualRoot.SetActive(!IsCollected);
        }
    }

    private void CancelSuction()
    {
        IsSucking = false;
        SuctionProgress = 0f;
        SuctionItemObject = null;
    }
}