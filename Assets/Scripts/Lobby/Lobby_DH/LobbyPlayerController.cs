using Fusion;
using LockdownProtocol.Lobby.Invite;
using UnityEngine;

namespace LockdownProtocol.Lobby
{
    public class LobbyPlayerController : NetworkBehaviour
    {
        [Networked] public NetworkString<_16> Nickname { get; private set; }
        [Networked] public NetworkBool IsReady { get; private set; }
        [Networked] public NetworkBool IsHost { get; private set; }
        [Networked] public NetworkBool IsTalking { get; private set; } 
        [Networked] public int JoinOrder { get; private set; }

        public event System.Action<bool> ReadyChanged;

        public override void Spawned()
        {
            if (Object.HasInputAuthority)
            {
                // 로컬 플레이어 초기 닉네임 세팅
                RPC_SetNickname($"Player_{Object.InputAuthority.PlayerId}");

                string roomId = RoomManager.Instance != null ? RoomManager.Instance.RoomName.ToString() : null;
                GlobalLobbyInviteTransport.Instance?.UpdateLocalRoomStatus(true, roomId, isInGame: false);

                if (RoomManager.Instance != null)
                {
                    RoomManager.Instance.RoomStateChanged += HandleRoomStateChangedForPresence;
                }
            }

            if (RoomManager.Instance != null)
            {
                bool isHost = RoomManager.Instance.HostPlayerId == Object.InputAuthority;
                if (Object.HasStateAuthority)
                {
                    IsHost = isHost;
                }
            }
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            if (Object != null && Object.HasInputAuthority)
            {
                GlobalLobbyInviteTransport.Instance?.UpdateLocalRoomStatus(false, null, isInGame: false);

                if (RoomManager.Instance != null)
                {
                    RoomManager.Instance.RoomStateChanged -= HandleRoomStateChangedForPresence;
                }
            }
        }

        private void HandleRoomStateChangedForPresence(RoomManager.RoomState state)
        {
            if (!Object.HasInputAuthority) return;

            bool isInGame = state == RoomManager.RoomState.Playing;
            string roomId = RoomManager.Instance != null ? RoomManager.Instance.RoomName.ToString() : null;
            GlobalLobbyInviteTransport.Instance?.UpdateLocalRoomStatus(true, roomId, isInGame);
        }

        // ================== Ready ==================

        public void ToggleReady()
        {
            if (!Object.HasInputAuthority) return;
            RPC_SetReady(!IsReady);
        }

        [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
        private void RPC_SetReady(bool ready)
        {
            if (RoomManager.Instance != null &&
                RoomManager.Instance.CurrentRoomState != RoomManager.RoomState.Waiting)
            {
                return;
            }

            IsReady = ready;
            RPC_NotifyReadyChanged(ready);
        }

        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        private void RPC_NotifyReadyChanged(bool ready)
        {
            ReadyChanged?.Invoke(ready);
        }

        // ================== 닉네임 ==================

        [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
        private void RPC_SetNickname(string nickname)
        {
            Nickname = nickname;
        }

        // ================== 방장 지정 ==================

        public void SetHost(bool isHost)
        {
            if (!Object.HasStateAuthority) return;
            IsHost = isHost;

            if (isHost)
            {
                IsReady = false;
            }
        }

        public void SetJoinOrder(int order)
        {
            if (!Object.HasStateAuthority) return;
            JoinOrder = order;
        }
    }
}