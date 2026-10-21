using Fusion;
using LockdownProtocol.Lobby.Invite;
using UnityEngine;

namespace LockdownProtocol.Lobby
{
    public class LobbyPlayerController : NetworkBehaviour
    {
        private const int MaxNicknameLength = 12;

        [Networked, OnChangedRender(nameof(retainLobbyIdentity))] public NetworkString<_16> Nickname { get; private set; }
        [Networked] public NetworkBool IsReady { get; private set; }
        [Networked] public NetworkBool IsHost { get; private set; }
        [Networked] public NetworkBool IsTalking { get; private set; }
        [Networked, OnChangedRender(nameof(retainLobbyIdentity))] public int JoinOrder { get; private set; }

        private NetworkBootstrap bootstrap; //씬 전환 후에도 참가자 표시 정보를 유지할 진입점

        public event System.Action<bool> ReadyChanged;

        public override void Spawned()
        {
            bootstrap = FindFirstObjectByType<NetworkBootstrap>();
            if (Object.HasInputAuthority)
            {
                // Main에서 선택한 닉네임을 우선하고 저장값이 없으면 보존한 이름 사용
                string fallback = bootstrap != null ? bootstrap.getLobbyIdentity(Object.InputAuthority).nickname
                    : $"Player_{Object.InputAuthority.PlayerId}";
                string nickname = NicknamePopupUI.HasSavedNickname() ? NicknamePopupUI.GetSavedNickname() : fallback;
                RPC_SetNickname(string.IsNullOrWhiteSpace(nickname) ? fallback : nickname);

                string roomId = RoomManager.Instance != null ? RoomManager.Instance.RoomName.ToString() : null;
                bool isInGame = RoomManager.Instance != null && RoomManager.Instance.CurrentRoomState == RoomManager.RoomState.Playing;
                GlobalLobbyInviteTransport.Instance?.UpdateLocalRoomStatus(true, roomId, isInGame);

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
            retainLobbyIdentity();
        }

        private void retainLobbyIdentity() //초기 생성과 복제 값 변경 시 이름과 입장 순서 보관
        {
            if (bootstrap != null) bootstrap.retainLobbyIdentity(this);
        }

        public override void Render() //본인 화면에서 확정 방 정보를 씬 제거 전에 보관
        {
            if (Object.HasInputAuthority && bootstrap != null) bootstrap.retainRoomSettings(RoomManager.Instance);
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            if (Object != null && Object.HasInputAuthority)
            {
                bool isInGame = RoomManager.Instance != null && RoomManager.Instance.CurrentRoomState == RoomManager.RoomState.Playing;
                GlobalLobbyInviteTransport.Instance?.UpdateLocalRoomStatus(isInGame,
                    isInGame ? RoomManager.Instance.RoomName.ToString() : null, isInGame);

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
            // 호스트 쪽에서 한 번 더 정리: 태그 문자 제거, 공백 제거, 길이 제한
            string cleaned = (nickname ?? string.Empty).Replace("<", string.Empty).Replace(">", string.Empty).Trim();
            if (cleaned.Length > MaxNicknameLength) cleaned = cleaned.Substring(0, MaxNicknameLength);
            if (cleaned.Length == 0) cleaned = $"Player_{Object.InputAuthority.PlayerId}";

            Nickname = cleaned;
            retainLobbyIdentity();
        }

        // ================== 방장 지정 ==================

        public void SetHost(bool isHost)
        {
            if (!Object.HasStateAuthority) return;
            if (IsHost == isHost) return;
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
            retainLobbyIdentity();
        }
    }
}