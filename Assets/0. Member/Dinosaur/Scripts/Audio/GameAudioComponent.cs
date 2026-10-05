using UnityEngine;

public class GameAudioComponent : MonoBehaviour
{
    [SerializeField] private AudioSource musicSource; //배경음 재생기
    [SerializeField] private AudioSource effectsSource; //본인의 미션음 재생기
    [SerializeField] private AudioClip mainMusic; //메인과 로비 배경음
    [SerializeField] private AudioClip standByMusic; //대기실 배경음
    [SerializeField] private AudioClip missionCompleteSound; //미션 성공음
    [SerializeField] private AudioClip missionFailedSound; //미션 실패음

    private AudioSettingsComponent settings; //로컬 음량 설정

    internal void initialize(AudioSettingsComponent audioSettings) //재생 설정과 음량 변경 연결
    {
        settings = audioSettings;
        musicSource.playOnAwake = false;
        musicSource.loop = true;
        musicSource.spatialBlend = 0f;
        effectsSource.playOnAwake = false;
        effectsSource.loop = false;
        effectsSource.spatialBlend = 0f;
        settings.changed += applyVolume;
        applyVolume();
    }

    internal void changeScene(string sceneName) //현재 씬에 맞는 배경음 재생
    {
        AudioClip nextClip = sceneName == "Main" || sceneName == "Lobby" ? mainMusic
            : sceneName == "StandBy" ? standByMusic : null;
        if (musicSource.clip == nextClip && (nextClip == null || musicSource.isPlaying)) return;
        musicSource.Stop();
        musicSource.clip = nextClip;
        if (nextClip != null) musicSource.Play();
    }

    internal void playMissionResult(bool completed) //미션 결과 효과음 재생
    {
        AudioClip clip = completed ? missionCompleteSound : missionFailedSound;
        if (clip != null) effectsSource.PlayOneShot(clip);
    }

    private void applyVolume() //재생 중인 배경음과 효과음 음량 갱신
    {
        musicSource.volume = settings.getEffectiveVolume(AudioVolumeChannel.Music);
        effectsSource.volume = settings.getEffectiveVolume(AudioVolumeChannel.Effects);
    }

    private void OnDestroy() //음량 변경 구독 정리
    {
        if (settings != null) settings.changed -= applyVolume;
    }
}
