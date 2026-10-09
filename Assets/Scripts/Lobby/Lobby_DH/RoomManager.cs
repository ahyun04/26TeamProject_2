using System;
using Fusion;
using LockdownProtocol.Networking;
using UnityEngine;

namespace LockdownProtocol.Lobby
{
    /// <summary>
    /// 로비의 방(Room) 생성/참가/나가기 및 방장 관리를 담당.
    /// Fusion의 세션(Session) 기능을 감싸는 래퍼 역할이며,
    /// 방 목록 UI나 Ready/게임 시작 판정 등은 다른 매니저에 위임한다.
    ///
    /// PlayerSpawner와 동일하게 NetworkBootstrap의 static 이벤트를 구독하는 방식을 쓴다
    /// (INetworkRunnerCallbacks를 직접 구현하면 runner.AddCallbacks()를 호출해주지 않는 한
    /// 실제로 콜백이 오지 않으므로, 이미 있는 NetworkBootstrap 쪽으로 통일).
    /// </summary>
    public class RoomManager : NetworkBehaviour
    {
        public enum RoomState
        {
            Waiting,
            Standby,   // 기획서 추가 상태 (Waiting ~ Starting 사이). 현재 전환 로직은 미정
            Starting,
            Playing,
            Ending,
            Closed
        }

        public enum JoinRoomResult
        {
            Success,
            RoomFull,
            NotFound,
            GameStarted,
            ConnectionError
        }

        /// <summary>NetworkBootstrap.JoinRoom()이 반환한 StartGameResult를 Lobby UI가 쓰기 편한 열거형으로 변환.</summary>
        public static JoinRoomResult MapJoinResult(StartGameResult result)
        {
            if (result.Ok) return JoinRoomResult.Success;

            return result.ShutdownReason switch
            {
                ShutdownReason.GameIsFull => JoinRoomResult.RoomFull,
                ShutdownReason.GameNotFound => JoinRoomResult.NotFound,
                _ => JoinRoomResult.ConnectionError
            };
        }

        [Header("Room Settings")]
        [SerializeField] private int minPlayerCount = 2;
        [SerializeField] private int defaultMaxPlayerCount = 6;

        [Networked] public NetworkString<_32> RoomName { get; private set; }
        [Networked] public PlayerRef HostPlayerId { get; private set; }
        [Networked] public int MaxPlayerCount { get; private set; }
        [Networked] public RoomState CurrentRoomState { get; private set; }
        [Networked] public NetworkBool IsPrivate { get; private set; }
        /// <summary>8자리 영숫자 초대 코드(소문자). Fusion SessionName과 동일 — 서버(호스트)만 설정.</summary>
        [Networked] public NetworkString<_16> InviteCode { get; private set; }
        [Networked] public NetworkBool IsInviteEnabled { get; private set; }
        [Networked] public long CreatedTime { get; private set; }  // UTC ticks

        public const int AbsoluteMinPlayers = 2;
        public const int AbsoluteMaxPlayers = 10;

        public event Action<int> MaxPlayerChanged;
        public event Action<string> MaxPlayerChangeRejected;

        public static RoomManager Instance { get; private set; }

        public event Action<PlayerRef> HostChanged;
        public event Action<RoomState> RoomStateChanged;

        private RoomState _lastKnownState;

        public override void Spawned()
        {
            Instance = this;

            // MaxPlayerCount가 0일 때(= 갓 만든 방)만 기본값으로 채운다.
            // 호스트 마이그레이션으로 복구된 RoomManager는 이미 값이 복사되어 있는데,
            // 여기서 무조건 덮어쓰면 방 최대 인원이 기본값으로 되돌아간다.
            if (Object.HasStateAuthority && MaxPlayerCount == 0)
            {
                CurrentRoomState = RoomState.Waiting;
                MaxPlayerCount = defaultMaxPlayerCount;
            }

            _lastKnownState = CurrentRoomState;

            NetworkBootstrap.OnPlayerJoinedEvent += HandlePlayerJoined;
            NetworkBootstrap.OnPlayerLeftEvent += HandlePlayerLeft;
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            NetworkBootstrap.OnPlayerJoinedEvent -= HandlePlayerJoined;
            NetworkBootstrap.OnPlayerLeftEvent -= HandlePlayerLeft;
            if (Instance == this) Instance = null;
        }

