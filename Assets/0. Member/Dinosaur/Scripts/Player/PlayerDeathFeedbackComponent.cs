using System.Collections;
using Fusion;
using LockdownProtocol.Networking;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class PlayerDeathFeedbackComponent : MonoBehaviour
{
    [SerializeField] private CanvasGroup blackOverlay; //사망 순간 화면을 덮는 검은 배경
    [SerializeField, Min(0f)] private float blackHoldSeconds = 0.3f; //검은 화면 최소 유지 시간
    [SerializeField, Min(0.01f)] private float blackFadeSeconds = 1.2f; //관전자 화면이 밝아지는 시간
    [SerializeField, Min(0.01f)] private float blurFadeSeconds = 1.5f; //흐린 시야가 선명해지는 시간
    [SerializeField, Range(0f, 1.5f)] private float blurRadius = 1.5f; //사망 직후 시야 흐림 강도

    private Coroutine deathRoutine; //현재 로컬 사망 연출
    private Volume blurVolume; //이 클라이언트에만 생성하는 임시 후처리
    private VolumeProfile blurProfile; //공유 프로필을 수정하지 않는 런타임 설정
    private DepthOfField depthOfField; //기존 URP의 흐림 효과

    internal void showDeath(NetworkRunner runner) //본인의 즉시 관전 전환을 검은 화면으로 가리기
    {
        if (deathRoutine != null || blackOverlay == null || !isActiveAndEnabled) return;
        blackOverlay.gameObject.SetActive(true);
        blackOverlay.alpha = 1f;
        prepareBlur();
        deathRoutine = StartCoroutine(fadeDeath(runner));
    }

    private void prepareBlur() //사망 연출 전용 후처리 설정
    {
        if (blurVolume == null)
        {
            GameObject volumeObject = new GameObject("Local Death Blur"); //카메라가 이미 사용하는 기본 Volume 레이어
            volumeObject.transform.SetParent(transform, false);
            blurVolume = volumeObject.AddComponent<Volume>();
            blurVolume.isGlobal = true;
            blurVolume.priority = 1000f;
            blurProfile = ScriptableObject.CreateInstance<VolumeProfile>();
            depthOfField = blurProfile.Add<DepthOfField>(true);
            depthOfField.mode.Override(DepthOfFieldMode.Gaussian);
            depthOfField.gaussianStart.Override(0f);
            depthOfField.gaussianEnd.Override(0.1f);
            depthOfField.gaussianMaxRadius.min = 0f;
            depthOfField.highQualitySampling.Override(true);
            blurVolume.sharedProfile = blurProfile;
        }
        depthOfField.gaussianMaxRadius.Override(blurRadius);
        blurVolume.weight = 1f;
    }

    private IEnumerator fadeDeath(NetworkRunner runner) //관전자 카메라 준비 후 암전과 흐림을 부드럽게 해제
    {
        float startedAt = Time.unscaledTime; //일시 정지에 영향받지 않는 연출 시작 시간
        while (Time.unscaledTime - startedAt < blackHoldSeconds || !isSpectatorCameraReady(runner))
            yield return null;

        float elapsed = 0f; //관전자 화면을 복원한 시간
        float duration = Mathf.Max(blackFadeSeconds, blurFadeSeconds); //두 효과가 모두 끝나는 시간
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            blackOverlay.alpha = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / blackFadeSeconds));
            depthOfField.gaussianMaxRadius.value = blurRadius *
                (1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / blurFadeSeconds)));
            yield return null;
        }
        deathRoutine = null;
        clearEffects();
    }

    private bool isSpectatorCameraReady(NetworkRunner runner) //다른 플레이어의 카메라를 기다리지 않도록 본인 관전자 확인
    {
        if (runner == null || !runner.IsRunning) return false;
        NetworkObject player = runner.GetPlayerObject(runner.LocalPlayer); //승패 판정에 남아 있는 본인 몸
        SpectatorManager spectator = player != null && player.IsValid ? player.GetComponent<SpectatorManager>() : null; //본인의 관전자 참조
        Transform listener = PlayerCameraController.LocalListenerTransform; //현재 실제 시점
        return spectator != null && spectator.spectatorObject != null && spectator.spectatorObject.IsValid &&
            listener != null && listener.gameObject.activeInHierarchy && listener.IsChildOf(spectator.spectatorObject.transform);
    }

    internal void cancelDeath() //부활·게임 종료·연결 종료 시 화면 즉시 복원
    {
        if (deathRoutine != null) StopCoroutine(deathRoutine);
        deathRoutine = null;
        clearEffects();
    }

    private void clearEffects() //검은 배경과 임시 후처리 해제
    {
        if (blackOverlay != null)
        {
            blackOverlay.alpha = 0f;
            blackOverlay.gameObject.SetActive(false);
        }
        if (blurVolume != null) blurVolume.weight = 0f;
    }

    private void OnDisable() //화면 비활성화 시 코루틴과 흐림 정리
    {
        cancelDeath();
    }

    private void OnDestroy() //런타임 프로필과 구성 요소 정리
    {
        if (blurProfile != null)
        {
            foreach (VolumeComponent component in blurProfile.components)
                if (component != null) Destroy(component);
            Destroy(blurProfile);
        }
    }
}
