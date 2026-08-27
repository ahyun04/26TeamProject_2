using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace LockdownProtocol.Lobby.Invite
{
    public class InviteReceivePopup : MonoBehaviour
    {
        [SerializeField] private GameObject panelRoot;
        [SerializeField] private TMP_Text inviterNameText;
        [SerializeField] private TMP_Text roomNameText;
        [SerializeField] private TMP_Text playerCountText;
        [SerializeField] private TMP_Text expireTimerText;
        [SerializeField] private Button acceptButton;
        [SerializeField] private Button declineButton;

        private readonly Queue<InviteData> _pendingQueue = new Queue<InviteData>();
        private InviteData _current;
        private bool _responseSent;

        private void OnEnable()
        {
            acceptButton.onClick.AddListener(OnAcceptClicked);
            declineButton.onClick.AddListener(OnDeclineClicked);

            if (InviteClientManager.Instance != null)
            {
                InviteClientManager.Instance.InviteReceived += HandleInviteReceived;
                InviteClientManager.Instance.MyResponseResult += HandleMyResponseResult;
            }

            SetOpen(false);
        }

        private void OnDisable()
        {
            acceptButton.onClick.RemoveListener(OnAcceptClicked);
            declineButton.onClick.RemoveListener(OnDeclineClicked);

            if (InviteClientManager.Instance != null)
            {
                InviteClientManager.Instance.InviteReceived -= HandleInviteReceived;
                InviteClientManager.Instance.MyResponseResult -= HandleMyResponseResult;
            }
        }

        private void Update()
        {
            if (_current == null) return;

            var remaining = _current.ExpireTime - DateTime.UtcNow;
            if (remaining <= TimeSpan.Zero)
            {
                if (expireTimerText != null) expireTimerText.text = "초대 만료까지 0분 0초";
                return;
            }

            if (expireTimerText != null)
            {
                expireTimerText.text = $"초대 만료까지 {remaining.Minutes}분 {remaining.Seconds}초";
            }
        }

        // ================== 수신 ==================

        private void HandleInviteReceived(InviteData invite)
        {
            _pendingQueue.Enqueue(invite);
            TryShowNext();
        }

        private void TryShowNext()
        {
            if (_current != null) return;
            if (_pendingQueue.Count == 0)
            {
                SetOpen(false);
                return;
            }

            _current = _pendingQueue.Dequeue();
            _responseSent = false;

            var invite = _current;
            if (inviterNameText != null) inviterNameText.text = invite.InviterNickname;
            if (roomNameText != null) roomNameText.text = invite.RoomName;
            if (playerCountText != null) playerCountText.text = $"{invite.CurrentPlayerCount} / {invite.MaxPlayerCount}";

            acceptButton.interactable = true;
            declineButton.interactable = true;

            SetOpen(true);
        }

        // ================== 버튼 ==================

        private void OnAcceptClicked()
        {
            if (_current == null || _responseSent) return;
            _responseSent = true;
            acceptButton.interactable = false;
            declineButton.interactable = false;

            InviteClientManager.Instance?.AcceptInvite(_current);
        }

        private void OnDeclineClicked()
        {
            if (_current == null || _responseSent) return;
            _responseSent = true;

            InviteClientManager.Instance?.DeclineInvite(_current);

            _current = null;
            TryShowNext();
        }

        // ================== 서버 응답 ==================

        private void HandleMyResponseResult(string inviteId, InviteState state, string message)
        {
            if (_current == null || _current.InviteId != inviteId) return;

            if (state != InviteState.Accepted && !string.IsNullOrEmpty(message))
            {
                Debug.Log($"[InviteReceivePopup] {message}");
            }

            _current = null;
            TryShowNext();
        }

        // ================== 패널 열기/닫기 ==================

        private void SetOpen(bool open)
        {
            if (panelRoot == null)
            {
                Debug.LogWarning("[InviteReceivePopup] Panel Root가 할당되지 않았습니다.");
                return;
            }
            if (panelRoot == gameObject)
            {
                Debug.LogError("[InviteReceivePopup] Panel Root에 이 스크립트가 붙은 오브젝트 자신을 넣으면 안 됩니다. " +
                                "별도 자식 오브젝트를 연결하세요.");
                return;
            }

            panelRoot.SetActive(open);
        }
    }
}