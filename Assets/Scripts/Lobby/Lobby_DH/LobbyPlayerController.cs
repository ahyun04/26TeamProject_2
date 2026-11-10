using Fusion;
using LockdownProtocol.Lobby.Invite;
using UnityEngine;

namespace LockdownProtocol.Lobby
{
    public class LobbyPlayerController : NetworkBehaviour
    {
        private const int MaxNicknameLength = 6;

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
                // 로컬 플레이어 초기 닉네임 세팅: Main 씬에서 입력한 닉네임, 없으면 Player_{번호}
                string fallback = $"P{Object.InputAuthority.PlayerId}";
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
            if (cleaned.Length == 0) cleaned = $"P{Object.InputAuthority.PlayerId}";

            Nickname = makeUniqueNickname(cleaned);
        }

        // 같은 방의 다른 참가자와 겹치면 "이름(2)", "이름(3)" ... 으로 바꾼다 (대소문자 구분 없이 비교, 호스트에서만 실행)
        private string makeUniqueNickname(string desired)
        {
            string candidate = desired;
            for (int n = 2; n < 100 && isNicknameTaken(candidate); n++)
            {
                string suffix = $"({n})";
                candidate = desired + suffix; // 최대 6자 + 4자 = 10자 이내 (NetworkString<_16> 안에 들어감)
            }
            return candidate;
        }

        private bool isNicknameTaken(string candidate)
        {
            foreach (LobbyPlayerController other in FindObjectsByType<LobbyPlayerController>(FindObjectsSortMode.None))
            {
                if (other == this || other.Object == null || !other.Object.IsValid) continue;
                if (string.Equals(other.Nickname.ToString(), candidate, System.StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
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
        }
    }
}