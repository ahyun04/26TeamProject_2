using TMPro;
using UnityEngine;

namespace LockdownProtocol.Lobby
{
    /// <summary>플레이어 목록 한 줄(닉네임 + Ready 상태 + 방장 왕관). LobbyRoomUI가 채워준다.</summary>
    public class PlayerListEntryUI : MonoBehaviour
    {
        [SerializeField] private TMP_Text nicknameText;
        [SerializeField] private TMP_Text readyStateText;
        [SerializeField] private GameObject crownIcon;

        public void Bind(string nickname, bool isReady, bool isHost)
        {
            if (nicknameText != null) nicknameText.text = nickname;
            if (readyStateText != null) readyStateText.text = isReady ? "READY" : "Not READY";
            if (crownIcon != null) crownIcon.SetActive(isHost);
        }
    }
}