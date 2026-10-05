using Fusion;
using Photon.Voice.Unity;
using UnityEngine;

/// <summary>
/// 로컬 플레이어의 음성 송신 방식, 음소거(V키), 마이크 상태 UI를 연결한다.
///
/// Recorder.TransmitEnabled만 끄고 켜므로, 음소거해도 다른 사람 목소리는 계속 들린다
/// (송신만 막고 수신은 막지 않는 것이 일반적인 음소거 동작이며, Photon Voice 공식 문서에도
/// TransmitEnabled는 수신에 영향을 주지 않는다고 명시되어 있다).
///
/// 순수 로컬 UX 기능이라 네트워크 동기화가 필요 없다. 다른 사람에게 "음소거 중" 표시가
/// 필요해지면(예: 말풍선 아이콘) 그때 이 상태를 [Networked] bool로 승격하면 된다.
/// </summary>
[RequireComponent(typeof(Recorder))]
public class VoiceMuteController : MonoBehaviour
{
    [SerializeField] private KeyCode muteKey = KeyCode.V;
    [SerializeField] private KeyCode pushToTalkKey = KeyCode.T; //눌러서 말하기 송신 키
    [SerializeField] private MicrophoneUIComponent microphoneUIPrefab; //본인에게만 표시할 마이크 UI

    private Recorder _recorder;
    private NetworkObject _networkObject;
    private bool _transmissionAllowed = true;
    private static bool _localMuted;
    private MicrophoneUIComponent microphoneUI; //로컬 마이크 상태 표시
    private bool applicationFocused = true; //창 전환 시 눌러서 말하기 송신 차단

    public bool IsMuted { get; private set; }

    private void Awake()
    {
        _recorder = GetComponent<Recorder>();
        _networkObject = GetComponent<NetworkObject>();
        IsMuted = _localMuted;
        if (GetComponent<PlayerVoiceChannelController>() != null)
            _transmissionAllowed = false;
    }

    private void Update()
    {
        if (_networkObject != null && (!_networkObject.IsValid || !_networkObject.HasInputAuthority)) return;
        if (Input.GetKeyDown(muteKey))
        {
            ToggleMute();
        }
        applyTransmission(applicationFocused && Input.GetKey(pushToTalkKey));
    }

    private void ToggleMute()
    {
        IsMuted = !IsMuted;
        _localMuted = IsMuted;
        applyTransmission(applicationFocused && Input.GetKey(pushToTalkKey));

        Debug.Log($"[VoiceMuteController] 음소거: {IsMuted}");
    }

    internal void SetTransmissionAllowed(bool allowed)
    {
        _transmissionAllowed = allowed;
        applyTransmission(applicationFocused && Input.GetKey(pushToTalkKey));
    }

    internal void initializeLocalUI() //로컬 네트워크 캐릭터의 마이크 표시 생성
    {
        if (_networkObject == null || !_networkObject.IsValid || !_networkObject.HasInputAuthority || !isActiveAndEnabled) return;
        if (microphoneUI == null && microphoneUIPrefab != null) microphoneUI = Instantiate(microphoneUIPrefab);
        applyTransmission(applicationFocused && Input.GetKey(pushToTalkKey));
    }

    internal void releaseLocalUI() //캐릭터 해제 시 본인 마이크 표시 정리
    {
        if (microphoneUI != null) Destroy(microphoneUI.gameObject);
        microphoneUI = null;
    }

    private void applyTransmission(bool pushToTalkPressed) //모드와 권한·음소거를 함께 적용
    {
        if (_recorder == null) return;
        bool pushToTalk = GameAudio.getVoiceInputMode() == VoiceInputMode.PushToTalk; //현재 송신 방식
        bool ready = isActiveAndEnabled && _transmissionAllowed && _recorder.RecordingEnabled && _recorder.VoiceDetector != null; //실제 송신 준비 여부
        if (_recorder.VoiceDetection == pushToTalk) _recorder.VoiceDetection = !pushToTalk;
        _recorder.TransmitEnabled = ready && !IsMuted && (!pushToTalk || pushToTalkPressed);
        if (microphoneUI != null) microphoneUI.setState(IsMuted, ready, pushToTalk, pushToTalkPressed, muteKey, pushToTalkKey);
    }

    private void OnApplicationFocus(bool focused) //창을 벗어나면 눌러서 말하기 송신 중지
    {
        applicationFocused = focused;
        if (_networkObject != null && _networkObject.IsValid && _networkObject.HasInputAuthority)
            applyTransmission(focused && Input.GetKey(pushToTalkKey));
    }

    private void OnEnable() //기존 로컬 표시를 다시 활성화
    {
        if (microphoneUI != null) microphoneUI.gameObject.SetActive(true);
    }

    private void OnDisable() //시체의 중복 마이크 표시와 송신 해제
    {
        if (_recorder != null) _recorder.TransmitEnabled = false;
        if (microphoneUI != null) microphoneUI.gameObject.SetActive(false);
    }

    private void OnDestroy() //씬 종료 시 로컬 마이크 표시 정리
    {
        releaseLocalUI();
    }
}
