using Fusion;
using System;
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

    // Host에서 현재 이 먼지를 청소 중인 플레이어
    private PlayerRef suctionPlayer;

    // 먼지가 완전히 청소됐을 때 알림
    public event Action<PlayerRef> Collected;



    private Vector3 startLocalPosition;

    private Vector3 startScale;

    private NetworkObject cachedVacuumObject;

    private VacuumItem cachedVacuum;

    public NetworkObject TargetObject => Object;

    public bool IsCleaned => IsCollected;


    public override void Spawned()
    {
        if (visualRoot != null)
        {
            startLocalPosition = visualRoot.transform.localPosition;
            startScale = visualRoot.transform.localScale;
        }
            

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
    public bool TryBeginSuction(NetworkObject vacuumObject, float duration, PlayerRef player)
    {
        if (!HasStateAuthority)
            return false;

        if (player == PlayerRef.None)
            return false;

        if (IsSucking || IsCollected)
            return false;

        FilterCleaningMission mission = GetComponentInParent<FilterCleaningMission>();
        if (mission == null || !mission.CanPlayerInteract(player))
            return false;

        if (vacuumObject == null)
            return false;

        if (visualRoot == null)
            return false;

        VacuumItem vacuum =
            vacuumObject.GetComponentInChildren<VacuumItem>(true);


        if (vacuum == null)
            return false;


        SuctionItemObject = vacuumObject;

        suctionPlayer = player;

        SuctionStartPosition = visualRoot.transform.position;

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

        NetworkObject playerObject = Runner.GetPlayerObject(suctionPlayer);
        PlayerHealth health = playerObject != null ? playerObject.GetComponent<PlayerHealth>() : null;
        VacuumItem heldVacuum = GetVacuum();
        if (health == null || !health.CanAct || heldVacuum == null || heldVacuum.HolderObject != playerObject)
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

        Collected?.Invoke(suctionPlayer);

        suctionPlayer = PlayerRef.None;
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


        if (dustCollider != null && IsCollected)
        {
            dustCollider.enabled = false;
        }


        if (visualRoot != null)
        {
            visualRoot.SetActive(!IsCollected);
        }

        if (visualRoot != null && !IsSucking && !IsCollected)
        {
            visualRoot.transform.localPosition = startLocalPosition;
            visualRoot.transform.localScale = startScale;
        }
    }

    private void CancelSuction()
    {
        IsSucking = false;
        SuctionProgress = 0f;
        SuctionItemObject = null;

        suctionPlayer = PlayerRef.None;

        cachedVacuumObject = null;
        cachedVacuum = null;
    }
}
