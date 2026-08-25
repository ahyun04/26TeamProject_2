using Fusion;
using Photon.Voice.Fusion;
using Photon.Voice.Unity;
using UnityEngine;

/// <summary>
/// 생존/관전/탈출 음성 채널 분리. Photon Voice의 Interest Group 기능을 사용한다.
///
/// 생존: 생존자 그룹에만 방송(InterestGroup), 생존자 그룹만 청취.
/// 사망/관전: 관전자 그룹에 방송, 생존자+관전자 그룹 둘 다 청취.
/// 탈출: 송신 완전 차단(TransmitEnabled=false), 생존자 그룹만 청취 (관전자 그룹은 청취 안 함).
///
/// (기획서에 "사망 대기" 상태의 음성 권한이 별도로 명시되어 있지 않아 관전과 동일하게 처리한다.
///  기획팀 확인 후 다르면 조정 필요.)
///
/// 이 로직은 오직 "나 자신의 송수신 설정"을 바꾸는 것이라 로컬 플레이어에서만 실행한다.
/// </summary>
[RequireComponent(typeof(PlayerHealth))]
public class PlayerVoiceChannelController : NetworkBehaviour
{
    private enum VoiceChannelState { Survivor, Spectator, Escaped }

    private const byte SurvivorGroup = 1;
    private const byte SpectatorGroup = 2;

    private PlayerHealth _health;
    private Recorder _recorder;
    private FusionVoiceClient _voiceClient;

    // null이면 "아직 한 번도 채널을 적용 안 함", 그 이후로는 실제 상태 변화가 있을 때만 재적용한다.
    // OpChangeGroups는 호출할 때마다 서버에 네트워크 요청을 보내므로, 매 프레임 호출하면 낭비다.
    private VoiceChannelState? _lastAppliedState;

    public override void Spawned()
    {
        if (!Object.HasInputAuthority) return;

        _health = GetComponent<PlayerHealth>();
        _recorder = FindObjectOfType<Recorder>();
        _voiceClient = FindObjectOfType<FusionVoiceClient>();

        if (_recorder == null || _voiceClient == null)
        {
            Debug.LogWarning("[PlayerVoiceChannelController] Recorder 또는 FusionVoiceClient를 찾을 수 없습니다.");
        }
    }

    private void Update()
    {
        if (!Object.HasInputAuthority) return;
        if (_recorder == null || _voiceClient == null) return;

        VoiceChannelState currentState = DetermineState();

        if (!_lastAppliedState.HasValue || _lastAppliedState.Value != currentState)
        {
            ApplyChannel(currentState);
            _lastAppliedState = currentState;
        }

        // 탈출 상태에서는 VoiceMuteController(V키)가 송신을 다시 켜더라도 매 프레임 강제로 차단한다.
        // 탈출한 플레이어는 어떤 경우에도 생존자에게 목소리가 들리면 안 되기 때문이다.
        if (currentState == VoiceChannelState.Escaped)
        {
            _recorder.TransmitEnabled = false;
        }
    }

    private VoiceChannelState DetermineState()
    {
        if (_health.IsEscaped) return VoiceChannelState.Escaped;
        if (_health.IsDead) return VoiceChannelState.Spectator;
        return VoiceChannelState.Survivor;
    }

    private void ApplyChannel(VoiceChannelState state)
    {
        switch (state)
        {
            case VoiceChannelState.Survivor:
                _recorder.TransmitEnabled = true;
                _recorder.InterestGroup = SurvivorGroup;
                _voiceClient.Client.OpChangeGroups(new byte[] { SpectatorGroup }, new byte[] { SurvivorGroup });
                break;

            case VoiceChannelState.Spectator:
                _recorder.TransmitEnabled = true;
                _recorder.InterestGroup = SpectatorGroup;
                _voiceClient.Client.OpChangeGroups(null, new byte[] { SurvivorGroup, SpectatorGroup });
                break;

            case VoiceChannelState.Escaped:
                _recorder.TransmitEnabled = false;
                _voiceClient.Client.OpChangeGroups(new byte[] { SpectatorGroup }, new byte[] { SurvivorGroup });
                break;
        }

        Debug.Log($"[PlayerVoiceChannelController] 음성 채널 전환: {state}");
    }
}