using Fusion;
using UnityEngine;

public class ValveMission : MissionMiniGameBase, ITargetable, IHoldInteractable
{
    [Header("밸브")]
    [SerializeField] private Transform rotatingPart;
    [SerializeField] private Vector3 rotationAxis = Vector3.forward;
    [SerializeField] private float rotationSpeed = 90f;


    [Header("목표 회전")]
    [SerializeField] private float minTargetAngle = 360f;
    [SerializeField] private float maxTargetAngle = 720f;


    [Networked] private PlayerRef User { get; set; }
    [Networked] private float Angle { get; set; }
    [Networked] private float TargetAngle { get; set; }

    private Quaternion startRotation;

    public NetworkObject TargetObject => Object;


    public override void Spawned()
    {
        if (rotatingPart != null)
            startRotation = rotatingPart.localRotation;

        if (HasStateAuthority)
            TargetAngle = Random.Range(minTargetAngle, maxTargetAngle);
    }

    public override void FixedUpdateNetwork()
    {
        if (!HasStateAuthority || IsCompleted || User == PlayerRef.None)
            return;

        Angle = Mathf.Min(Angle + rotationSpeed * Runner.DeltaTime, TargetAngle);

        if (Angle < TargetAngle) return;

        PlayerRef completedBy = User;

        User = PlayerRef.None;

        RequestComplete(completedBy);
    }

    public override void Render()
    {
        base.Render();

        if (rotatingPart == null)
            return;

        rotatingPart.localRotation = startRotation * Quaternion.AngleAxis(Angle, rotationAxis.normalized);
    }


    public void BeginHold()
    {
        if (IsCompleted)
            return;

        RPC_SetTurning(true);
    }


    public void EndHold()
    {
        RPC_SetTurning(false);
    }


    [Rpc(RpcSources.All, RpcTargets.StateAuthority, HostMode = RpcHostMode.SourceIsHostPlayer)]
    public void RPC_SetTurning(bool turning, RpcInfo info = default)
    {
        if (IsCompleted)
            return;

        if (turning)
        {
            if (User == PlayerRef.None || User == info.Source)
                User = info.Source;

            return;
        }

        if (User == info.Source)
            User = PlayerRef.None;
    }
}