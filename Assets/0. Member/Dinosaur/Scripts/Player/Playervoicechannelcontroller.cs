using Fusion;
using LockdownProtocol.Lobby;
using Photon.Realtime;
using Photon.Voice.Fusion;
using Photon.Voice.Unity;
using UnityEngine;

/// <summary>
/// 공용 플레이어의 생존/관전/탈출 음성 채널과 대기실 송신 상태를 관리한다.
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
[RequireComponent(typeof(VoiceNetworkObject))]
[RequireComponent(typeof(Recorder))]
[RequireComponent(typeof(VoiceMuteController))]
public class PlayerVoiceChannelController : NetworkBehaviour
{
    private enum VoiceChannelState { Survivor, Spectator, Escaped }

    private const byte SurvivorGroup = 1;
    private const byte SpectatorGroup = 2;

    private PlayerHealth _health;
    private Recorder _recorder;
    private FusionVoiceClient _voiceClient;
    private VoiceNetworkObject _voiceObject;
    private VoiceMuteController _muteController;
    private bool _isLobbyPlayer;

    // null이면 "아직 한 번도 채널을 적용 안 함", 그 이후로는 실제 상태 변화가 있을 때만 재적용한다.
    // OpChangeGroups는 호출할 때마다 서버에 네트워크 요청을 보내므로, 매 프레임 호출하면 낭비다.
    private VoiceChannelState? _lastAppliedState;

    public override void Spawned()
    {
        _recorder = GetComponent<Recorder>();
        _recorder.RecordingEnabled = false;
        if (!Object.HasInputAuthority) return;

        _health = GetComponent<PlayerHealth>();
        _voiceObject = GetComponent<VoiceNetworkObject>();
        _muteController = GetComponent<VoiceMuteController>();
        _muteController.SetTransmissionAllowed(false);
        _voiceClient = Runner.GetComponent<FusionVoiceClient>();
        _isLobbyPlayer = LobbyRoomUI.Instance != null;

        if (_recorder == null || _voiceClient == null)
        {
            Debug.LogWarning("[PlayerVoiceChannelController] Recorder 또는 FusionVoiceClient를 찾을 수 없습니다.");
        }
    }

    private void Update()
    {
        if (Object == null || !Object.IsValid || !Object.HasInputAuthority) return;
        if (_recorder == null || _voiceClient == null || _muteController == null) return;
        if (_voiceObject.RecorderInUse != _recorder || _voiceClient.Client.State != ClientState.Joined)
        {
            _muteController.SetTransmissionAllowed(false);
            _lastAppliedState = null;
            return;
        }

        VoiceChannelState currentState = DetermineState();

        if (!_lastAppliedState.HasValue || _lastAppliedState.Value != currentState)
        {
            if (!ApplyChannel(currentState))
            {
                _muteController.SetTransmissionAllowed(false);
                return;
            }
            _lastAppliedState = currentState;
        }

        RoomManager room = RoomManager.Instance;
        bool lobbyWaiting = !_isLobbyPlayer || (room != null && room.Object != null && room.Object.IsValid &&
            room.Runner == Runner && room.CurrentRoomState == RoomManager.RoomState.Waiting);
        _muteController.SetTransmissionAllowed(lobbyWaiting && currentState != VoiceChannelState.Escaped);
        if (!_recorder.RecordingEnabled) _recorder.RecordingEnabled = true;
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        if (_muteController != null) _muteController.SetTransmissionAllowed(false);
        if (_recorder != null) _recorder.RecordingEnabled = false;
        _lastAppliedState = null;
    }

    private VoiceChannelState DetermineState()
    {
        if (_health.IsEscaped) return VoiceChannelState.Escaped;
        if (_health.IsDead) return VoiceChannelState.Spectator;
        return VoiceChannelState.Survivor;
    }

    private bool ApplyChannel(VoiceChannelState state)
    {
        bool accepted;
        switch (state)
        {
            case VoiceChannelState.Survivor:
                _recorder.InterestGroup = SurvivorGroup;
                accepted = _voiceClient.Client.OpChangeGroups(new byte[] { SpectatorGroup }, new byte[] { SurvivorGroup });
                break;

            case VoiceChannelState.Spectator:
                _recorder.InterestGroup = SpectatorGroup;
                accepted = _voiceClient.Client.OpChangeGroups(null, new byte[] { SurvivorGroup, SpectatorGroup });
                break;

            case VoiceChannelState.Escaped:
                accepted = _voiceClient.Client.OpChangeGroups(new byte[] { SpectatorGroup }, new byte[] { SurvivorGroup });
                break;
            default:
                return false;
        }

        if (accepted) Debug.Log($"[PlayerVoiceChannelController] 음성 채널 전환: {state}");
        return accepted;
    }
}
