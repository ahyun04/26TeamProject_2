using System.Collections.Generic;
using Fusion;
using UnityEngine;

namespace LockdownProtocol.Lobby
{
    /// <summary>
    /// 게임 시작 조건(최소 인원 + 전원 Ready + 방장 요청)을 검증하고,
    /// 통과 시 RoomState를 Starting으로 바꾼 뒤 게임 씬 전환을 트리거한다.
    /// 실제 게임 세션 생성/씬 로드는 GameManager(게임 쪽)에 위임 — 이 매니저는
    /// "시작해도 되는가"까지만 책임진다 (기획서: 로비 서버는 게임 플레이를 담당하지 않음).
    /// </summary>
    public class LobbyGameStartManager : NetworkBehaviour
    {
        [SerializeField] private int minPlayerCount = 2;
        [SerializeField] private float startCountdownSeconds = 3f;

        [Networked] private TickTimer StartCountdown { get; set; }

        public enum StartFailReason
        {
            NotHost,
            NotEnoughPlayers,
            NotAllReady,
            AlreadyStarting
        }

        public event System.Action<StartFailReason> StartFailed;
        public event System.Action CountdownStarted;

        public override void FixedUpdateNetwork()
        {
            if (!Object.HasStateAuthority) return;

            if (StartCountdown.IsRunning && StartCountdown.Expired(Runner))
            {
                StartCountdown = TickTimer.None;
                BeginGameSceneTransition();
            }
        }

        /// <summary>[게임 시작] 버튼(방장 전용) -> 이 메서드 호출.
        /// LobbyGameStartManager는 RoomManager처럼 방 단위 싱글턴이라 특정 플레이어의
        /// InputAuthority를 갖지 않는다. RpcSources.InputAuthority로는 애초에 아무도 이 RPC를
        /// 호출할 수 없었으므로(호출 권한이 있는 InputAuthority 자체가 없음), All로 열어두고
        /// 요청자 식별은 RpcInfo.Source로 한다.</summary>
        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        public void RPC_RequestStartGame(RpcInfo info = default)
        {
            if (RoomManager.Instance == null) return;

            PlayerRef requester = info.Source;

            // 1. 요청자가 방장인지 확인
            if (RoomManager.Instance.HostPlayerId != requester)
            {
                RPC_NotifyStartFailed(requester, StartFailReason.NotHost);
                return;
            }

            // 2. 이미 시작 절차 진행 중인지
            if (RoomManager.Instance.CurrentRoomState != RoomManager.RoomState.Waiting)
            {
                RPC_NotifyStartFailed(requester, StartFailReason.AlreadyStarting);
                return;
            }

            var players = GetAllLobbyPlayers();

            // 3. 최소 인원 확인
            if (players.Count < minPlayerCount)
            {
                RPC_NotifyStartFailed(requester, StartFailReason.NotEnoughPlayers);
                return;
            }

            // 4. 전원 Ready 확인
            foreach (var p in players)
            {
                if (!p.IsReady)
                {
                    RPC_NotifyStartFailed(requester, StartFailReason.NotAllReady);
                    return;
                }
            }

            // ---- 시작 승인 ----
            RoomManager.Instance.SetRoomState(RoomManager.RoomState.Starting);
            RPC_NotifyStartApproved();

            StartCountdown = TickTimer.CreateFromSeconds(Runner, startCountdownSeconds);
        }

        private void BeginGameSceneTransition()
        {
            if (!Object.HasStateAuthority) return;

            // 로비 이동/Ready 입력 제한 + 음성 채널 종료는 각 클라에서
            // RPC_NotifyStartApproved 수신 시점에 이미 처리되어 있어야 함

            // TODO: GameManager 쪽 게임 세션 생성 요청 연결 (미구현)
            Debug.Log("[LobbyGameStartManager] 게임 씬 전환 요청 (미구현) - GameManager 연결 필요");

            RoomManager.Instance.SetRoomState(RoomManager.RoomState.Playing);
        }

        private List<LobbyPlayerController> GetAllLobbyPlayers()
        {
            // 실제로는 RoomManager나 별도 PlayerRegistry에서 관리하는 리스트를 참조하는 게 낫다.
            // 우선 씬에서 찾는 방식으로 최소 동작 확보 (TODO: 성능/구조 개선)
            var result = new List<LobbyPlayerController>();
            foreach (var p in FindObjectsByType<LobbyPlayerController>(FindObjectsSortMode.None))
            {
                result.Add(p);
            }
            return result;
        }

        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        private void RPC_NotifyStartApproved()
        {
            Debug.Log("[LobbyGameStartManager] 게임 시작 승인");
            CountdownStarted?.Invoke();
            // 클라: 로비 이동/Ready 입력 제한, 음성 채널 종료, 카운트다운 UI 재생은 이 이벤트 구독해서 처리
        }

        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        private void RPC_NotifyStartFailed([RpcTarget] PlayerRef requester, StartFailReason reason)
        {
            Debug.Log($"[LobbyGameStartManager] 게임 시작 실패: {reason}");
            StartFailed?.Invoke(reason);
        }
    }
}