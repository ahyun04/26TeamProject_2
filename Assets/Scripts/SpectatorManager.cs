using System.Collections.Generic;
using Fusion;
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

        public override void FixedUpdateNetwork()
        {
            if (!Object.HasStateAuthority) return;

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
            if (!Object.HasStateAuthority) return;
            TransitionTimer = TickTimer.CreateFromSeconds(Runner, spectatorDelaySeconds);
        }

        private void EnterSpectatorMode()
        {
            IsSpectator = true;
            TransitionTimer = TickTimer.None;

            var living = GetLivingPlayers();
            CurrentSpectateTarget = living.Count > 0 ? living[0] : PlayerRef.None;

            if (living.Count == 0)
            {
                // 결과 화면 시스템 나오면 연결
                Debug.Log($"[SpectatorManager] {name} 관전 가능한 플레이어 없음 - 결과 화면 전환 필요 (미구현)");
            }
        }

        private void ValidateSpectateTarget()
        {
            var targetObj = Runner.GetPlayerObject(CurrentSpectateTarget);
            var targetHealth = targetObj != null ? targetObj.GetComponent<PlayerHealth>() : null;

            if (targetHealth != null && !targetHealth.IsDead && !targetHealth.IsEscaped) return;

            var living = GetLivingPlayers();
            CurrentSpectateTarget = living.Count > 0 ? living[0] : PlayerRef.None;

        }

        [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
        public void RPC_ChangeSpectator(bool next)
        {
            if (!HasStateAuthority || !IsSpectator) return;
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
