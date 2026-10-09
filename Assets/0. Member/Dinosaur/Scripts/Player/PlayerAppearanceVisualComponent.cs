using System;
using System.Threading;
using Photon.Voice;
using Photon.Voice.Fusion;
using Photon.Voice.Unity;
using UnityEngine;

[DisallowMultipleComponent]
public class PlayerAppearanceVisualComponent : MonoBehaviour
{
    [SerializeField] private SkinnedMeshRenderer characterRenderer; //프리팹의 캐릭터 메시
    [SerializeField] private int faceMaterialIndex = 3; //검은 바이저 재질 슬롯
    [SerializeField] private int bodyMaterialIndex; //캐릭터 기본색 재질 슬롯
    [SerializeField, Min(0f)] private float emissionStrength = 2f; //말할 때 바이저 발광 세기
    [SerializeField, Min(0f)] private float voiceHoldSeconds = 0.2f; //짧은 음성 간격의 발광 유지 시간
    [SerializeField, Min(0.0001f)] private float voiceThreshold = 0.01f; //무음 패킷을 제외할 음성 크기
    [SerializeField, Min(0.01f)] private float fadeSeconds = 0.12f; //발광이 사라지는 시간

    private static readonly int baseColorProperty = Shader.PropertyToID("_BaseColor"); //기본색 셰이더 속성
    private static readonly int baseMapProperty = Shader.PropertyToID("_BaseMap"); //거의 검정인 원본 기본색 텍스처의 개별 표시 보정
    private static readonly int emissionProperty = Shader.PropertyToID("_EmissionColor"); //바이저 발광 셰이더 속성
    private MaterialPropertyBlock bodyProperties; //본인 메시의 기본색 변경
    private MaterialPropertyBlock faceProperties; //본인 메시의 바이저 발광 변경
    private VoiceNetworkObject voiceObject; //기존 Photon Voice 연결
    private PlayerHealth health; //사망·탈출·결과의 표시 차단
    private RemoteVoiceLink remoteVoice; //현재 관찰하는 원격 음성
    private bool isLocal; //본인 마이크를 사용하는 캐릭터 여부
    private bool initialized; //네트워크 표시 연결 완료 여부
    private int voicedFrame; //음성 스레드가 전달한 실제 발화 신호
    private float holdTimer; //발화 유지 남은 시간
    private float glow; //현재 발광 비율
    private Color appearanceColor = Color.white; //배정된 캐릭터 색상

    private void Awake() //메시별 속성 블록 준비
    {
        if (characterRenderer == null) characterRenderer = GetComponentInChildren<SkinnedMeshRenderer>(true);
        bodyProperties = new MaterialPropertyBlock();
        faceProperties = new MaterialPropertyBlock();
        resetGlow();
    }

    internal void initialize(VoiceNetworkObject connection, PlayerHealth playerHealth, bool local) //기존 음성·생존 상태 연결
    {
        voiceObject = connection;
        health = playerHealth;
        isLocal = local;
        initialized = true;
    }

    internal void setColor(Color color) //공유 재질을 수정하지 않고 해당 캐릭터만 색상 적용
    {
        appearanceColor = color;
        if (characterRenderer == null) return;
        characterRenderer.GetPropertyBlock(bodyProperties, bodyMaterialIndex);
        bodyProperties.SetColor(baseColorProperty, color);
        bodyProperties.SetTexture(baseMapProperty, Texture2D.whiteTexture);
        characterRenderer.SetPropertyBlock(bodyProperties, bodyMaterialIndex);
        applyGlow();
    }

    private void Update() //실제 송신·수신 음성으로 로컬 발광 갱신
    {
        if (!initialized) return;
        if (health == null || !health.CanAct || voiceObject == null)
        {
            resetGlow();
            return;
        }
        bool talking = false; //이번 프레임의 실제 발화 여부
        bool ready = false; //해당 캐릭터 음성 연결 준비 여부
        if (isLocal)
        {
            Recorder recorder = voiceObject.RecorderInUse; //본인 송신기
            ready = recorder != null && recorder.RecordingEnabled && recorder.TransmitEnabled;
            if (ready && recorder.IsCurrentlyTransmitting)
                talking = recorder.VoiceDetection
                    ? recorder.VoiceDetector != null && recorder.VoiceDetector.Detected
                    : recorder.LevelMeter != null && recorder.LevelMeter.CurrentPeakAmp >= voiceThreshold;
        }
        else
        {
            Speaker speaker = voiceObject.SpeakerInUse; //원격 캐릭터의 실제 재생기
            bindRemoteVoice(speaker != null ? speaker.RemoteVoice : null);
            ready = speaker != null && speaker.IsPlaying && remoteVoice != null;
            talking = Interlocked.Exchange(ref voicedFrame, 0) != 0;
        }
        if (!ready)
        {
            resetGlow();
            return;
        }
        holdTimer = talking ? voiceHoldSeconds : Mathf.Max(0f, holdTimer - Time.unscaledDeltaTime);
        glow = Mathf.MoveTowards(glow, holdTimer > 0f ? 1f : 0f, Time.unscaledDeltaTime / fadeSeconds);
        applyGlow();
    }

    private void bindRemoteVoice(RemoteVoiceLink connection) //교체된 원격 음성 구독 정리와 연결
    {
        if (remoteVoice == connection) return;
        if (remoteVoice != null) remoteVoice.FloatFrameDecoded -= handleRemoteFrame;
        remoteVoice = connection;
        Interlocked.Exchange(ref voicedFrame, 0);
        if (remoteVoice != null) remoteVoice.FloatFrameDecoded += handleRemoteFrame;
    }

    private void handleRemoteFrame(FrameOut<float> frame) //음성 스레드에서는 신호만 전달
    {
        foreach (float sample in frame.Buf)
            if (Math.Abs(sample) >= voiceThreshold) { Interlocked.Exchange(ref voicedFrame, 1); break; }
    }

    private void applyGlow() //검은 바이저 슬롯에만 발광색 적용
    {
        if (characterRenderer == null) return;
        characterRenderer.GetPropertyBlock(faceProperties, faceMaterialIndex);
        Color emissionColor = QualitySettings.activeColorSpace == ColorSpace.Linear ? appearanceColor.linear : appearanceColor; //선택 색상에 맞는 발광 계산 색 공간
        faceProperties.SetVector(emissionProperty, (Vector4)(emissionColor * (emissionStrength * glow)));
        characterRenderer.SetPropertyBlock(faceProperties, faceMaterialIndex);
    }

    private void resetGlow() //송신 불가·사망·연결 종료에서 발광 해제
    {
        holdTimer = 0f;
        glow = 0f;
        Interlocked.Exchange(ref voicedFrame, 0);
        applyGlow();
    }

    internal void releaseVoice() //비활성 캐릭터의 음성 구독과 표시 정리
    {
        initialized = false;
        bindRemoteVoice(null);
        resetGlow();
    }

    private void OnDisable() //비활성화된 표시의 수신과 발광 해제
    {
        bindRemoteVoice(null);
        resetGlow();
    }

    private void OnDestroy() //남은 음성 구독 해제
    {
        bindRemoteVoice(null);
    }
}
