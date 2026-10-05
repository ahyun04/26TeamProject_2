using LockdownProtocol.Lobby;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

[DefaultExecutionOrder(-100)]
public class GameSettingsUIComponent : MonoBehaviour
{
    [SerializeField] private GameObject panel; //설정창 패널
    [SerializeField] private Button openButton; //메뉴의 설정 버튼
    [SerializeField] private Button closeButton; //설정창 닫기 버튼
    [SerializeField] private Slider masterSlider; //전체 음량 조절
    [SerializeField] private Slider musicSlider; //배경음 음량 조절
    [SerializeField] private Slider effectsSlider; //효과음 음량 조절
    [SerializeField] private Slider voiceSlider; //음성 수신 음량 조절
    [SerializeField] private TMP_Text masterValueText; //전체 음량 표시
    [SerializeField] private TMP_Text musicValueText; //배경음 음량 표시
    [SerializeField] private TMP_Text effectsValueText; //효과음 음량 표시
    [SerializeField] private TMP_Text voiceValueText; //음성 수신 음량 표시
    [SerializeField] private Button voiceActivationButton; //음성 인식 송신 선택
    [SerializeField] private Button pushToTalkButton; //눌러서 말하기 선택
    [SerializeField] private TMP_Text voiceModeHintText; //선택한 송신 방식의 사용법
    [SerializeField] private EventSystem fallbackEventSystem; //씬에 UI 입력 시스템이 없을 때 사용

    private GameAudio owner; //설정 기능 진입점
    private GameEndSystem gameEndSystem; //결과 화면 우선 표시
    private string sceneName; //현재 게임 씬
    private bool isOpen; //현재 설정창 상태
    private int toggleFrame = -1; //Esc로 닫은 프레임의 입력 재사용 방지
    internal bool blocksPlayerInput => isOpen || toggleFrame == Time.frameCount; //로컬 게임 입력 차단

    private void Awake() //버튼과 슬라이더 입력 연결
    {
        panel.SetActive(false);
        openButton.onClick.AddListener(open);
        closeButton.onClick.AddListener(close);
        masterSlider.onValueChanged.AddListener(setMasterVolume);
        musicSlider.onValueChanged.AddListener(setMusicVolume);
        effectsSlider.onValueChanged.AddListener(setEffectsVolume);
        voiceSlider.onValueChanged.AddListener(setVoiceVolume);
        voiceActivationButton.onClick.AddListener(selectVoiceActivation);
        pushToTalkButton.onClick.AddListener(selectPushToTalk);
    }

    internal void initialize(GameAudio gameAudio) //기능 진입점과 저장된 음량 연결
    {
        owner = gameAudio;
        refreshValues();
    }

    internal void changeScene(string currentSceneName) //씬 전환 시 설정창 닫기와 입력 시스템 선택
    {
        sceneName = currentSceneName;
        gameEndSystem = null;
        close();
        toggleFrame = -1;
        bool hasSceneEventSystem = false;
        foreach (EventSystem eventSystem in FindObjectsByType<EventSystem>(FindObjectsSortMode.None))
        {
            if (eventSystem != fallbackEventSystem && eventSystem.isActiveAndEnabled)
            {
                hasSceneEventSystem = true;
                break;
            }
        }
        fallbackEventSystem.gameObject.SetActive(!hasSceneEventSystem);
    }

    private void Update() //설정창 Esc 처리와 다른 필수 화면 우선 처리
    {
        if (sceneName == "GamePlay" && gameEndSystem == null)
            gameEndSystem = FindFirstObjectByType<GameEndSystem>();
        if (SessionDisconnectUIComponent.IsOpen || isGameEnded())
        {
            if (isOpen) close();
            openButton.gameObject.SetActive(false);
            return;
        }
        if (!Input.GetKeyDown(KeyCode.Escape)) return;
        if (isOpen) close();
        else if (sceneName == "GamePlay" && PlayerCameraController.LocalListenerTransform != null) open();
    }

    private void LateUpdate() //기존 방 메뉴와 설정 버튼 표시 협력
    {
        bool canShowButton = !isOpen && !SessionDisconnectUIComponent.IsOpen && !isGameEnded() &&
            (sceneName == "Main" || sceneName == "Lobby" ||
             (sceneName == "StandBy" && LobbyRoomUI.Instance != null && LobbyRoomUI.Instance.IsRoomMenuOpen));
        if (openButton.gameObject.activeSelf != canShowButton) openButton.gameObject.SetActive(canShowButton);
        if (isOpen)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }

    private bool isGameEnded() //유효한 결과 상태 확인
    {
        return gameEndSystem != null && gameEndSystem.Object != null &&
            gameEndSystem.Object.IsValid && gameEndSystem.IsGameEnded;
    }

