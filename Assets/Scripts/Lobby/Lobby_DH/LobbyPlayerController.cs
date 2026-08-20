using Fusion;
using UnityEngine;

namespace LockdownProtocol.Lobby
{
    /// <summary>
    /// 방에 참가한 플레이어 한 명을 표현하는 네트워크 오브젝트.
    /// 닉네임, Ready 상태, 방장 여부, 입장 순서(방장 위임 판단용)를 들고 있다.
    /// 실제 3D 캐릭터 이동/스폰은 LobbyMovementController(별도) 쪽에서 다룬다는 전제.
    /// </summary>
    public class LobbyPlayerController : NetworkBehaviour
    {
        [Networked] public NetworkString<_16> Nickname { get; private set; }
        [Networked] public NetworkBool IsReady { get; private set; }
        [Networked] public NetworkBool IsHost { get; private set; }
        [Networked] public NetworkBool IsTalking { get; private set; } // 음성 SDK 쪽에서 갱신
        [Networked] public int JoinOrder { get; private set; } // 방장 위임 시 "입장 순서 가장 빠른 사람" 판단용

        public event System.Action<bool> ReadyChanged;

        public override void Spawned()
        {
            if (Object.HasInputAuthority)
            {
                // 로컬 플레이어 초기 닉네임 세팅 (실제로는 프로필/로그인 시스템에서 가져와야 함)
                RPC_SetNickname($"Player_{Object.InputAuthority.PlayerId}");
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

        // ================== Ready ==================

        /// <summary>Ready 버튼 클릭 -> 로컬 클라가 호출.</summary>
        public void ToggleReady()
        {
            if (!Object.HasInputAuthority) return;
            RPC_SetReady(!IsReady);
        }

        [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
        private void RPC_SetReady(bool ready)
        {
            // 게임 시작 확정(Starting) 이후에는 Ready 토글 무시
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

        // ================== 방장 지정 (RoomManager가 호출) ==================

        public void SetHost(bool isHost)
        {
            if (!Object.HasStateAuthority) return;
            IsHost = isHost;

            // 방장이 바뀌면 새 방장도 다시 Ready를 눌러야 하는 기획 반영
            // (기획서: "방장도 일반 플레이어와 동일하게 Ready를 눌러야함")
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