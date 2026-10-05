using Fusion;
using UnityEngine;

[RequireComponent(typeof(PlayerMovement), typeof(PlayerHealth))]
public class PlayerAudioComponent : NetworkBehaviour
{
    [SerializeField] private AudioSource footstepSource; //발소리 재생기
    [SerializeField] private AudioSource deathSource; //사망음 재생기
    [SerializeField] private AudioClip walkSound; //발소리 클립
    [SerializeField] private AudioClip deathSound; //사망음 클립
    [SerializeField, Min(0.1f)] private float strideDistance = 1.7f; //한 걸음의 이동 거리
    [SerializeField, Range(0f, 1f)] private float footstepVolume = 0.7f; //발소리 상대 음량
    [SerializeField, Range(0f, 1f)] private float deathVolume = 1f; //사망음 상대 음량
    [SerializeField, Min(0.1f)] private float maxAudibleDistance = 15f; //다른 플레이어 소리가 들리는 거리

    private PlayerMovement movement; //이미 동기화된 이동 상태
    private PlayerHealth health; //이미 동기화된 생존 상태
    private float distanceToNextStep; //다음 발소리까지 이동할 거리
    private bool deathPlayed; //사망음 중복 재생 방지

    public override void Spawned() //기존 상태와 로컬 재생기 연결
    {
        movement = GetComponent<PlayerMovement>();
        health = GetComponent<PlayerHealth>();
        deathPlayed = health.IsDead;
        configureSource(footstepSource);
        configureSource(deathSource);
        applyVolume();
        health.Died += playDeath;
    }

    private void configureSource(AudioSource source) //본인은 직접 듣고 다른 사람은 거리로 감쇠
    {
        source.playOnAwake = false;
        source.loop = false;
        source.spatialBlend = HasInputAuthority ? 0f : 1f;
        source.rolloffMode = AudioRolloffMode.Logarithmic;
        source.minDistance = 2f;
        source.maxDistance = maxAudibleDistance;
        source.dopplerLevel = 0f;
    }

    public override void Render() //실제 접지 이동에 맞춰 본 PC에서 발소리 재생
    {
        applyVolume();
        if (!health.CanAct || !movement.IsGrounded || movement.CurrentSpeed < 0.15f ||
            (HasInputAuthority && GameAudio.blocksPlayerInput))
        {
            distanceToNextStep = 0f;
            if (footstepSource.isPlaying) footstepSource.Stop();
            return;
        }
        distanceToNextStep -= movement.CurrentSpeed * Time.deltaTime;
        if (distanceToNextStep > 0f) return;
        distanceToNextStep = strideDistance;
        if (walkSound != null) footstepSource.PlayOneShot(walkSound);
    }

    private void playDeath() //기존 사망 알림당 한 번 재생
    {
        if (deathPlayed) return;
        deathPlayed = true;
        footstepSource.Stop();
        applyVolume();
        if (deathSound != null) deathSource.PlayOneShot(deathSound);
    }

    private void applyVolume() //전체와 효과음 설정 적용
    {
        float volume = GameAudio.getEffectsVolume();
        if (!HasInputAuthority)
        {
            Transform listener = PlayerCameraController.LocalListenerTransform;
            if (listener == null || Vector3.Distance(transform.position, listener.position) >= maxAudibleDistance)
                volume = 0f;
        }
        footstepSource.volume = volume * footstepVolume;
        deathSource.volume = volume * deathVolume;
    }

    public override void Despawned(NetworkRunner runner, bool hasState) //사망 구독과 재생 정리
    {
        if (health != null) health.Died -= playDeath;
        footstepSource.Stop();
        deathSource.Stop();
    }
}
