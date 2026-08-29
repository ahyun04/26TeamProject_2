using Fusion;
using System.Collections;
using UnityEngine;


/// <summary>
/// 4개의 전선을 모두 연결한 뒤 미션을 최종 완료시키는 레버를 담당
/// 플레이어의 클릭을 WiringMission에 전달하고, 완료 후 레버가 내려가는 시각 애니메이션을 재생한다
/// 실제 미션 완료 판정은 WiringMission과 MissionMiniGameBase가 담당한다
/// </summary>
public class WiringLever : MonoBehaviour, ITargetable, IInteractable
{
    [SerializeField] private WiringMission mission;
    [SerializeField] private Transform leverTransform;

    [Header("회전")]
    [SerializeField] private Vector3 completedRotation;
    [SerializeField] private float rotateDuration = 0.25f;

    private Quaternion startRotation;
    private bool completedVisualApplied;
    private Coroutine rotateRoutine;

    public NetworkObject TargetObject => mission != null ? mission.Object : null;

    private void Awake()
    {
        if (mission == null)
            mission = GetComponentInParent<WiringMission>();

        if (leverTransform == null)
            leverTransform = transform;

        startRotation = leverTransform.localRotation;
    }

    public void Interact()
    {
        if (mission == null || mission.IsCompleted || !mission.IsReady)
            return;

        mission.RequestLever();
    }

    public void SetCompleted()
    {
        if (completedVisualApplied || leverTransform == null)
            return;

        completedVisualApplied = true;

        if (rotateRoutine != null)
            StopCoroutine(rotateRoutine);

        rotateRoutine = StartCoroutine(RotateLever());
    }

    private IEnumerator RotateLever()
    {
        Quaternion targetRotation = Quaternion.Euler(completedRotation);
        float time = 0f;

        while (time < rotateDuration)
        {
            time += Time.deltaTime;

            float t = Mathf.Clamp01(time / rotateDuration);

            leverTransform.localRotation = Quaternion.Slerp(startRotation, targetRotation, t);

            yield return null;
        }

        leverTransform.localRotation = targetRotation;
        rotateRoutine = null;
    }
}