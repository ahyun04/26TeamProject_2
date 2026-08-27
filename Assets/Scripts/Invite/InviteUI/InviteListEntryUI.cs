using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace LockdownProtocol.Lobby.Invite
{
    public class InviteListEntryUI : MonoBehaviour
    {
        [SerializeField] private TMP_Text nicknameText;
        [SerializeField] private Button inviteButton;
        [SerializeField] private TMP_Text inviteButtonLabel;

        public string TargetPlayerId { get; private set; }

        private System.Action<string> _onInviteClicked;

        public void Bind(string playerId, string nickname, InviteListEntryState state, System.Action<string> onInviteClicked)
        {
            TargetPlayerId = playerId;
            _onInviteClicked = onInviteClicked;

            if (nicknameText != null) nicknameText.text = nickname;

            inviteButton.onClick.RemoveAllListeners();

            switch (state)
            {
                case InviteListEntryState.Invitable:
                    inviteButton.interactable = true;
                    if (inviteButtonLabel != null) inviteButtonLabel.text = "초대";
                    inviteButton.onClick.AddListener(() => _onInviteClicked?.Invoke(TargetPlayerId));
                    break;

                case InviteListEntryState.AlreadyInvited:
                    inviteButton.interactable = false;
                    if (inviteButtonLabel != null) inviteButtonLabel.text = "초대 대기 중";
                    break;

                case InviteListEntryState.Unavailable:
                    inviteButton.interactable = false;
                    if (inviteButtonLabel != null) inviteButtonLabel.text = "초대 불가";
                    break;
            }
        }
    }

    public enum InviteListEntryState
    {
        Invitable,
        AlreadyInvited,
        Unavailable
    }
}