using Fusion;
using UnityEngine;

public class PlayerMissionDetector : NetworkBehaviour
{
    [SerializeField] private Camera playerCamera;
    [SerializeField] private float detectDistance = 2f;
    [SerializeField] private LayerMask missionLayer;

    private MissionPrompt currentTarget;
    private ValveMission activeValve;

    public override void Spawned()
    {
        enabled = HasInputAuthority;

        if (enabled && playerCamera == null)
            playerCamera = GetComponentInChildren<Camera>(true);
    }

    private void Update()
    {
        if (playerCamera == null)
            return;

        Transform cameraTransform = playerCamera.transform;
        Ray ray = new Ray(cameraTransform.position, cameraTransform.forward);

        if (!Physics.Raycast(ray, out RaycastHit hit, detectDistance, missionLayer, QueryTriggerInteraction.Collide))
        {
            Debug.DrawRay(ray.origin, ray.direction * detectDistance, Color.red);
            SetTarget(null);
            StopValve();
            return;
        }

        Debug.DrawRay(ray.origin, ray.direction * hit.distance, Color.green);

        MissionPrompt target = hit.collider.GetComponentInParent<MissionPrompt>();
        ValveMission valve = hit.collider.GetComponentInParent<ValveMission>();
        GeneratorButton button = hit.collider.GetComponentInParent<GeneratorButton>();

        if (activeValve != null && activeValve != valve)
            StopValve();

        SetTarget(target);

        // 밸브 - F키를 누르고 있는 동안 회전
        if (Input.GetKeyDown(KeyCode.F) && valve != null)
        {
            activeValve = valve;
            activeValve.RPC_SetTurning(true);
        }

        if (Input.GetKeyUp(KeyCode.F))
            StopValve();

        // 발전기 버튼 - 마우스 왼쪽 클릭
        if (Input.GetMouseButtonDown(0) && button != null)
            button.Press();
    }

    private void SetTarget(MissionPrompt newTarget)
    {
        if (currentTarget == newTarget)
            return;

        if (currentTarget != null)
            currentTarget.Hide();

        currentTarget = newTarget;

        if (currentTarget != null)
            currentTarget.Show();
    }

    private void StopValve()
    {
        if (activeValve == null)
            return;

        activeValve.RPC_SetTurning(false);
        activeValve = null;
    }
}