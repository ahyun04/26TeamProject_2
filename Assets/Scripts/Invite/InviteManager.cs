using System;
using System.Collections.Generic;
using Fusion;
using LockdownProtocol.Networking;
using UnityEngine;

namespace LockdownProtocol.Lobby.Invite
{
    /// <summary>
    /// 초대 시스템의 서버(Host) 쪽 코어. RoomManager와 같은 프리팹에 붙어서 방 하나당 하나씩 존재한다.
    ///
    /// 기획서 문서3의 서버 역할표 그대로: 초대 요청 검증, 초대 가능 여부 확인, 초대 데이터 생성,
    /// 초대 대상에게 전달, 초대 수락/거절 최종 판정, 방 참가 처리(승인까지만 - 실제 참가는
    /// 기존 NetworkBootstrap.JoinRoom 재사용), 초대 만료 관리, 중복 초대 방지, 게임 시작 시
    /// 기존 초대 종료를 담당한다.
    ///
    /// 초대 데이터(_invites)는 Networked 프로퍼티가 아니라 서버 로컬 메모리에만 있다 -
    /// 기획서에 "초대 정보 저장: 서버만, 클라 X"라고 명시돼 있어서 굳이 복제할 필요가 없다.
    ///
    /// RequestInvite/CancelInvite는 이 방에 이미 들어와 있는 플레이어(inviter)가 보내는 거라
    /// 일반 Fusion RPC로 충분하다. 반면 대상 플레이어(target)는 이 세션에 없는 사람이라
    /// RPC가 닿지 않으므로, 그쪽은 IInviteTransport를 거쳐야 한다 - 이게 이 클래스가
    /// Transport 인터페이스에 의존하는 이유.
    /// </summary>
    [RequireComponent(typeof(RoomManager))]
    public class InviteManager : NetworkBehaviour
    {
        [Header("Settings")]
        [SerializeField] private float inviteLifetimeSeconds = 300f; // 기획서: 5분
        [SerializeField] private float expireCheckIntervalSeconds = 5f;

        private IInviteTransport Transport { get; set; }
        private RoomManager _roomManager;

        // InviteId -> InviteData. 서버 로컬 메모리 전용 (Networked 아님).
        private readonly Dictionary<string, InviteData> _invites = new Dictionary<string, InviteData>();

        // 초대를 보낸 인원이 방을 나가면 그 사람이 보낸 초대를 정리해야 하므로,
        // InviterId(문자열) <-> PlayerRef(이 세션 로컬) 대응을 따로 들고 있는다.
        private readonly Dictionary<string, PlayerRef> _inviterIdToPlayerRef = new Dictionary<string, PlayerRef>();

        private float _expireCheckTimer;

        // 초대자(로컬 플레이어)에게 돌아오는 결과들 - InviteClientManager가 구독해서 InviteListPanel에 반영한다.
        public event Action<string /*targetPlayerId*/, string /*inviteId*/> InviteSent;
        public event Action<string /*message*/> InviteRequestFailed;
        public event Action<string /*targetPlayerId*/, InviteState, string /*message*/> InviteResultForInviter;

        public override void Spawned()
        {
            _roomManager = GetComponent<RoomManager>();

            // GlobalLobbyInviteTransport는 씬(Main)에 미리 배치된 DontDestroyOnLoad 오브젝트라
            // RoomManager 프리팹 안에서 인스펙터로 미리 참조를 박아둘 수 없다 - 그래서 인스펙터
            // 필드 대신 자동으로 찾아서 연결한다. 나중에 Transport 구현을 바꾸더라도, 그 새
            // 구현체가 같은 방식(static Instance)으로 자기 자신을 노출해주기만 하면 여기 코드는
            // 그대로 두고 한 줄만 바꾸면 된다.
            Transport = GlobalLobbyInviteTransport.Instance;

            if (Transport == null)
            {
                Debug.LogError("[InviteManager] GlobalLobbyInviteTransport.Instance가 없습니다. " +
                               "Main 씬에 GlobalLobbyInviteTransport 오브젝트가 배치되어 있는지, " +
                               "그리고 이 오브젝트가 스폰되기 전에 전역 로비 접속이 끝났는지 확인하세요.");
            }

            if (Transport != null)
            {
                Transport.InviteResponseReceived += HandleInviteResponseReceived;
            }

            _roomManager.RoomStateChanged += HandleRoomStateChanged;

            NetworkBootstrap.OnPlayerLeftEvent += HandlePlayerLeft;
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            if (Transport != null)
            {
                Transport.InviteResponseReceived -= HandleInviteResponseReceived;
            }

            if (_roomManager != null)
            {
                _roomManager.RoomStateChanged -= HandleRoomStateChanged;
            }

            NetworkBootstrap.OnPlayerLeftEvent -= HandlePlayerLeft;
        }