    private void open() //설정창 열기와 본인의 게임 입력 차단
    {
        if (owner == null || SessionDisconnectUIComponent.IsOpen || isGameEnded()) return;
        isOpen = true;
        toggleFrame = Time.frameCount;
        refreshValues();
        panel.SetActive(true);
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    private void close() //설정 저장과 현재 화면에 맞는 커서 복원
    {
        if (!isOpen) return;
        isOpen = false;
        toggleFrame = Time.frameCount;
        panel.SetActive(false);
        owner.saveSettings();
        bool showCursor = PlayerCameraController.LocalListenerTransform == null ||
            SessionDisconnectUIComponent.IsOpen || isGameEnded() ||
            (LobbyRoomUI.Instance != null && LobbyRoomUI.Instance.BlocksPlayerInput);
        Cursor.lockState = showCursor ? CursorLockMode.None : CursorLockMode.Locked;
        Cursor.visible = showCursor;
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
    }

    private void setMasterVolume(float volume) //전체 음량 변경 전달
    {
        owner.setVolume(AudioVolumeChannel.Master, volume);
        masterValueText.text = formatVolume(volume);
    }

    private void setMusicVolume(float volume) //배경음 음량 변경 전달
    {
        owner.setVolume(AudioVolumeChannel.Music, volume);
        musicValueText.text = formatVolume(volume);
    }

    private void setEffectsVolume(float volume) //효과음 음량 변경 전달
    {
        owner.setVolume(AudioVolumeChannel.Effects, volume);
        effectsValueText.text = formatVolume(volume);
    }

    private void setVoiceVolume(float volume) //음성 수신 음량 변경 전달
    {
        owner.setVolume(AudioVolumeChannel.Voice, volume);
        voiceValueText.text = formatVolume(volume);
    }

    private void refreshValues() //저장값을 슬라이더와 숫자에 표시
    {
        Slider[] sliders = { masterSlider, musicSlider, effectsSlider, voiceSlider };
        TMP_Text[] labels = { masterValueText, musicValueText, effectsValueText, voiceValueText };
        for (int i = 0; i < sliders.Length; i++)
        {
            float volume = owner.getVolume((AudioVolumeChannel)i);
            sliders[i].SetValueWithoutNotify(volume);
            labels[i].text = formatVolume(volume);
        }
        refreshVoiceMode();
    }

    private void selectVoiceActivation() //자동 음성 감지 방식 선택
    {
        owner.setVoiceInputMode(VoiceInputMode.VoiceActivation);
        refreshVoiceMode();
    }

    private void selectPushToTalk() //T키를 누르는 동안 송신하는 방식 선택
    {
        owner.setVoiceInputMode(VoiceInputMode.PushToTalk);
        refreshVoiceMode();
    }

    private void refreshVoiceMode() //송신 방식의 선택 표시와 안내 갱신
    {
        bool pushToTalk = GameAudio.getVoiceInputMode() == VoiceInputMode.PushToTalk; //현재 선택한 송신 방식
        setModeButtonColor(voiceActivationButton, !pushToTalk);
        setModeButtonColor(pushToTalkButton, pushToTalk);
        voiceModeHintText.text = pushToTalk ? "T키를 누르는 동안 말합니다.  ·  V 음소거" : "목소리를 감지하면 자동으로 송신합니다.  ·  V 음소거";
    }

    private void setModeButtonColor(Button button, bool selected) //현재 선택한 방식의 버튼 강조
    {
        ColorBlock colors = button.colors; //기존 버튼의 입력 상태 색상
        colors.normalColor = selected ? new Color(0.2f, 0.42f, 0.65f) : new Color(0.18f, 0.21f, 0.27f);
        colors.selectedColor = colors.normalColor;
        button.colors = colors;
    }

    private string formatVolume(float volume) //음량 퍼센트 문자열
    {
        return Mathf.RoundToInt(volume * 100f) + "%";
    }

    private void OnDestroy() //UI 입력 구독 정리
    {
        openButton.onClick.RemoveListener(open);
        closeButton.onClick.RemoveListener(close);
        masterSlider.onValueChanged.RemoveListener(setMasterVolume);
        musicSlider.onValueChanged.RemoveListener(setMusicVolume);
        effectsSlider.onValueChanged.RemoveListener(setEffectsVolume);
        voiceSlider.onValueChanged.RemoveListener(setVoiceVolume);
        voiceActivationButton.onClick.RemoveListener(selectVoiceActivation);
        pushToTalkButton.onClick.RemoveListener(selectPushToTalk);
    }
}
