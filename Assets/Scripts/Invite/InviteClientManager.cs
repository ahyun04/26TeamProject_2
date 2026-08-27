using System.Collections.Generic;
using LockdownProtocol.Lobby;
using LockdownProtocol.Networking;
using UnityEngine;

namespace LockdownProtocol.Lobby.Invite
{
    public class InviteClientManager : MonoBehaviour
    {
        public static InviteClientManager Instance { get; private set; }

        // ================== UI가 구독하는 이벤트 ==================

        public event System.Action<InviteData> InviteReceived;

        public event System.Action<string /*targetPlayerId*/, InviteState, string /*message*/> OutgoingInviteStatusChanged;

        public event System.Action<string /*message*/> InviteRequestFailed;

        public event System.Action<string /*inviteId*/, InviteState, string /*message*/> MyResponseResult;

        private readonly Dictionary<string, string> _outgoingInvites = new Dictionary<string, string>();

        private readonly Dictionary<string, InviteData> _incomingInvites = new Dictionary<string, InviteData>();

        private InviteManager _currentRoomInviteManager;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void OnEnable()
        {
            TryHookGlobalLobby();
        }

        private void Update()
        {
            var roomInviteManager = RoomManager.Instance != null
                ? RoomManager.Instance.GetComponent<InviteManager>()
                : null;

            if (roomInviteManager != _currentRoomInviteManager)
            {
                UnhookRoomInviteManager();
                _currentRoomInviteManager = roomInviteManager;
                HookRoomInviteManager();
            }

            if (PlayerPresence.Local != null)
            {
                TryHookGlobalLobby();
            }
        }

        private void OnDestroy()
        {
            UnhookRoomInviteManager();
            UnhookGlobalLobby();
        }

        // ================== 채널 1: 내가 있는 방의 InviteManager ==================

        private void HookRoomInviteManager()
        {
            if (_currentRoomInviteManager == null) return;

            _currentRoomInviteManager.InviteSent += HandleInviteSent;
            _currentRoomInviteManager.InviteRequestFailed += HandleInviteRequestFailed;
            _currentRoomInviteManager.InviteResultForInviter += HandleInviteResultForInviter;
        }

        private void UnhookRoomInviteManager()
        {
            if (_currentRoomInviteManager == null) return;

            _currentRoomInviteManager.InviteSent -= HandleInviteSent;
            _currentRoomInviteManager.InviteRequestFailed -= HandleInviteRequestFailed;
            _currentRoomInviteManager.InviteResultForInviter -= HandleInviteResultForInviter;
        }

        private void HandleInviteSent(string targetPlayerId, string inviteId)
        {
            _outgoingInvites[targetPlayerId] = inviteId;
            OutgoingInviteStatusChanged?.Invoke(targetPlayerId, InviteState.Pending, null);
        }

        private void HandleInviteRequestFailed(string message)
        {
            InviteRequestFailed?.Invoke(message);
        }

        private void HandleInviteResultForInviter(string targetPlayerId, InviteState state, string message)
        {
            _outgoingInvites.Remove(targetPlayerId);
            OutgoingInviteStatusChanged?.Invoke(targetPlayerId, state, message);
        }

        // ================== 채널 2: 전역 로비의 내 PlayerPresence ==================

        private bool _globalLobbyHooked;

        private void TryHookGlobalLobby()
        {
            if (_globalLobbyHooked) return;
            if (PlayerPresence.Local == null) return;

            PlayerPresence.Local.InviteReceivedLocally += HandleInviteReceived;
            PlayerPresence.Local.InviteResultReceivedLocally += HandleMyInviteResult;
            _globalLobbyHooked = true;
        }

        private void UnhookGlobalLobby()
        {
            if (!_globalLobbyHooked) return;
            if (PlayerPresence.Local != null)
            {
                PlayerPresence.Local.InviteReceivedLocally -= HandleInviteReceived;
                PlayerPresence.Local.InviteResultReceivedLocally -= HandleMyInviteResult;
            }
            _globalLobbyHooked = false;
        }

        private void HandleInviteReceived(InviteData invite)
        {
            _incomingInvites[invite.InviteId] = invite;
            InviteReceived?.Invoke(invite);
        }

        private void HandleMyInviteResult(string inviteId, InviteState state, string message, string approvedRoomId)
        {
            _incomingInvites.Remove(inviteId);

            if (state == InviteState.Accepted && !string.IsNullOrEmpty(approvedRoomId))
            {
                // ---- InviteSceneHandler 역할: 승인된 방으로 실제 참가 ----
                var bootstrap = FindFirstObjectByType<NetworkBootstrap>();
                if (bootstrap != null)
                {
                    _ = bootstrap.JoinRoom(approvedRoomId);
                }
                else
                {
                    Debug.LogError("[InviteClientManager] NetworkBootstrap을 찾을 수 없어 승인된 방에 참가하지 못했습니다.");
                }
            }

            MyResponseResult?.Invoke(inviteId, state, message);
        }

        // ================== 외부(UI)에서 호출하는 API ==================

        public void RequestInvite(string targetPlayerId)
        {
            if (_currentRoomInviteManager == null)
            {
                InviteRequestFailed?.Invoke("초대할 수 없습니다");
                return;
            }

            if (_outgoingInvites.ContainsKey(targetPlayerId)) return;

            _outgoingInvites[targetPlayerId] = string.Empty;

            string myId = LocalPlayerIdentity.PlayerId;
            string myNickname = PlayerPrefs.GetString("Nickname", "Player");

            _currentRoomInviteManager.RPC_RequestInvite(myId, myNickname, targetPlayerId);
        }

        public void CancelInvite(string targetPlayerId)
        {
            if (_currentRoomInviteManager == null) return;
            if (!_outgoingInvites.TryGetValue(targetPlayerId, out var inviteId)) return;
            if (string.IsNullOrEmpty(inviteId))
            {
                return;
            }

            _outgoingInvites.Remove(targetPlayerId);
            _currentRoomInviteManager.RPC_CancelInvite(inviteId);
        }

        public void AcceptInvite(InviteData invite)
        {
            GlobalLobbyInviteTransport.Instance?.SendInviteResponse(invite.RoomHostPlayerId, invite.InviteId, true);
        }

        public void DeclineInvite(InviteData invite)
        {
            GlobalLobbyInviteTransport.Instance?.SendInviteResponse(invite.RoomHostPlayerId, invite.InviteId, false);
            _incomingInvites.Remove(invite.InviteId);
        }

        public IReadOnlyDictionary<string, InviteData> IncomingInvites => _incomingInvites;

        public bool HasPendingOutgoingInvite(string targetPlayerId) => _outgoingInvites.ContainsKey(targetPlayerId);
    }
}