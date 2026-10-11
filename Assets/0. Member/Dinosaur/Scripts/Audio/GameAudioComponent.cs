using UnityEngine;

public class GameAudioComponent : MonoBehaviour
{
    [SerializeField] private AudioSource musicSource; //배경음 재생기
    [SerializeField] private AudioSource effectsSource; //본인의 미션음 재생기
    [SerializeField] private AudioClip mainMusic; //메인과 로비 배경음
    [SerializeField] private AudioClip standByMusic; //대기실 배경음
    [SerializeField] private AudioClip missionCompleteSound; //미션 성공음
    [SerializeField] private AudioClip missionFailedSound; //미션 실패음
    [SerializeField, Min(0f)] private float missionCompleteStartSeconds = 0.76f; //완료음 앞 무음을 건너뛸 시간

    private AudioSettingsComponent settings; //로컬 음량 설정
    private AudioClip missionCompletePlayback; //원본 음원을 보존한 완료음 재생용 클립

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
        prepareMissionCompleteSound();
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
        AudioClip clip = completed ? missionCompletePlayback : missionFailedSound;
        if (clip != null) effectsSource.PlayOneShot(clip);
    }

    internal void playPlayerDeath(AudioClip clip, float volume) //씬 전환에도 끊기지 않는 본인 사망음
    {
        if (clip != null) effectsSource.PlayOneShot(clip, Mathf.Clamp01(volume));
    }

    private void prepareMissionCompleteSound() //앞 무음만 제외한 클립을 초기화 때 한 번 준비
    {
        missionCompletePlayback = missionCompleteSound;
        if (missionCompleteSound == null || missionCompleteStartSeconds <= 0f) return;
        missionCompleteSound.LoadAudioData();
        int startSample = Mathf.Clamp(Mathf.FloorToInt(missionCompleteStartSeconds * missionCompleteSound.frequency),
            0, missionCompleteSound.samples - 1); //채널별 재생 시작 프레임
        float[] source = new float[missionCompleteSound.samples * missionCompleteSound.channels]; //원본 PCM 데이터
        if (!missionCompleteSound.GetData(source, 0))
        {
            Debug.LogError("[GameAudio] 미션 완료음의 무음 구간을 제외할 수 없습니다.");
            return;
        }
        int sampleCount = missionCompleteSound.samples - startSample; //남은 채널별 프레임 수
        float[] samples = new float[sampleCount * missionCompleteSound.channels]; //즉시 들리는 재생 데이터
        System.Array.Copy(source, startSample * missionCompleteSound.channels, samples, 0, samples.Length);
        missionCompletePlayback = AudioClip.Create("MissionComplete Immediate", sampleCount,
            missionCompleteSound.channels, missionCompleteSound.frequency, false);
        missionCompletePlayback.SetData(samples, 0);
    }

    private void applyVolume() //재생 중인 배경음과 효과음 음량 갱신
    {
        musicSource.volume = settings.getEffectiveVolume(AudioVolumeChannel.Music);
        effectsSource.volume = settings.getEffectiveVolume(AudioVolumeChannel.Effects);
    }

    private void OnDestroy() //음량 변경 구독 정리
    {
        if (settings != null) settings.changed -= applyVolume;
        if (missionCompletePlayback != null && missionCompletePlayback != missionCompleteSound)
            Destroy(missionCompletePlayback);
    }
}
