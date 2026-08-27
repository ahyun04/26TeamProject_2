using Fusion;
using UnityEngine;

namespace LockdownProtocol.Lobby.Invite
{
    public class PlayerPresence : NetworkBehaviour
    {
        [Networked, Capacity(32)] public string PlayerId { get; private set; }
        [Networked, Capacity(16)] public string Nickname { get; private set; }
        [Networked] public NetworkBool IsInRoom { get; private set; }
        [Networked, Capacity(32)] public string CurrentRoomId { get; private set; }
        [Networked] public NetworkBool IsInGame { get; private set; }

        public static PlayerPresence Local { get; private set; }

        public override void Spawned()
        {
            if (Object.HasStateAuthority)
            {
                Local = this;
                SetIdentity(LocalPlayerIdentity.PlayerId, PlayerPrefs.GetString("Nickname", "Player"));
            }
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            if (Local == this) Local = null;
        }

        public void SetIdentity(string playerId, string nickname)
        {
            if (!Object.HasStateAuthority) return;
            PlayerId = playerId;
            Nickname = nickname;
        }

        /// 방 상태 바뀔 때마다 호출해서 자기 재실 상태를 갱신
        public void SetRoomStatus(bool isInRoom, string roomId, bool isInGame)
        {
            if (!Object.HasStateAuthority) return;
            IsInRoom = isInRoom;
            CurrentRoomId = roomId ?? string.Empty;
            IsInGame = isInGame;
        }

        // ================== 다른 사람이 나에게 보내는 메시지 ==================

        public event System.Action<InviteData> InviteReceivedLocally;
        public event System.Action<string /*inviteId*/, InviteState, string /*message*/, string /*approvedRoomId*/> InviteResultReceivedLocally;
        public event System.Action<string /*inviteId*/, bool /*accepted*/> InviteResponseReceivedLocally;

        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        public void RPC_ReceiveInvite(string inviteId, string inviterId, string inviterNickname, string roomHostPlayerId,
            string roomId, string roomName, int currentPlayerCount, int maxPlayerCount, double expireUnixSeconds)
        {
            var invite = new InviteData
            {
                InviteId = inviteId,
                InviterId = inviterId,
                InviterNickname = inviterNickname,
                RoomHostPlayerId = roomHostPlayerId,
                TargetPlayerId = PlayerId,
                RoomId = roomId,
                RoomName = roomName,
                CurrentPlayerCount = currentPlayerCount,
                MaxPlayerCount = maxPlayerCount,
                State = InviteState.Pending,
                CreateTime = System.DateTime.UtcNow,
                ExpireTime = System.DateTimeOffset.FromUnixTimeMilliseconds((long)(expireUnixSeconds * 1000)).UtcDateTime
            };

            InviteReceivedLocally?.Invoke(invite);
        }

        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        public void RPC_ReceiveResult(string inviteId, InviteState state, string message, string approvedRoomId)
        {
            InviteResultReceivedLocally?.Invoke(inviteId, state, message, approvedRoomId ?? string.Empty);
        }

        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        public void RPC_ReceiveResponse(string inviteId, NetworkBool accepted)
        {
            InviteResponseReceivedLocally?.Invoke(inviteId, accepted);
        }
    }
}