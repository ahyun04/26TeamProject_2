using System.Collections;
using UnityEngine;

public class PlayerDamageFeedbackComponent : MonoBehaviour
{
    [SerializeField] private CanvasGroup damageOverlay; //붉은 가장자리의 투명도
    [SerializeField, Min(0.01f)] private float overlayFadeSeconds = 0.65f; //피격 표시 사라지는 시간
    [SerializeField, Min(0.01f)] private float shakeSeconds = 0.25f; //카메라 흔들림 시간
    [SerializeField, Range(0f, 2f)] private float shakeDegrees = 0.65f; //약한 흔들림 최대 각도
    [SerializeField, Min(0f)] private float shakeFrequency = 28f; //흔들림 변화 속도

    private float damageTime = float.NegativeInfinity; //마지막 로컬 피격 시간
    private Coroutine overlayRoutine; //현재 피격 표시 연출

    internal void showDamage() //연속 피격 시 표시와 흔들림 다시 시작
    {
        if (!isActiveAndEnabled) return;
        damageTime = Time.unscaledTime;
        if (damageOverlay == null) return;
        if (overlayRoutine != null) StopCoroutine(overlayRoutine);
        damageOverlay.alpha = 1f;
        overlayRoutine = StartCoroutine(fadeOverlay());
    }

    private IEnumerator fadeOverlay() //중앙 투명·가장자리 빨간 표시를 서서히 제거
    {
        float elapsed = 0f;
        while (elapsed < overlayFadeSeconds)
        {
            elapsed += Time.unscaledDeltaTime;
            damageOverlay.alpha = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / overlayFadeSeconds));
            yield return null;
        }
        damageOverlay.alpha = 0f;
        overlayRoutine = null;
    }

    internal Quaternion getShakeRotation() //네트워크 조준에는 반영하지 않는 화면 회전
    {
        float elapsed = Time.unscaledTime - damageTime;
        if (elapsed < 0f || elapsed >= shakeSeconds) return Quaternion.identity;

        float strength = shakeDegrees * Mathf.Pow(1f - elapsed / shakeSeconds, 2f);
        float phase = elapsed * shakeFrequency;
        float pitch = (Mathf.PerlinNoise(phase, 0.37f) * 2f - 1f) * strength;
        float yaw = (Mathf.PerlinNoise(0.73f, phase) * 2f - 1f) * strength;
        float roll = Mathf.Sin(phase * Mathf.PI) * strength * 0.6f;
        return Quaternion.Euler(pitch, yaw, roll);
    }

    private void OnDisable() //씬 전환과 UI 제거 시 연출 초기화
    {
        if (overlayRoutine != null) StopCoroutine(overlayRoutine);
        overlayRoutine = null;
        damageTime = float.NegativeInfinity;
        if (damageOverlay != null) damageOverlay.alpha = 0f;
    }
}
