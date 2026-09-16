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

        public static RoomManager Instance { get; private set; }

        public event Action<PlayerRef> HostChanged;
        public event Action<RoomState> RoomStateChanged;

        private RoomState _lastKnownState;

        public override void Spawned()
        {
            Instance = this;

            if (Object.HasStateAuthority)
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
            HostPlayerId = hostPlayer;
            CurrentRoomState = RoomState.Waiting;
            FindFirstObjectByType<LobbyPlayerSpawner>()?.RefreshHostFlag(hostPlayer);
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

        // ================== 방장 위임 ==================

        private void AssignNewHost(NetworkRunner runner)
        {
            if (!Object.HasStateAuthority) return;

            // 기획서: 입장 순서가 가장 빠른 플레이어를 새 방장으로.
            // ActivePlayers 순서가 입장 순서를 보장하지 않을 수 있어 실제로는
            // LobbyPlayerController 쪽에 JoinedTick 같은 값을 두고 비교하는 게 안전함 (TODO)
            foreach (var player in runner.ActivePlayers)
            {
                if (player == HostPlayerId) continue;
                HostPlayerId = player;
                FindFirstObjectByType<LobbyPlayerSpawner>()?.RefreshHostFlag(player);
                RPC_NotifyHostChanged(player);
                return;
            }

            LobbyPlayerController earliest = null;

            foreach (var controller in FindObjectsByType<LobbyPlayerController>(FindObjectsSortMode.None))
            {
                if (controller.Object == null || controller.Object.InputAuthority == HostPlayerId) continue;
                if (earliest == null || controller.JoinOrder < earliest.JoinOrder)
                {
                    earliest = controller;
                }
            }

            if (earliest != null)
            {
                var newHost = earliest.Object.InputAuthority;
                HostPlayerId = newHost;
                FindFirstObjectByType<LobbyPlayerSpawner>()?.RefreshHostFlag(newHost);
                RPC_NotifyHostChanged(newHost);
                return;
            }

            // 남은 플레이어 없음 -> 방 삭제
            CurrentRoomState = RoomState.Closed;
            Debug.Log("[RoomManager] 남은 플레이어 없음 - 방 삭제 (미구현: 세션 종료 처리)");
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

            if (HostPlayerId == PlayerRef.None)
            {
                HostPlayerId = player;
            }
        }

        private void HandlePlayerLeft(NetworkRunner runner, PlayerRef player)
        {
            if (runner != Runner) return;
            if (!Object.HasStateAuthority) return;

            bool wasHost = player == HostPlayerId;

            if (wasHost)
            {
                bool wasStarting = CurrentRoomState == RoomState.Starting;
                AssignNewHost(runner);

                // 게임 시작 중(Starting) 방장이 나간 경우: 시작 취소하고 Waiting 복귀
                if (wasStarting && CurrentRoomState != RoomState.Closed)
                {
                    CurrentRoomState = RoomState.Waiting;
                    Debug.Log("[RoomManager] 게임 시작 중 방장 이탈 - 시작 취소, Waiting 복귀");
                }
            }
        }
    }
}