        public override void FixedUpdateNetwork()
        {
            // 클라 쪽 상태 변화 감지 (Networked 프로퍼티는 OnChanged 콜백을 따로 등록하는 게 더 정확하지만
            // 우선 폴링으로 최소 동작만 확보)
            if (_lastKnownState != CurrentRoomState)
            {
                _lastKnownState = CurrentRoomState;
                RoomStateChanged?.Invoke(CurrentRoomState);
            }
        }

        // ================== 초기화 (NetworkBootstrap이 호스트 세션 시작 성공 후 호출) ==================
        // 세션(StartGame)을 여는 책임은 NetworkBootstrap에 있다 — RoomManager는 NetworkBehaviour라
        // 세션이 이미 시작된 뒤에야 스폰될 수 있으므로, 이 클래스 스스로 StartGame을 호출할 수 없다.
        // (RoomManager가 스폰되려면 세션이 먼저 있어야 하는데, 예전 버전은 그 반대로 짜여 있었음 — 수정)

        public void InitializeRoom(string roomName, int maxPlayers, bool isPrivate, PlayerRef hostPlayer)
        {
            if (!Object.HasStateAuthority) return;

            RoomName = roomName;
            MaxPlayerCount = maxPlayers > 0 ? maxPlayers : defaultMaxPlayerCount;
            IsPrivate = isPrivate;
            // 마이그레이션 후 재호출돼도 코드/생성시각은 유지 (비어 있을 때만 채운다)
            if (InviteCode.Length == 0)
            {
                InviteCode = Runner.SessionInfo.Name;
                IsInviteEnabled = true;
                CreatedTime = DateTime.UtcNow.Ticks;
            }
            HostPlayerId = hostPlayer;
            SetRoomState(RoomState.Waiting);
            FindFirstObjectByType<LobbyPlayerSpawner>()?.RefreshHostFlag(hostPlayer);
            RPC_NotifyHostChanged(hostPlayer);
        }

        /// <summary>
        /// 호스트 마이그레이션 직후 새 Host에서 호출. 옛 방장의 PlayerRef가 남아있으므로
        /// 새 Host(= 남은 사람 중 Photon이 뽑은 사람)를 방장으로 지정하고, 시작 중이었다면 Waiting으로 되돌린다.
        /// </summary>
        public void RecoverAfterHostMigration(PlayerRef newHost)
        {
            if (!Object.HasStateAuthority) return;

            HostPlayerId = newHost;
            SetRoomState(RoomState.Waiting);
        }

        // ================== 최대 인원 변경 ==================

        /// <summary>방장 전용. 게임 시작 전(Waiting)에만, 현재 인원 미만/허용 범위 밖은 거부. 성공 시 Networked 값으로 전원 동기화.</summary>
        [Rpc(RpcSources.All, RpcTargets.StateAuthority, HostMode = RpcHostMode.SourceIsHostPlayer)]
        public void RPC_RequestChangeMaxPlayer(int newMax, RpcInfo info = default)
        {
            PlayerRef requester = info.Source;
            Debug.Log($"[RoomManager] 최대 인원 변경 요청: {MaxPlayerCount} -> {newMax} (요청자 {requester}, 방장 {HostPlayerId}, 상태 {CurrentRoomState})");

            if (requester != HostPlayerId) { RPC_MaxPlayerRejected(requester, "방장만 변경할 수 있습니다."); return; }
            if (CurrentRoomState != RoomState.Waiting) { RPC_MaxPlayerRejected(requester, "게임 시작 전에만 변경할 수 있습니다."); return; }
            if (newMax < AbsoluteMinPlayers || newMax > AbsoluteMaxPlayers) { RPC_MaxPlayerRejected(requester, $"{AbsoluteMinPlayers}~{AbsoluteMaxPlayers}명 사이로 설정하세요."); return; }

            int current = 0;
            foreach (var _ in Runner.ActivePlayers) current++;
            if (newMax < current) { RPC_MaxPlayerRejected(requester, "현재 인원보다 적게 설정할 수 없습니다."); return; }

            MaxPlayerCount = newMax;
            Debug.Log($"[RoomManager] 최대 인원 변경 완료: {newMax}");
            RPC_NotifyMaxPlayerChanged(newMax);
        }

        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        private void RPC_NotifyMaxPlayerChanged(int newMax) => MaxPlayerChanged?.Invoke(newMax);

        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        private void RPC_MaxPlayerRejected([RpcTarget] PlayerRef target, string reason)
        {
            Debug.Log($"[RoomManager] 최대 인원 변경 거부: {reason}");
            MaxPlayerChangeRejected?.Invoke(reason);
        }

