using LockdownProtocol.Lobby;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Events;
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
    [SerializeField] private Button soundTabButton; //사운드 탭 선택
    [SerializeField] private Button controlsTabButton; //조작키 탭 선택
    [SerializeField] private Sprite selectedTabSprite; //선택한 아이콘 탭의 진한 배경
    [SerializeField] private Sprite neutralTabSprite; //선택하지 않은 아이콘 탭의 배경
    [SerializeField] private GameObject soundPage; //음량과 송신 방식 페이지
    [SerializeField] private GameObject controlsPage; //현재 조작키 안내 페이지
    [SerializeField] private Button customizationTabButton; //캐릭터 색상 탭 선택
    [SerializeField] private GameObject customizationPage; //색상 팔레트와 모델 미리보기 페이지
    [SerializeField] private Button applyButton; //설정 적용과 저장
    [SerializeField] private Button cancelButton; //미적용 설정 취소
    [SerializeField] private Button defaultsButton; //임시 설정 기본값 복원
    [SerializeField] private TMP_Text saveHintText; //저장 방법과 저장 실패 안내
    [SerializeField] private TMP_InputField[] volumeInputs; //음량의 정수 입력 칸
    [SerializeField] private Button[] decreaseButtons; //음량 1퍼센트 감소
    [SerializeField] private Button[] increaseButtons; //음량 1퍼센트 증가
    [SerializeField] private Sprite selectedButtonSprite; //선택한 송신 방식의 버튼 리소스
    [SerializeField] private Sprite neutralButtonSprite; //일반 송신 방식의 버튼 리소스
    [SerializeField] private EventSystem fallbackEventSystem; //씬에 UI 입력 시스템이 없을 때 사용

    private GameAudio owner; //설정 기능 진입점
    private GameEndSystem gameEndSystem; //결과 화면 우선 표시
    private string sceneName; //현재 게임 씬
    private bool isOpen; //현재 설정창 상태
    private int toggleFrame = -1; //Esc로 닫은 프레임의 입력 재사용 방지
    private bool soundSelected = true; //현재 열린 설정 탭
    private bool customizationSelected; //현재 커스터마이징 탭 선택 여부
    private UnityAction<string>[] volumeInputActions; //숫자 입력 구독 해제에 사용할 함수
    private UnityAction[] decreaseActions; //감소 버튼 구독 해제에 사용할 함수
    private UnityAction[] increaseActions; //증가 버튼 구독 해제에 사용할 함수
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
        soundTabButton?.onClick.AddListener(showSoundTab);
        controlsTabButton?.onClick.AddListener(showControlsTab);
        customizationTabButton?.onClick.AddListener(showCustomizationTab);
        applyButton?.onClick.AddListener(apply);
        cancelButton?.onClick.AddListener(close);
        defaultsButton?.onClick.AddListener(restoreDefaults);
        connectVolumeInputs();
        refreshTab();
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
        if (isOpen || owner == null || SessionDisconnectUIComponent.IsOpen || isGameEnded()) return;
        owner.beginSettingsEditing();
        PlayerAppearance.beginCustomization();
        isOpen = true;
        toggleFrame = Time.frameCount;
        refreshValues();
        refreshTab();
        if (saveHintText != null) saveHintText.text = "적용을 누르면 변경 사항이 저장됩니다.";
        panel.SetActive(true);
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    private void close() //X·Esc·취소·씬 전환에서 미적용 설정 복원
    {
        if (!isOpen) return;
        finishVolumeInput();
        owner.cancelSettings();
        PlayerAppearance.cancelCustomization();
        finishClose();
    }

    private void apply() //입력 중인 숫자도 반영한 뒤 설정 저장
    {
        if (!isOpen) return;
        finishVolumeInput();
        if (PlayerAppearance.prepareSave() && owner.applySettings())
        {
            PlayerAppearance.completeSave();
            finishClose();
        }
        else
        {
            PlayerAppearance.rollbackSave();
            if (saveHintText != null) saveHintText.text = "저장하지 못했습니다. 다시 적용해 주세요.";
        }
    }

    private void restoreDefaults() //기본값은 적용 전까지 미리 듣기에만 반영
    {
        if (!isOpen) return;
        finishVolumeInput();
        owner.restoreDefaultSettings();
        PlayerAppearance.restoreDefaults();
        refreshValues();
    }

    private void finishClose() //입력 차단 해제와 현재 화면의 커서 복원
    {
        isOpen = false;
        toggleFrame = Time.frameCount;
        panel.SetActive(false);
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
        refreshVolumeRow(AudioVolumeChannel.Master);
    }

    private void setMusicVolume(float volume) //배경음 음량 변경 전달
    {
        owner.setVolume(AudioVolumeChannel.Music, volume);
        refreshVolumeRow(AudioVolumeChannel.Music);
    }

    private void setEffectsVolume(float volume) //효과음 음량 변경 전달
    {
        owner.setVolume(AudioVolumeChannel.Effects, volume);
        refreshVolumeRow(AudioVolumeChannel.Effects);
    }

    private void setVoiceVolume(float volume) //음성 수신 음량 변경 전달
    {
        owner.setVolume(AudioVolumeChannel.Voice, volume);
        refreshVolumeRow(AudioVolumeChannel.Voice);
    }

    private void refreshValues() //저장값을 슬라이더와 숫자에 표시
    {
        for (int i = 0; i < 4; i++) refreshVolumeRow((AudioVolumeChannel)i);
        refreshVoiceMode();
    }

    private void refreshVolumeRow(AudioVolumeChannel channel) //슬라이더·숫자·증감 버튼을 같은 값으로 표시
    {
        int index = (int)channel; //표시할 음량 행
        float volume = owner.getVolume(channel); //미리 듣는 음량
        Slider[] sliders = { masterSlider, musicSlider, effectsSlider, voiceSlider }; //기존 음량 슬라이더
        TMP_Text[] labels = { masterValueText, musicValueText, effectsValueText, voiceValueText }; //기존 값 표시 참조
        sliders[index].SetValueWithoutNotify(volume);
        if (volumeInputs != null && volumeInputs.Length > index && volumeInputs[index] != null)
            volumeInputs[index].SetTextWithoutNotify(Mathf.RoundToInt(volume * 100f).ToString());
        else labels[index].text = formatVolume(volume);
        if (decreaseButtons != null && decreaseButtons.Length > index && decreaseButtons[index] != null)
            decreaseButtons[index].interactable = volume > 0f;
        if (increaseButtons != null && increaseButtons.Length > index && increaseButtons[index] != null)
            increaseButtons[index].interactable = volume < 1f;
    }

    private void connectVolumeInputs() //숫자 입력과 증감 버튼 연결
    {
        if (volumeInputs == null || decreaseButtons == null || increaseButtons == null) return;
        volumeInputActions = new UnityAction<string>[4];
        decreaseActions = new UnityAction[4];
        increaseActions = new UnityAction[4];
        for (int i = 0; i < 4; i++)
        {
            int index = i; //행별 입력 대상
            volumeInputActions[i] = value => setVolumeInput((AudioVolumeChannel)index, value);
            decreaseActions[i] = () => adjustVolume((AudioVolumeChannel)index, -0.01f);
            increaseActions[i] = () => adjustVolume((AudioVolumeChannel)index, 0.01f);
            volumeInputs[i].onEndEdit.AddListener(volumeInputActions[i]);
            decreaseButtons[i].onClick.AddListener(decreaseActions[i]);
            increaseButtons[i].onClick.AddListener(increaseActions[i]);
        }
    }

    private void setVolumeInput(AudioVolumeChannel channel, string value) //숫자 입력을 0~100 범위로 적용
    {
        if (!isOpen || owner == null) return;
        if (int.TryParse(value, out int percent)) owner.setVolume(channel, Mathf.Clamp(percent, 0, 100) / 100f);
        refreshVolumeRow(channel);
    }

    private void adjustVolume(AudioVolumeChannel channel, float amount) //음량을 1퍼센트 단위로 증감
    {
        if (!isOpen) return;
        finishVolumeInput();
        owner.setVolume(channel, owner.getVolume(channel) + amount);
        refreshVolumeRow(channel);
    }

    private void finishVolumeInput() //탭 전환이나 버튼 처리 전에 편집 중인 숫자 확정
    {
        if (volumeInputs == null) return;
        for (int i = 0; i < volumeInputs.Length; i++)
            if (volumeInputs[i].isFocused)
            {
                setVolumeInput((AudioVolumeChannel)i, volumeInputs[i].text);
                volumeInputs[i].DeactivateInputField();
            }
    }

    private void showSoundTab() //임시 설정을 유지하면서 사운드 탭 선택
    {
        finishVolumeInput();
        soundSelected = true;
        customizationSelected = false;
        refreshTab();
    }

    private void showControlsTab() //현재 키 안내 탭 선택
    {
        finishVolumeInput();
        soundSelected = false;
        customizationSelected = false;
        refreshTab();
    }

    private void showCustomizationTab() //기존 임시 설정을 유지하면서 색상 탭 선택
    {
        finishVolumeInput();
        customizationSelected = true;
        refreshTab();
    }

    private void refreshTab() //현재 페이지와 아이콘 탭의 선택 배경 표시
    {
        if (soundPage != null) soundPage.SetActive(!customizationSelected && soundSelected);
        if (controlsPage != null) controlsPage.SetActive(!customizationSelected && !soundSelected);
        if (customizationPage != null) customizationPage.SetActive(customizationSelected);
        if (selectedTabSprite == null || neutralTabSprite == null) return;
        if (soundTabButton != null) soundTabButton.image.sprite = !customizationSelected && soundSelected ? selectedTabSprite : neutralTabSprite;
        if (controlsTabButton != null) controlsTabButton.image.sprite = !customizationSelected && !soundSelected ? selectedTabSprite : neutralTabSprite;
        if (customizationTabButton != null) customizationTabButton.image.sprite = customizationSelected ? selectedTabSprite : neutralTabSprite;
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
        if (selectedButtonSprite != null && neutralButtonSprite != null)
        {
            button.image.sprite = selected ? selectedButtonSprite : neutralButtonSprite;
            colors.normalColor = Color.white;
        }
        else colors.normalColor = selected ? new Color(0.2f, 0.42f, 0.65f) : new Color(0.18f, 0.21f, 0.27f);
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
        soundTabButton?.onClick.RemoveListener(showSoundTab);
        controlsTabButton?.onClick.RemoveListener(showControlsTab);
        customizationTabButton?.onClick.RemoveListener(showCustomizationTab);
        applyButton?.onClick.RemoveListener(apply);
        cancelButton?.onClick.RemoveListener(close);
        defaultsButton?.onClick.RemoveListener(restoreDefaults);
        if (volumeInputActions == null) return;
        for (int i = 0; i < 4; i++)
        {
            volumeInputs[i].onEndEdit.RemoveListener(volumeInputActions[i]);
            decreaseButtons[i].onClick.RemoveListener(decreaseActions[i]);
            increaseButtons[i].onClick.RemoveListener(increaseActions[i]);
        }
    }
}
