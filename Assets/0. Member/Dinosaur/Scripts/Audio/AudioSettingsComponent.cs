using System;
using UnityEngine;

internal enum AudioVolumeChannel
{
    Master,
    Music,
    Effects,
    Voice
}

public class AudioSettingsComponent : MonoBehaviour
{
    private const string preferencePrefix = "TrustNoOne.Audio."; //로컬 음량 저장 키
    [SerializeField, Range(0f, 1f)] private float defaultMasterVolume = 1f; //전체 기본 음량
    [SerializeField, Range(0f, 1f)] private float defaultMusicVolume = 0.5f; //배경음 기본 음량
    [SerializeField, Range(0f, 1f)] private float defaultEffectsVolume = 0.8f; //효과음 기본 음량
    [SerializeField, Range(0f, 1f)] private float defaultVoiceVolume = 1f; //음성 수신 기본 음량

    private readonly float[] volumes = new float[4]; //현재 로컬 음량
    internal event Action changed; //음량 변경 알림

    internal void initialize() //저장된 음량 불러오기
    {
        float[] defaults = { defaultMasterVolume, defaultMusicVolume, defaultEffectsVolume, defaultVoiceVolume };
        for (int i = 0; i < volumes.Length; i++)
        {
            float saved = PlayerPrefs.GetFloat(preferencePrefix + (AudioVolumeChannel)i, defaults[i]);
            volumes[i] = float.IsNaN(saved) || float.IsInfinity(saved) ? defaults[i] : Mathf.Clamp01(saved);
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

    internal void setVolume(AudioVolumeChannel channel, float volume) //음량 즉시 적용과 저장 값 갱신
    {
        volume = Mathf.Clamp01(volume);
        if (Mathf.Approximately(volumes[(int)channel], volume)) return;
        volumes[(int)channel] = volume;
        PlayerPrefs.SetFloat(preferencePrefix + channel, volume);
        changed?.Invoke();
    }

    internal void save() //설정창 종료 시 디스크 저장
    {
        PlayerPrefs.Save();
    }
}
