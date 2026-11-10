using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace LockdownProtocol.Lobby
{
    /// <summary>방 목록의 한 줄. 방 이름, 인원, [입장] 버튼. 줄 프리팹 루트에 붙인다.</summary>
    public class RoomListRow : MonoBehaviour
    {
        [SerializeField] private TMP_Text nameText;
        [SerializeField] private TMP_Text countText;
        [SerializeField] private Button joinButton;

        private string sessionName;
        private Action<string> onJoin;

        public void Setup(string roomName, string countLabel, bool joinable, string session, Action<string> joinCallback)
        {
            sessionName = session;
            onJoin = joinCallback;
            if (nameText != null) nameText.text = roomName;
            if (countText != null) countText.text = countLabel;
            if (joinButton != null)
            {
                joinButton.interactable = joinable;
                joinButton.onClick.RemoveAllListeners();
                joinButton.onClick.AddListener(() => onJoin?.Invoke(sessionName));
            }
        }
    }
}