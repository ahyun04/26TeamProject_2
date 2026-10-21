using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace LockdownProtocol.Lobby
{
    /// <summary>플레이어 목록 한 줄(닉네임 + Ready 상태 + 방장 왕관). LobbyRoomUI가 채워준다.</summary>
    public class PlayerListEntryUI : MonoBehaviour
    {
        [SerializeField] private TMP_Text nicknameText;
        [SerializeField] private TMP_Text readyStateText;
        [SerializeField] private GameObject crownIcon;
        [SerializeField] private GameObject localIndicator; //설정창에서 본인 표시
        [SerializeField] private Image avatarImage; //설정창 아바타의 캐릭터 색상
        [SerializeField] private Image avatarBackground; //아바타 바탕의 선택 색상
        [SerializeField] private Image readyBackground; //준비 상태 배경
        [SerializeField] private bool useKoreanLabels; //설정창 행의 한글 표시

        public void Bind(string nickname, bool isReady, bool isHost)
        {
            if (nicknameText != null) nicknameText.text = nickname;
            if (readyStateText != null) readyStateText.text = useKoreanLabels
                ? (isReady ? "준비 완료" : "준비 안 됨") : (isReady ? "READY" : "Not READY");
            if (crownIcon != null) crownIcon.SetActive(isHost);
        }

        internal void bindForSettings(string nickname, bool isReady, bool isHost, bool isLocal, Color color, bool showReady = true) //기존 표시에 본인·색상·준비 배경 연결
        {
            Bind(nickname, isReady, isHost);
            if (readyStateText != null) readyStateText.gameObject.SetActive(showReady);
            if (readyBackground != null) readyBackground.gameObject.SetActive(showReady);
            if (localIndicator != null) localIndicator.SetActive(isLocal);
            if (avatarImage != null) avatarImage.color = color;
            if (avatarBackground != null) avatarBackground.color = new Color(color.r * 0.32f, color.g * 0.32f, color.b * 0.32f, 1f);
            if (readyStateText != null) readyStateText.color = isReady ? new Color(0.45f, 0.9f, 0.37f) : new Color(0.73f, 0.79f, 0.85f);
            if (readyBackground != null) readyBackground.color = isReady ? new Color(0.18f, 0.63f, 0.22f) : new Color(0.55f, 0.67f, 0.82f);
        }
    }
}