        public override void FixedUpdateNetwork()
        {
            if (!Object.HasStateAuthority) return;

            _expireCheckTimer += Runner.DeltaTime;
            if (_expireCheckTimer >= expireCheckIntervalSeconds)
            {
                _expireCheckTimer = 0f;
                CheckExpiredInvites();
            }
        }

        // ================== 초대 요청 (인바이터 = 이 방 안에 있는 플레이어) ==================

        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        public async void RPC_RequestInvite(string inviterId, string inviterNickname, string targetPlayerId, RpcInfo info = default)
        {
            PlayerRef inviterRef = info.Source;

            // 1. 초대자가 실제로 이 방에 있는지 (기본 방어 - RPC가 왔다는 것 자체가 이 세션에
            //    연결돼 있다는 뜻이지만, 방 상태 조건은 별도로 검증해야 한다)
            if (_roomManager.CurrentRoomState != RoomManager.RoomState.Waiting)
            {
                NotifyRequestFailed(inviterRef, "게임이 이미 시작되었습니다");
                return;
            }

            // 2. 방 인원 확인 (현재 인원이 최대 인원 미만이어야 함)
            var currentPlayers = FindObjectsByType<LobbyPlayerController>(FindObjectsSortMode.None);
            if (currentPlayers.Length >= _roomManager.MaxPlayerCount)
            {
                NotifyRequestFailed(inviterRef, "방의 인원이 가득 찼습니다");
                return;
            }

            // 3. 중복 초대 확인 (동일 초대자 -> 동일 대상, Pending 상태로 이미 존재하면 거부)
            foreach (var existing in _invites.Values)
            {
                if (existing.InviterId == inviterId && existing.TargetPlayerId == targetPlayerId
                    && existing.State == InviteState.Pending)
                {
                    // 기획서: "새 Invite 생성 X, 기존 Pending 유지" - 실패 취급하지 않고 조용히 무시
                    return;
                }
            }

            // 4. 대상 플레이어 상태 확인 (온라인/다른 방/게임 중 여부) - Transport를 통해 조회
            if (Transport == null)
            {
                NotifyRequestFailed(inviterRef, "초대할 수 없습니다");
                return;
            }

            var presence = await Transport.QueryPresence(targetPlayerId);

            if (!presence.Exists)
            {
                NotifyRequestFailed(inviterRef, "존재하지 않는 플레이어입니다");
                return;
            }
            if (!presence.IsOnline)
            {
                NotifyRequestFailed(inviterRef, "플레이어가 오프라인입니다");
                return;
            }
            if (presence.IsInGame)
            {
                NotifyRequestFailed(inviterRef, "현재 게임 중인 플레이어입니다");
                return;
            }
            if (presence.IsInAnyRoom)
            {
                NotifyRequestFailed(inviterRef, "이미 다른 방에 참가 중입니다");
                return;
            }

            // ---- 초대 생성 승인 ----
            var invite = InviteData.Create(
                inviterId, inviterNickname, LocalPlayerIdentity.PlayerId,
                targetPlayerId,
                roomId: _roomManager.RoomName.ToString(), roomName: _roomManager.RoomName.ToString(),
                currentPlayerCount: currentPlayers.Length, maxPlayerCount: _roomManager.MaxPlayerCount,
                lifetime: TimeSpan.FromSeconds(inviteLifetimeSeconds));

            _invites[invite.InviteId] = invite;
            _inviterIdToPlayerRef[inviterId] = inviterRef;

            Transport.DeliverInvite(invite);

            // 초대자 쪽 버튼 상태를 "초대 대기 중"으로 갱신 (취소할 때 필요하니 inviteId도 같이 보냄)
            RPC_NotifyInviteSent(inviterRef, targetPlayerId, invite.InviteId);
        }

