using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace LockdownProtocol.Lobby.Invite
{
    public class InviteListPanel : MonoBehaviour
    {
        [SerializeField] private GameObject panelRoot;
        [SerializeField] private Transform listContainer;
        [SerializeField] private InviteListEntryUI entryPrefab;
        [SerializeField] private Button closeButton;

        private readonly List<InviteListEntryUI> _spawnedEntries = new List<InviteListEntryUI>();
        private bool _isOpen;

        internal bool IsOpen => _isOpen;

        private void OnEnable()
        {
            if (closeButton != null) closeButton.onClick.AddListener(Close);

            if (InviteClientManager.Instance != null)
            {
                InviteClientManager.Instance.OutgoingInviteStatusChanged += HandleOutgoingStatusChanged;
                InviteClientManager.Instance.InviteRequestFailed += HandleInviteRequestFailed;
            }

            SetOpen(false);
        }

        private void OnDisable()
        {
            if (closeButton != null) closeButton.onClick.RemoveListener(Close);

            if (InviteClientManager.Instance != null)
            {
                InviteClientManager.Instance.OutgoingInviteStatusChanged -= HandleOutgoingStatusChanged;
                InviteClientManager.Instance.InviteRequestFailed -= HandleInviteRequestFailed;
            }
        }

        private void Update()
        {
            if (!_isOpen) return;
            RefreshList();
        }

        public void Open()
        {
            SetOpen(true);
        }

        public void Close()
        {
            SetOpen(false);
        }

        private void SetOpen(bool open)
        {
            _isOpen = open;

            if (panelRoot == null)
            {
                Debug.LogWarning("[InviteListPanel] Panel Root가 할당되지 않았습니다.");
                return;
            }
            if (panelRoot == gameObject)
            {
                Debug.LogError("[InviteListPanel] Panel Root에 이 스크립트가 붙은 오브젝트 자신을 넣으면 안 됩니다 " +
                                "(꺼지면 이 스크립트도 같이 꺼짐). 별도 자식 오브젝트를 연결하세요.");
                return;
            }

            panelRoot.SetActive(open);
        }

        // ================== 목록 갱신 ==================

        private void RefreshList()
        {
            if (RoomManager.Instance == null) return;

            string myRoomId = RoomManager.Instance.RoomName.ToString();
            string myPlayerId = LocalPlayerIdentity.PlayerId;

            var candidates = new List<PlayerPresence>();
            foreach (var presence in FindObjectsByType<PlayerPresence>(FindObjectsSortMode.None))
            {
                if (presence.PlayerId == myPlayerId) continue;
                if (presence.IsInRoom && presence.CurrentRoomId == myRoomId) continue;

                candidates.Add(presence);
            }

            EnsureEntryCount(candidates.Count);

            for (int i = 0; i < candidates.Count; i++)
            {
                var presence = candidates[i];
                var state = DetermineState(presence);
                _spawnedEntries[i].gameObject.SetActive(true);
                _spawnedEntries[i].Bind(presence.PlayerId, presence.Nickname.ToString(), state, OnInviteClicked);
            }

            for (int i = candidates.Count; i < _spawnedEntries.Count; i++)
            {
                _spawnedEntries[i].gameObject.SetActive(false);
            }
        }

        private InviteListEntryState DetermineState(PlayerPresence presence)
        {
            if (presence.IsInGame) return InviteListEntryState.Unavailable;
            if (presence.IsInRoom) return InviteListEntryState.Unavailable;

            if (InviteClientManager.Instance != null && InviteClientManager.Instance.HasPendingOutgoingInvite(presence.PlayerId))
            {
                return InviteListEntryState.AlreadyInvited;
            }

            return InviteListEntryState.Invitable;
        }

        private void EnsureEntryCount(int count)
        {
            while (_spawnedEntries.Count < count)
            {
                var entry = Instantiate(entryPrefab, listContainer);
                _spawnedEntries.Add(entry);
            }
        }

        private void OnInviteClicked(string targetPlayerId)
        {
            InviteClientManager.Instance?.RequestInvite(targetPlayerId);
        }

        private void HandleOutgoingStatusChanged(string targetPlayerId, InviteState state, string message)
        {
            if (state != InviteState.Pending && !string.IsNullOrEmpty(message))
            {
                Debug.Log($"[InviteListPanel] {targetPlayerId} 초대 결과: {state} ({message})");
            }
        }

        private void HandleInviteRequestFailed(string message)
        {
            Debug.Log($"[InviteListPanel] 초대 실패: {message}");
        }
    }
}
