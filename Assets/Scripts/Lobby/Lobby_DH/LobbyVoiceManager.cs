using Fusion;
using Photon.Voice.Unity;
using UnityEngine;

namespace LockdownProtocol.Lobby
{
    /// <summary>
    /// 로비 전용 음성 채널. 거리 기반 볼륨(부드러운 커브)을 담당하고,
    /// 게임 시작이 승인되는 순간 송신을 끊는다.
    ///
    /// 인게임 음성(VoiceProximityController)과는 물리적으로 다른 프리팹(LobbyPlayerController)에
    /// 붙어 있어서, 로비 오브젝트가 게임 씬 전환 시 Despawn되면 자연스럽게 채널이 사라진다 —
    /// 별도의 그룹 전환 로직 없이도 "로비 채널 종료 -> 게임 채널 참가" 흐름이 성립한다.
    /// 다만 "게임 시작 승인" 시점(실제 Despawn보다 앞선 타이밍)에 미리 송신을 끊어달라는
    /// 기획 요구사항이 있어, LobbyGameStartManager의 카운트다운 시작 이벤트를 구독해 처리한다.
    ///
    /// 음소거(V키)는 VoiceMuteController가 게임/로비 공용으로 그대로 재사용 가능 —
    /// Recorder.TransmitEnabled만 다루는 순수 로컬 UX라 로비 전용으로 새로 만들 필요 없음.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    [RequireComponent(typeof(Recorder))]
    public class LobbyVoiceManager : NetworkBehaviour
    {
        [Header("Distance Falloff")]
        [SerializeField] private float maxAudibleDistance = 15f;

        private AudioSource _audioSource;
        private Recorder _recorder;
        private LobbyGameStartManager _gameStartManager;
        private bool _channelClosed;

        public override void Spawned()
        {
            _audioSource = GetComponent<AudioSource>();
            _recorder = GetComponent<Recorder>();

            _audioSource.spatialBlend = 1f;
            _audioSource.rolloffMode = AudioRolloffMode.Custom;
            _audioSource.maxDistance = maxAudibleDistance;
            _audioSource.SetCustomCurve(AudioSourceCurveType.CustomRolloff, BuildFalloffCurve());

            _gameStartManager = FindFirstObjectByType<LobbyGameStartManager>();
            if (_gameStartManager != null)
            {
                _gameStartManager.CountdownStarted += HandleGameStarting;
            }
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            if (_gameStartManager != null)
            {
                _gameStartManager.CountdownStarted -= HandleGameStarting;
            }
        }

        private void Update()
        {
            if (_channelClosed) return;

            // 자기 자신의 스피커는 자기 목소리를 재생하지 않는다
            if (Object != null && Object.HasInputAuthority) return;

            Transform listener = LobbyMovementController.LocalListenerTransform;
            _audioSource.volume = listener == null ? 0f : 1f;
            // 실제 거리 감쇠 값 자체는 AudioSource의 Custom Rolloff 커브가 3D 위치 기준으로
            // 알아서 계산해준다. volume은 "커브를 탈지 말지"의 on/off 스위치로만 쓴다.
        }

        private AnimationCurve BuildFalloffCurve()
        {
            // 기획서 표: 0~3m 100% / 3~7m 70% / 7~12m 40% / 12~15m 10% / 15m 이상 0%
            // "계단식으로 적었지만 자연스럽게 줄었다 커지는 느낌"을 원한다고 해서
            // 각 지점을 Smooth 탄젠트로 이어 부드러운 곡선으로 만든다.
            // Custom Rolloff 커브의 x축은 0~1로 정규화된 거리(distance / maxDistance) 기준.
            var curve = new AnimationCurve();
            curve.AddKey(0f, 1f);
            curve.AddKey(3f / maxAudibleDistance, 1f);
            curve.AddKey(7f / maxAudibleDistance, 0.7f);
            curve.AddKey(12f / maxAudibleDistance, 0.4f);
            curve.AddKey(1f, 0f); // 15m = maxAudibleDistance 지점

            for (int i = 0; i < curve.length; i++)
            {
                curve.SmoothTangents(i, 0f);
            }

            return curve;
        }

        private void HandleGameStarting()
        {
            // 게임 시작 승인 시점 -> 로비 음성 채널 종료 (기획서 요구사항)
            _channelClosed = true;
            _audioSource.volume = 0f;

            if (Object != null && Object.HasInputAuthority)
            {
                _recorder.TransmitEnabled = false;
            }
        }
    }
}