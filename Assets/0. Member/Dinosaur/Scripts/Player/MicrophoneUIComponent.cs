using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class MicrophoneUIComponent : MonoBehaviour
{
    [SerializeField] private Image stateIcon; //본인 마이크 상태 아이콘
    [SerializeField] private Sprite enabledIcon; //마이크 켜짐의 흰색 아이콘
    [SerializeField] private Sprite mutedIcon; //음소거의 빨간 사선 아이콘
    [SerializeField] private Sprite disconnectedIcon; //송신 불가의 회색 X 아이콘
    [SerializeField] private TMP_Text stateText; //현재 마이크 상태
    [SerializeField] private TMP_Text hintText; //송신 방식과 조작 키 안내
    [SerializeField] private Color enabledColor = Color.white; //마이크 켜짐 색상
    [SerializeField] private Color mutedColor = new Color(1f, 0.3f, 0.3f); //음소거 색상
    [SerializeField] private Color waitingColor = new Color(0.6f, 0.63f, 0.68f); //연결·키 입력 대기 색상

    internal void setState(bool muted, bool ready, bool pushToTalk, bool pressed, KeyCode muteKey, KeyCode talkKey) //본인의 송신 상태 표시
    {
        bool waitingForKey = ready && pushToTalk && !pressed; //눌러서 말하기 입력 대기
        Color color = muted ? mutedColor : !ready || waitingForKey ? waitingColor : enabledColor; //현재 표시 색상
        string label = muted ? "음소거" : !ready ? "송신 불가" : waitingForKey ? "말하기 대기" : "마이크 켜짐"; //현재 상태 문구
        string hint = pushToTalk ? $"{talkKey} 눌러 말하기 · {muteKey} 음소거" : $"음성 인식 · {muteKey} 음소거"; //현재 방식의 사용법
        if (stateText.text != label) stateText.text = label;
        if (stateText.color != color) stateText.color = color;
        Sprite icon = muted ? mutedIcon : !ready ? disconnectedIcon : enabledIcon; //기존 송신 판정에 맞는 표시
        Color iconColor = !muted && waitingForKey ? waitingColor : Color.white; //완성된 컬러 아이콘의 중복 색 곱 방지
        if (stateIcon.sprite != icon) stateIcon.sprite = icon;
        if (stateIcon.color != iconColor) stateIcon.color = iconColor;
        if (hintText.text != hint) hintText.text = hint;
    }
}
