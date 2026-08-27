using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Fusion;
using UnityEngine;

namespace LockdownProtocol.Lobby.Invite
{
    public class GlobalLobbyInviteTransport : MonoBehaviour, IInviteTransport
    {
        [Header("Session Settings")]
        [SerializeField] private string globalLobbySessionName = "LockdownProtocol_GlobalLobby";
        [SerializeField] private NetworkObject playerPresencePrefab;
        [SerializeField] private float presenceSearchTimeoutSeconds = 3f;

        public static GlobalLobbyInviteTransport Instance { get; private set; }

        public event Action<string, bool> InviteResponseReceived;

        private NetworkRunner _runner;
        private PlayerPresence _localPresence;

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

        private async void Start()
        {
            await ConnectToGlobalLobby();
        }

        private async Task ConnectToGlobalLobby()
        {
            _runner = gameObject.AddComponent<NetworkRunner>();
            _runner.ProvideInput = false; 

            var sceneManager = gameObject.AddComponent<NetworkSceneManagerDefault>();

            var result = await _runner.StartGame(new StartGameArgs
            {
                GameMode = GameMode.Shared,
                SessionName = globalLobbySessionName,
                SceneManager = sceneManager
            });

            if (!result.Ok)
            {
                Debug.LogError($"[GlobalLobbyInviteTransport] 전역 로비 접속 실패: {result.ShutdownReason}");
                return;
            }

            if (playerPresencePrefab == null)
            {
                Debug.LogError("[GlobalLobbyInviteTransport] Player Presence Prefab이 할당되지 않았습니다.");
                return;
            }

            var spawned = _runner.Spawn(playerPresencePrefab, inputAuthority: _runner.LocalPlayer);
            _localPresence = spawned.GetComponent<PlayerPresence>();
            _localPresence.InviteReceivedLocally += invite => { /* InviteClientManager가 이후 여기에 연결 */ };
            _localPresence.InviteResponseReceivedLocally += (inviteId, accepted) => InviteResponseReceived?.Invoke(inviteId, accepted);

            Debug.Log("[GlobalLobbyInviteTransport] 전역 로비 접속 완료");
        }

        public void UpdateLocalRoomStatus(bool isInRoom, string roomId, bool isInGame)
        {
            _localPresence?.SetRoomStatus(isInRoom, roomId, isInGame);
        }

        // ================== IInviteTransport 구현 ==================

        public async Task<PlayerPresenceSnapshot> QueryPresence(string playerId)
        {
            var presence = await FindPresenceByPlayerId(playerId);
            if (presence == null) return PlayerPresenceSnapshot.NotFound;

            return new PlayerPresenceSnapshot
            {
                Exists = true,
                IsOnline = true,
                IsInAnyRoom = presence.IsInRoom,
                IsInGame = presence.IsInGame
            };
        }

        public void DeliverInvite(InviteData invite)
        {
            FindPresenceByPlayerId(invite.TargetPlayerId).ContinueWith(task =>
            {
                var presence = task.Result;
                if (presence == null)
                {
                    Debug.LogWarning($"[GlobalLobbyInviteTransport] 초대 대상({invite.TargetPlayerId})을 전역 로비에서 찾지 못함");
                    return;
                }

                double expireUnixSeconds = ((DateTimeOffset)invite.ExpireTime).ToUnixTimeMilliseconds() / 1000.0;

                presence.RPC_ReceiveInvite(
                    invite.InviteId, invite.InviterId, invite.InviterNickname, invite.RoomHostPlayerId,
                    invite.RoomId, invite.RoomName, invite.CurrentPlayerCount, invite.MaxPlayerCount,
                    expireUnixSeconds);
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        public void NotifyResult(string playerId, string inviteId, InviteState finalState, string message, string approvedRoomId = null)
        {
            FindPresenceByPlayerId(playerId).ContinueWith(task =>
            {
                var presence = task.Result;
                if (presence == null) return;

                presence.RPC_ReceiveResult(inviteId, finalState, message ?? string.Empty, approvedRoomId ?? string.Empty);
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        public void SendInviteResponse(string roomHostPlayerId, string inviteId, bool accepted)
        {
            FindPresenceByPlayerId(roomHostPlayerId).ContinueWith(task =>
            {
                var presence = task.Result;
                if (presence == null)
                {
                    Debug.LogWarning("[GlobalLobbyInviteTransport] 방 호스트를 전역 로비에서 찾지 못해 응답을 보낼 수 없음");
                    return;
                }

                presence.RPC_ReceiveResponse(inviteId, accepted);
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }


        private async Task<PlayerPresence> FindPresenceByPlayerId(string playerId)
        {
            float elapsed = 0f;
            const float retryInterval = 0.2f;

            while (elapsed < presenceSearchTimeoutSeconds)
            {
                foreach (var presence in FindObjectsByType<PlayerPresence>(FindObjectsSortMode.None))
                {
                    if (presence.PlayerId == playerId) return presence;
                }

                await Task.Delay(TimeSpan.FromSeconds(retryInterval));
                elapsed += retryInterval;
            }

            return null;
        }
    }
}