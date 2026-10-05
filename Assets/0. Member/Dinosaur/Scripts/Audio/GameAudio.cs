using UnityEngine;
using UnityEngine.SceneManagement;

public class GameAudio : MonoBehaviour
{
    [SerializeField] private AudioSettingsComponent settings; //음량 저장 담당
    [SerializeField] private GameAudioComponent audioComponent; //공용 사운드 재생 담당
    [SerializeField] private GameSettingsUIComponent settingsUI; //설정창 담당

    private static GameAudio instance; //클라이언트 하나의 사운드 진입점
    internal static bool blocksPlayerInput => instance != null && instance.settingsUI.blocksPlayerInput; //설정창 입력 차단

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void resetInstance() //플레이 시작 시 정적 참조 초기화
    {
        instance = null;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void bootstrap() //어느 게임 씬에서 시작해도 공용 프리팹 한 번 생성
    {
        if (instance != null) return;
        GameAudio prefab = Resources.Load<GameAudio>("GameAudio");
        if (prefab == null)
        {
            Debug.LogError("[GameAudio] Resources/GameAudio 프리팹이 없습니다.");
            return;
        }
        Instantiate(prefab);
    }

    private void Awake() //구성 요소 초기화와 씬 전환 연결
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }
        instance = this;
        DontDestroyOnLoad(gameObject);
        settings.initialize();
        audioComponent.initialize(settings);
        settingsUI.initialize(this);
        SceneManager.sceneLoaded += handleSceneLoaded;
    }

    private void handleSceneLoaded(Scene scene, LoadSceneMode mode) //씬별 재생과 설정창 상태 전달
    {
        if (scene.name != "Main" && scene.name != "Lobby" && scene.name != "StandBy" && scene.name != "GamePlay") return;
        audioComponent.changeScene(scene.name);
        settingsUI.changeScene(scene.name);
    }

    internal float getVolume(AudioVolumeChannel channel) //설정창의 음량 조회 진입점
    {
        return settings.getVolume(channel);
    }

    internal void setVolume(AudioVolumeChannel channel, float volume) //설정창의 음량 변경 진입점
    {
        settings.setVolume(channel, volume);
    }

    internal void saveSettings() //설정 저장 진입점
    {
        settings.save();
    }

    internal static VoiceInputMode getVoiceInputMode() //로컬 마이크의 송신 방식 조회 진입점
    {
        return instance != null ? instance.settings.getVoiceInputMode() : VoiceInputMode.VoiceActivation;
    }

    internal void setVoiceInputMode(VoiceInputMode mode) //설정창의 송신 방식 변경 진입점
    {
        settings.setVoiceInputMode(mode);
    }

    internal static float getEffectsVolume() //플레이어 효과음에 적용할 음량
    {
        return instance != null ? instance.settings.getEffectiveVolume(AudioVolumeChannel.Effects) : 1f;
    }

    internal static float getVoiceVolume() //기존 음성 거리 판정에 곱할 수신 음량
    {
        return instance != null ? instance.settings.getEffectiveVolume(AudioVolumeChannel.Voice) : 1f;
    }

    internal static void playMissionResult(bool completed) //호스트가 확정한 본인의 미션 결과 재생
    {
        if (instance != null) instance.audioComponent.playMissionResult(completed);
    }

    private void OnApplicationPause(bool paused) //앱 일시 중단 시 설정 저장
    {
        if (paused && instance == this) settings.save();
    }

    private void OnDestroy() //씬 구독과 공용 참조 정리
    {
        if (instance != this) return;
        SceneManager.sceneLoaded -= handleSceneLoaded;
        settings.save();
        instance = null;
    }
}