        private void NotifyRequestFailed(PlayerRef inviter, string message)
        {
            RPC_NotifyInviteFailed(inviter, message);
        }

        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        private void RPC_NotifyInviteSent([RpcTarget] PlayerRef inviter, string targetPlayerId, string inviteId)
        {
            InviteSent?.Invoke(targetPlayerId, inviteId);
        }

        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        private void RPC_NotifyInviteFailed([RpcTarget] PlayerRef inviter, string message)
        {
            InviteRequestFailed?.Invoke(message);
        }

        // ================== 초대 취소 (인바이터가 요청) ==================

        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        public void RPC_CancelInvite(string inviteId, RpcInfo info = default)
        {
            if (!_invites.TryGetValue(inviteId, out var invite)) return;
            if (invite.State != InviteState.Pending) return;

            // 취소 요청자가 실제 초대자인지 확인
            if (!_inviterIdToPlayerRef.TryGetValue(invite.InviterId, out var inviterRef) || inviterRef != info.Source)
            {
                return;
            }

            invite.State = InviteState.Cancelled;
            _invites.Remove(inviteId);

            Transport?.NotifyResult(invite.TargetPlayerId, inviteId, InviteState.Cancelled, "초대가 취소되었습니다");
        }

        // ================== 대상 플레이어의 수락/거절 (Transport로 들어옴) ==================

        private void HandleInviteResponseReceived(string inviteId, bool accepted)
        {
            if (!Object.HasStateAuthority) return;
            if (!_invites.TryGetValue(inviteId, out var invite)) return;
            if (invite.State != InviteState.Pending) return;

            if (!accepted)
            {
                invite.State = InviteState.Declined;
                _invites.Remove(inviteId);
                NotifyInviterOfResult(invite, InviteState.Declined, "상대가 초대를 거절했습니다");
                return;
            }

            // ---- 수락 시 재검증 (기획서: 초대 생성 이후 방 상태가 바뀌었을 수 있음) ----
            if (invite.IsExpired(DateTime.UtcNow))
            {
                invite.State = InviteState.Expired;
                _invites.Remove(inviteId);
                Transport?.NotifyResult(invite.TargetPlayerId, inviteId, InviteState.Expired, "초대가 만료되었습니다");
                return;
            }

            if (_roomManager.CurrentRoomState != RoomManager.RoomState.Waiting)
            {
                invite.State = InviteState.Failed;
                _invites.Remove(inviteId);
                Transport?.NotifyResult(invite.TargetPlayerId, inviteId, InviteState.Failed, "게임이 이미 시작되었습니다");
                return;
            }

            var currentPlayers = FindObjectsByType<LobbyPlayerController>(FindObjectsSortMode.None);
            if (currentPlayers.Length >= _roomManager.MaxPlayerCount)
            {
                invite.State = InviteState.Failed;
                _invites.Remove(inviteId);
                Transport?.NotifyResult(invite.TargetPlayerId, inviteId, InviteState.Failed, "방의 인원이 가득 찼습니다");
                return;
            }

            // ---- 승인 ----
            invite.State = InviteState.Accepted;
            _invites.Remove(inviteId);

            // 실제 방 참가(NetworkObject 스폰 등)는 대상 클라이언트가 이 승인을 받은 뒤
            // 기존 NetworkBootstrap.JoinRoom(approvedRoomId)를 그대로 호출해서 처리한다 -
            // 초대 시스템은 "참가해도 된다"는 승인까지만 책임진다.
            Transport?.NotifyResult(invite.TargetPlayerId, inviteId, InviteState.Accepted, null, approvedRoomId: invite.RoomId);

            NotifyInviterOfResult(invite, InviteState.Accepted, null);

            // 초대 받아 참가하는 플레이어는 Ready=False로 시작 - 이건 실제 LobbyPlayerController가
            // Spawn될 때(LobbyPlayerSpawner) 이미 기본값이 false라 별도 처리 불필요.
        }

