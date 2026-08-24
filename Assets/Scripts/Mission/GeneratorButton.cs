using System.Collections;
using UnityEngine;

public class GeneratorButton : MonoBehaviour
{
    [SerializeField] private GeneratorMission mission;

    [Header("버튼 눌림 효과")]
    [SerializeField] private Transform buttonVisual;
    [SerializeField] private Vector3 pressedOffset = new Vector3(0f, 0f, -0.02f);
    [SerializeField] private float pressTime = 0.08f;
    [SerializeField] private float returnTime = 0.08f;

    private Vector3 startPosition;
    private Coroutine pressRoutine;

    private void Awake()
    {
        if (buttonVisual != null)
            startPosition = buttonVisual.localPosition;
    }

    public void Press()
    {
        if (mission == null || buttonVisual == null)
            return;

        mission.StartMission();

        if (pressRoutine != null)
            StopCoroutine(pressRoutine);

        pressRoutine = StartCoroutine(PressAnimation());
    }

    private IEnumerator PressAnimation()
    {
        Vector3 pressedPosition = startPosition + pressedOffset;

        yield return MoveButton(startPosition, pressedPosition, pressTime);
        yield return MoveButton(pressedPosition, startPosition, returnTime);

        pressRoutine = null;
    }

    private IEnumerator MoveButton(Vector3 start, Vector3 end, float duration)
    {
        float time = 0f;

        while (time < duration)
        {
            time += Time.deltaTime;
            buttonVisual.localPosition = Vector3.Lerp(start, end, time / duration);
            yield return null;
        }

        buttonVisual.localPosition = end;
    }
}