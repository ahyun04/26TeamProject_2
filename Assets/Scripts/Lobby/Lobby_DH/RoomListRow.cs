using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace LockdownProtocol.Lobby
{
    /// <summary>방 목록의 한 줄. 방 이름, 인원, 방 컨디션(준비 중/게임 중), [입장] 버튼. 줄 프리팹 루트에 붙인다.</summary>
    public class RoomListRow : MonoBehaviour
    {
        [SerializeField] private TMP_Text nameText;
        [SerializeField] private TMP_Text countText;
        [SerializeField] private TMP_Text conditionText;   // 방 컨디션 ("준비 중" / "게임 중")
        [SerializeField] private Button joinButton;
        [SerializeField] private Color waitingColor = new Color(0.45f, 0.9f, 0.37f);   // 준비 중
        [SerializeField] private Color playingColor = new Color(0.95f, 0.45f, 0.4f);   // 게임 중

        private string sessionName;
        private Action<string> onJoin;

        public void Setup(string roomName, string countLabel, string conditionLabel, bool waiting, string session, Action<string> joinCallback)
        {
            sessionName = session;
            onJoin = joinCallback;
            if (nameText != null) nameText.text = roomName;
            if (countText != null) countText.text = countLabel;
            if (conditionText != null)
            {
                conditionText.text = conditionLabel;
                conditionText.color = waiting ? waitingColor : playingColor;
            }
            if (joinButton != null)
            {
                // 입장 버튼은 항상 눌러 볼 수 있다. 게임 중/가득 참 안내는 RoomListUI 가 누른 뒤에 띄운다.
                joinButton.interactable = true;
                joinButton.onClick.RemoveAllListeners();
                joinButton.onClick.AddListener(() => onJoin?.Invoke(sessionName));
            }
        }
    }
}