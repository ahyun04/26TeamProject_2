using Fusion;
using Photon.Voice.Unity;
using UnityEngine;

/// <summary>
/// 음성 거리 감쇠(Unity 기본 3D 사운드 사용) + 벽(장애물) 차단을 처리한다.
/// Player 프리팹의 VoiceSpeaker 자식(= Speaker 컴포넌트가 붙은 오브젝트)에 부착한다.
///
/// 거리 감쇠 자체는 Unity의 AudioSource 로그형 감쇠(Logarithmic Rolloff)에 맡긴다 —
/// 기획팀이 "자연스럽게 줄어드는" 느낌을 원해서, 직접 계단식으로 계산하는 대신
/// 엔진이 기본 제공하는 부드러운 감쇠를 그대로 활용한다.
///
/// 이 스크립트가 직접 담당하는 건 "벽에 막혔는지" 판정뿐이다. AudioSource.volume은
/// Unity가 계산하는 거리 감쇠에 곱해지는 별도의 배수이므로, 여기서는 0(막힘) 또는
/// 1(안 막힘)만 넣어주면 거리 감쇠와 자연스럽게 함께 적용된다.
/// </summary>
[RequireComponent(typeof(AudioSource))]
public class VoiceProximityController : NetworkBehaviour
{
    [Header("Distance Falloff (Unity 기본 감쇠)")]
    [SerializeField] private float fullVolumeDistance = 3f;   // 이 거리 안에서는 항상 100%
    [SerializeField] private float maxAudibleDistance = 15f;  // 이 거리를 넘으면 안 들림

    [Header("Obstacle Blocking")]
    [SerializeField] private LayerMask obstacleMask;

    private AudioSource _audioSource;

    private void Awake()
    {
        _audioSource = GetComponent<AudioSource>();

        _audioSource.spatialBlend = 1f; // 완전 3D 사운드
        _audioSource.rolloffMode = AudioRolloffMode.Logarithmic; // 자연스러운 감쇠 곡선
        _audioSource.minDistance = fullVolumeDistance;
        _audioSource.maxDistance = maxAudibleDistance;
    }

    private void Update()
    {
        Transform listener = PlayerCameraController.LocalListenerTransform;
        if (listener == null)
        {
            _audioSource.volume = 0f;
            return;
        }

        // 자기 자신(내 캐릭터)의 스피커는 자기 목소리를 재생하지 않으므로 계산할 필요 없다.
        if (Object != null && Object.HasInputAuthority)
        {
            return;
        }

        bool blocked = IsBlockedByObstacle(listener.position);
        _audioSource.volume = blocked ? 0f : 1f;
    }

    private bool IsBlockedByObstacle(Vector3 listenerPosition)
    {
        Vector3 origin = transform.position;
        Vector3 direction = listenerPosition - origin;
        float distance = direction.magnitude;

        return Physics.Raycast(origin, direction.normalized, distance, obstacleMask);
    }
}