        // ================== 방 나가기 ==================

        /// <summary>방 나가기 버튼(확인창 통과 후) -> 이 메서드 호출. 실제 Shutdown/씬 전환은 NetworkBootstrap이 담당.</summary>
        public void RequestLeaveRoom()
        {
            var bootstrap = FindFirstObjectByType<NetworkBootstrap>();
            if (bootstrap == null)
            {
                Debug.LogError("[RoomManager] NetworkBootstrap을 찾을 수 없음");
                return;
            }

            bootstrap.LeaveRoom();
        }

        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        private void RPC_NotifyHostChanged(PlayerRef newHost)
        {
            Debug.Log($"[RoomManager] 방장 변경: {newHost}");
            HostChanged?.Invoke(newHost);
        }

        // ================== 게임 시작 시 상태 전환 ==================
        // 실제 시작 조건 판정(인원/Ready)은 LobbyGameStartManager가 담당하고,
        // 조건 통과 후 여기 상태만 바꿔준다.

        public void SetRoomState(RoomState state)
        {
            if (!Object.HasStateAuthority) return;
            CurrentRoomState = state;
            Runner.SessionInfo.IsOpen = state == RoomState.Waiting;
        }

        // ================== NetworkBootstrap 이벤트 핸들러 ==================
        // PlayerSpawner와 동일한 구독 패턴. Runner.Spawn()으로 실제 플레이어 오브젝트를 만드는 건
        // PlayerSpawner(또는 이후 만들 LobbyPlayerSpawner)의 책임이고, 여기서는 방 상태만 갱신한다.

        private void HandlePlayerJoined(NetworkRunner runner, PlayerRef player)
        {
            if (runner != Runner) return;
            if (!Object.HasStateAuthority) return;

            if (CurrentRoomState != RoomState.Waiting)
            {
                // 게임 진행 중 참가는 StartGameArgs 단계에서 대부분 막히지만 방어적으로 한 번 더 체크
                Debug.LogWarning($"[RoomManager] Waiting 상태가 아닐 때 참가 시도: {player}");
                return;
            }

            // Fusion 세션 정원은 생성 시점 값이라, 방장이 줄인 MaxPlayerCount 초과 입장은 서버가 직접 차단
            int active = 0;
            foreach (var _ in runner.ActivePlayers) active++;
            if (MaxPlayerCount > 0 && active > MaxPlayerCount && player != runner.LocalPlayer)
            {
                runner.Disconnect(player);
                return;
            }

            if (HostPlayerId == PlayerRef.None)
            {
                HostPlayerId = player;
            }
        }

        private void HandlePlayerLeft(NetworkRunner runner, PlayerRef player)
        {
            if (runner != Runner) return;
            if (!Object.HasStateAuthority) return;

            if (player != HostPlayerId) return;

            if (!TryAssignNewHost(player))
            {
                SetRoomState(RoomState.Closed);
            }
        }

        private bool TryAssignNewHost(PlayerRef leavingHost)
        {
            LobbyPlayerController best = null;

            foreach (var controller in FindObjectsByType<LobbyPlayerController>(FindObjectsSortMode.None))
            {
                if (controller == null) continue;
                if (controller.Object == null || !controller.Object.IsValid) continue;

                PlayerRef owner = controller.Object.InputAuthority;
                if (owner == leavingHost) continue; // 나가는 사람 제외
                if (owner == PlayerRef.None) continue;

                if (best == null || controller.JoinOrder < best.JoinOrder)
                {
                    best = controller;
                }
            }

            if (best == null) return false;

            PlayerRef newHost = best.Object.InputAuthority;
            HostPlayerId = newHost;
            FindFirstObjectByType<LobbyPlayerSpawner>()?.RefreshHostFlag(newHost);
            RPC_NotifyHostChanged(newHost);

            Debug.Log($"[RoomManager] 방장 이양: {leavingHost} -> {newHost} (JoinOrder 기준)");
            return true;
        }
    }
}