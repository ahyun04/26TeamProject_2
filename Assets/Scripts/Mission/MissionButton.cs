using Fusion;
using System.Collections;
using UnityEngine;

/// <summary>
/// 버튼으로 시작하는 미션의 공통 상호작용 버튼
/// 미션 시작과 버튼 눌림 연출 담당
/// </summary>
public class MissionButton : MonoBehaviour, ITargetable, IInteractable
{
    [SerializeField] private MissionMiniGameBase mission;

    [Header("버튼 애니메이션 설정")]
    [SerializeField] private Transform buttonVisual;
    [SerializeField] private Vector3 pressOffset;
    [SerializeField] private float pressTime = 0.1f;
    [SerializeField] private float returnTime = 0.1f;


    // PlayerTargetDetector가 이 버튼의 NetworkObject를 알 수 있도록 제공
    public NetworkObject TargetObject => mission != null ? mission.Object : null;

    private Vector3 startPos;
    private Coroutine pressRoutine;


    private void Awake()
    {
        // 버튼이 눌린 뒤 다시 돌아올 원래 위치 저장
        if (buttonVisual != null) startPos = buttonVisual.localPosition;
    }


    public void Interact()
    {
        if (mission == null || mission.IsCompleted) return;

        // 실제 미션 시작은 연결된 MissionMiniGameBase 자식에게 맡김
        mission.StartMission();

        if (buttonVisual == null) return;

        // 연속 클릭 시 기존 애니메이션을 중단하고 처음부터 다시 실행
        if (pressRoutine != null) StopCoroutine(pressRoutine);

        buttonVisual.localPosition = startPos;
        pressRoutine = StartCoroutine(PressAnimation());
    }


    private IEnumerator PressAnimation()
    {
        Vector3 pressPos = startPos + pressOffset;

        // 눌림 → 원래 위치 복귀 순서로 실행
        yield return MoveButton(startPos, pressPos, pressTime);
        yield return MoveButton(pressPos, startPos, returnTime);

        pressRoutine = null;
    }


    private IEnumerator MoveButton(Vector3 start, Vector3 end, float duration)
    {
        float time = 0f;

        // 지정한 시간 동안 start에서 end까지 부드럽게 이동
        while (time < duration)
        {
            time += Time.deltaTime;
            buttonVisual.localPosition = Vector3.Lerp(start, end, time / duration);
            yield return null;
        }

        // 계산 오차가 남지 않도록 마지막에 정확한 위치로 맞춤
        buttonVisual.localPosition = end;
    }


    private void OnDisable()
    {
        if (pressRoutine != null) StopCoroutine(pressRoutine);

        pressRoutine = null;

        if (buttonVisual != null) buttonVisual.localPosition = startPos;
    }
}