        private void NotifyInviterOfResult(InviteData invite, InviteState state, string message)
        {
            if (_inviterIdToPlayerRef.TryGetValue(invite.InviterId, out var inviterRef))
            {
                RPC_NotifyInviteResultToInviter(inviterRef, invite.TargetPlayerId, state, message ?? string.Empty);
            }
        }

        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        private void RPC_NotifyInviteResultToInviter([RpcTarget] PlayerRef inviter, string targetPlayerId, InviteState state, string message)
        {
            InviteResultForInviter?.Invoke(targetPlayerId, state, message);
        }

        // ================== 만료 처리 ==================

        private void CheckExpiredInvites()
        {
            var now = DateTime.UtcNow;
            List<string> expired = null;

            foreach (var kvp in _invites)
            {
                if (kvp.Value.State == InviteState.Pending && kvp.Value.IsExpired(now))
                {
                    (expired ??= new List<string>()).Add(kvp.Key);
                }
            }

            if (expired == null) return;

            foreach (var inviteId in expired)
            {
                var invite = _invites[inviteId];
                invite.State = InviteState.Expired;
                _invites.Remove(inviteId);

                Transport?.NotifyResult(invite.TargetPlayerId, inviteId, InviteState.Expired, "초대 시간이 만료되었습니다");
                NotifyInviterOfResult(invite, InviteState.Expired, "초대 시간이 만료되었습니다");
            }
        }

        // ================== 게임 시작 시 대기 중 초대 전부 종료 ==================

        private void HandleRoomStateChanged(RoomManager.RoomState newState)
        {
            if (!Object.HasStateAuthority) return;
            if (newState != RoomManager.RoomState.Starting) return;

            CancelAllPendingInvites("게임이 시작되어 초대가 취소되었습니다");
        }

        // ================== 초대자가 방을 나가면 그 사람이 보낸 초대 정리 ==================

        private void HandlePlayerLeft(NetworkRunner runner, PlayerRef player)
        {
            if (!Object.HasStateAuthority) return;

            string leavingInviterId = null;
            foreach (var kvp in _inviterIdToPlayerRef)
            {
                if (kvp.Value == player)
                {
                    leavingInviterId = kvp.Key;
                    break;
                }
            }

            if (leavingInviterId == null) return;

            List<string> toCancel = null;
            foreach (var kvp in _invites)
            {
                if (kvp.Value.InviterId == leavingInviterId && kvp.Value.State == InviteState.Pending)
                {
                    (toCancel ??= new List<string>()).Add(kvp.Key);
                }
            }

            if (toCancel != null)
            {
                foreach (var inviteId in toCancel)
                {
                    var invite = _invites[inviteId];
                    invite.State = InviteState.Cancelled;
                    _invites.Remove(inviteId);
                    Transport?.NotifyResult(invite.TargetPlayerId, inviteId, InviteState.Cancelled, "초대한 플레이어가 방을 나갔습니다");
                }
            }

            _inviterIdToPlayerRef.Remove(leavingInviterId);
        }

        private void CancelAllPendingInvites(string message)
        {
            if (_invites.Count == 0) return;

            var ids = new List<string>(_invites.Keys);
            foreach (var inviteId in ids)
            {
                var invite = _invites[inviteId];
                if (invite.State != InviteState.Pending) continue;

                invite.State = InviteState.Cancelled;
                _invites.Remove(inviteId);
                Transport?.NotifyResult(invite.TargetPlayerId, inviteId, InviteState.Cancelled, message);
            }
        }

        // 방 삭제(RoomManager가 남은 플레이어 없어 Closed로 전환) 시에도 정리해야 하지만,
        // 그 시점엔 이 오브젝트 자체가 곧 Despawn되므로 Despawned()에서 이미 이벤트 구독을
        // 해제하는 것 이상으로는 별도 처리하지 않는다 - 남은 대기 중 초대는 어차피 방이
        // 사라지니 자연히 응답 못 받고 만료(5분) 처리된다. 즉시 정리가 필요하면 여기 추가할 것.
    }
}