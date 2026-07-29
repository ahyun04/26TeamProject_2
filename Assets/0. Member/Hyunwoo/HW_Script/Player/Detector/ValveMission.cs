using Fusion;
using UnityEngine;

public class ValveMission : NetworkBehaviour
{
    [SerializeField] private Transform rotatingPart;
    [SerializeField] private Vector3 rotationAxis = Vector3.forward;
    [SerializeField] private float rotationSpeed = 90f;

    [Networked] private PlayerRef User { get; set; }
    [Networked] private float Angle { get; set; }

    private Quaternion startRotation;

    public override void Spawned()
    {
        if (rotatingPart != null)
            startRotation = rotatingPart.localRotation;
    }

    public override void FixedUpdateNetwork()
    {
        if (!HasStateAuthority || User == PlayerRef.None)
            return;

        Angle = Mathf.Repeat(Angle + rotationSpeed * Runner.DeltaTime, 360f);
    }

    public override void Render()
    {
        if (rotatingPart == null)
            return;

        rotatingPart.localRotation = startRotation * Quaternion.AngleAxis(Angle, rotationAxis.normalized);
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority, HostMode = RpcHostMode.SourceIsHostPlayer)]
    public void RPC_SetTurning(bool turning, RpcInfo info = default)
    {
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