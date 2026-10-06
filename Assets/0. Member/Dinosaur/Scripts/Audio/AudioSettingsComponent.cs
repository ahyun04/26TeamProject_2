using System;
using UnityEngine;

internal enum AudioVolumeChannel
{
    Master,
    Music,
    Effects,
    Voice
}

internal enum VoiceInputMode
{
    VoiceActivation,
    PushToTalk
}

public class AudioSettingsComponent : MonoBehaviour
{
    private const string preferencePrefix = "TrustNoOne.Audio."; //로컬 음량 저장 키
    [SerializeField, Range(0f, 1f)] private float defaultMasterVolume = 1f; //전체 기본 음량
    [SerializeField, Range(0f, 1f)] private float defaultMusicVolume = 1f; //배경음 기본 음량
    [SerializeField, Range(0f, 1f)] private float defaultEffectsVolume = 1f; //효과음 기본 음량
    [SerializeField, Range(0f, 1f)] private float defaultVoiceVolume = 1f; //음성 수신 기본 음량

    private readonly float[] volumes = new float[4]; //현재 로컬 음량
    private readonly float[] savedVolumes = new float[4]; //적용을 마친 음량
    private VoiceInputMode voiceInputMode; //현재 로컬 음성 송신 방식
    private VoiceInputMode savedVoiceInputMode; //적용을 마친 송신 방식
    private bool isEditing; //미적용 설정을 미리 듣는 상태
    internal event Action changed; //음량 변경 알림

    internal void initialize() //저장된 음량 불러오기
    {
        float[] defaults = { defaultMasterVolume, defaultMusicVolume, defaultEffectsVolume, defaultVoiceVolume };
        for (int i = 0; i < volumes.Length; i++)
        {
            float saved = PlayerPrefs.GetFloat(preferencePrefix + (AudioVolumeChannel)i, defaults[i]);
            volumes[i] = float.IsNaN(saved) || float.IsInfinity(saved) ? defaults[i] : Mathf.Clamp01(saved);
        }
        int savedMode = PlayerPrefs.GetInt(preferencePrefix + "VoiceInputMode", (int)VoiceInputMode.VoiceActivation); //저장된 송신 방식
        voiceInputMode = savedMode == (int)VoiceInputMode.PushToTalk ? VoiceInputMode.PushToTalk : VoiceInputMode.VoiceActivation;
        Array.Copy(volumes, savedVolumes, volumes.Length);
        savedVoiceInputMode = voiceInputMode;
        isEditing = false;
    }

    internal VoiceInputMode getVoiceInputMode() //본인의 음성 송신 방식 조회
    {
        return voiceInputMode;
    }

    internal void setVoiceInputMode(VoiceInputMode mode) //음성 송신 방식 적용과 저장 값 갱신
    {
        if (mode != VoiceInputMode.VoiceActivation && mode != VoiceInputMode.PushToTalk) return;
        if (voiceInputMode == mode) return;
        voiceInputMode = mode;
        if (!isEditing)
        {
            savedVoiceInputMode = mode;
            PlayerPrefs.SetInt(preferencePrefix + "VoiceInputMode", (int)mode);
        }
    }

    internal float getVolume(AudioVolumeChannel channel) //슬라이더에 표시할 음량
    {
        return volumes[(int)channel];
    }

    internal float getEffectiveVolume(AudioVolumeChannel channel) //전체 음량을 곱한 실제 음량
    {
        return getVolume(AudioVolumeChannel.Master) * getVolume(channel);
    }

    internal void setVolume(AudioVolumeChannel channel, float volume) //본인의 음량 미리 듣기
    {
        if (float.IsNaN(volume) || float.IsInfinity(volume)) return;
        volume = Mathf.Clamp01(volume);
        if (Mathf.Approximately(volumes[(int)channel], volume)) return;
        volumes[(int)channel] = volume;
        if (!isEditing)
        {
            savedVolumes[(int)channel] = volume;
            PlayerPrefs.SetFloat(preferencePrefix + channel, volume);
        }
        changed?.Invoke();
    }

    internal void beginEditing() //현재 확정 설정을 취소 복원 기준으로 보관
    {
        if (isEditing) return;
        Array.Copy(volumes, savedVolumes, volumes.Length);
        savedVoiceInputMode = voiceInputMode;
        isEditing = true;
    }

    internal bool applyChanges() //적용 버튼으로만 편집값을 확정하고 저장
    {
        try
        {
            writePreferences(volumes, voiceInputMode);
            PlayerPrefs.Save();
            Array.Copy(volumes, savedVolumes, volumes.Length);
            savedVoiceInputMode = voiceInputMode;
            isEditing = false;
            return true;
        }
        catch (Exception exception)
        {
            Debug.LogError($"[AudioSettingsComponent] 설정 저장 실패: {exception.Message}");
            try { writePreferences(savedVolumes, savedVoiceInputMode); }
            catch (Exception restoreException) { Debug.LogException(restoreException); }
            return false;
        }
    }

    internal void cancelChanges() //미리 듣기를 끝내고 마지막 확정 설정으로 복원
    {
        if (!isEditing) return;
        Array.Copy(savedVolumes, volumes, volumes.Length);
        voiceInputMode = savedVoiceInputMode;
        isEditing = false;
        changed?.Invoke();
    }

    internal void restoreDefaults() //기본값을 임시 설정에만 반영
    {
        if (!isEditing) return;
        volumes[0] = defaultMasterVolume;
        volumes[1] = defaultMusicVolume;
        volumes[2] = defaultEffectsVolume;
        volumes[3] = defaultVoiceVolume;
        voiceInputMode = VoiceInputMode.VoiceActivation;
        changed?.Invoke();
    }

    private void writePreferences(float[] values, VoiceInputMode mode) //확정할 설정의 로컬 저장 값 작성
    {
        for (int i = 0; i < values.Length; i++)
            PlayerPrefs.SetFloat(preferencePrefix + (AudioVolumeChannel)i, values[i]);
        PlayerPrefs.SetInt(preferencePrefix + "VoiceInputMode", (int)mode);
    }

    internal void save() //앱 종료에서도 확정 설정만 디스크 저장
    {
        try
        {
            PlayerPrefs.Save();
        }
        catch (Exception exception)
        {
            Debug.LogError($"[AudioSettingsComponent] 설정 저장 실패: {exception.Message}");
        }
    }
}
