using System.Collections.Generic;
using Fusion;
using LockdownProtocol.Lobby;
using UnityEngine;

namespace LockdownProtocol.Networking
{
    [RequireComponent(typeof(PlayerHealth))]
    public class SpectatorManager : NetworkBehaviour
    {
        [SerializeField] private float spectatorDelaySeconds = 5f;

        [Networked] public NetworkBool IsSpectator { get; private set; }
        [Networked] public PlayerRef CurrentSpectateTarget { get; private set; }
        [Networked] private TickTimer TransitionTimer { get; set; }

        private PlayerHealth health; //관전 전환을 요청한 플레이어의 생존 상태

        public override void Spawned() //생존 상태 참조 연결
        {
            health = GetComponent<PlayerHealth>();
        }

        private void Update() //관전 중 좌클릭은 다음 대상, 우클릭은 이전 대상
        {
            if (Object == null || !Object.IsValid || !HasInputAuthority || !IsSpectator ||
                SessionDisconnectUIComponent.IsOpen || Cursor.lockState != CursorLockMode.Locked ||
                (LobbyRoomUI.Instance != null && LobbyRoomUI.Instance.BlocksPlayerInput)) return;

            if (Input.GetMouseButtonDown(0)) RPC_ChangeSpectator(true);
            else if (Input.GetMouseButtonDown(1)) RPC_ChangeSpectator(false);
        }

        public override void FixedUpdateNetwork()
        {
            if (!Object.HasStateAuthority || health == null || !health.IsDead) return;

            if (!IsSpectator && TransitionTimer.IsRunning && TransitionTimer.Expired(Runner))
            {
                EnterSpectatorMode();
            }

            if (IsSpectator)
            {
                ValidateSpectateTarget();
            }
        }

        public void BeginSpectatorTransition()
        {
            if (!Object.HasStateAuthority || health == null || !health.IsDead ||
                IsSpectator || TransitionTimer.IsRunning) return;
            TransitionTimer = TickTimer.CreateFromSeconds(Runner, spectatorDelaySeconds);
        }

        private void EnterSpectatorMode()
        {
            IsSpectator = true;
            TransitionTimer = TickTimer.None;

            var living = GetLivingPlayers();
            CurrentSpectateTarget = living.Count > 0 ? living[0] : PlayerRef.None;
        }

        private void ValidateSpectateTarget()
        {
            var targetObj = CurrentSpectateTarget != PlayerRef.None
                ? Runner.GetPlayerObject(CurrentSpectateTarget) : null;
            var targetHealth = targetObj != null ? targetObj.GetComponent<PlayerHealth>() : null;

            if (targetHealth != null && !targetHealth.IsDead && !targetHealth.IsEscaped) return;

            var living = GetLivingPlayers();
            CurrentSpectateTarget = living.Count > 0 ? living[0] : PlayerRef.None;

        }

        [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
        public void RPC_ChangeSpectator(bool next)
        {
            if (!HasStateAuthority || !IsSpectator || health == null || !health.IsDead) return;
            var living = GetLivingPlayers();
            if (living.Count == 0) return;

            int currentIndex = living.IndexOf(CurrentSpectateTarget);
            int nextIndex = currentIndex < 0
                ? 0
                : (currentIndex + (next ? 1 : -1) + living.Count) % living.Count;

            CurrentSpectateTarget = living[nextIndex];
        }

        private List<PlayerRef> GetLivingPlayers()
        {
            var living = new List<PlayerRef>();
            foreach (var player in Runner.ActivePlayers)
            {
                if (player == Object.InputAuthority) continue;

                var playerObj = Runner.GetPlayerObject(player);
                if (playerObj == null) continue;

                var health = playerObj.GetComponent<PlayerHealth>();
                if (health != null && !health.IsDead && !health.IsEscaped)
                    living.Add(player);
            }
            return living;
        }
    }
}
