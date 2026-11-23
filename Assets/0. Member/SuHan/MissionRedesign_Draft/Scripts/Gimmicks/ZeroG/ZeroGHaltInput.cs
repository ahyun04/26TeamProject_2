using System;
using System.Collections.Generic;
using Fusion;
using Fusion.Sockets;
using LockdownProtocol.Networking;
using UnityEngine;

namespace TrustNoOne.Gimmicks
{
    /// <summary>
    /// 무중력 멈춤 입력 (본인 PC 에서만 — ZeroGMovement 가 실행 중에 붙이고 뗀다).
    /// [역할] 팀원 입력 제공자(PlayerInputProvider)가 채운 입력을 읽어, 버튼 칸 31 에 "멈춤"을 덧붙여 다시 넣는다.
    ///  멈춤 = 상호작용 중(F 누르고 있기 · 끌기 — PlayerInteraction.isInteracting) 또는 지난 입력 이후 장치를 왼쪽 버튼으로 누른 순간.
    ///  누른 순간은 프레임에서 기억했다가 다음 입력 하나에 싣는다 (팀원 점프 처리와 같은 방식).
    /// [이유] "상호작용 중" 표시는 본인 PC 에만 있다. 입력에 실어 보내야 호스트도 같은 틱에 멈춰 위치가 튀지 않는다.
    ///  Photon 은 플레이어당 입력 구조체 하나라 내 구조체를 따로 보낼 수 없다 → 팀원 구조체의 빈 칸을 쓴다 (팀원 파일은 그대로).
    ///  러너 시작 때 등록된 팀원 입력 제공자보다 뒤에 등록되어야 한다 — 앞에 불리면 팀원이 덮어써 멈춤이 사라진다 (위험 3, 진단 ③).
    /// [근거] 17a 명세 ZG6 · ZG16 · ZG22, 기획서 7장 "미션 UI가 활성화되면 플레이어의 이동을 정지"
    /// </summary>
    public class ZeroGHaltInput : MonoBehaviour, INetworkRunnerCallbacks
    {
        private NetworkRunner runner;
        private PlayerInteraction interaction;
        private PlayerTargetDetector detector;
        private PlayerHealth health;
        private KillManager combat;
        private bool clickLatch;
        private bool lastSent;
        private bool warnedMissingInput;

        internal void Begin(NetworkRunner networkRunner)
        {
            runner = networkRunner;
            interaction = GetComponent<PlayerInteraction>();
            detector = GetComponent<PlayerTargetDetector>();
            health = GetComponent<PlayerHealth>();
            combat = GetComponent<KillManager>();
            runner.AddCallbacks(this);
        }

        internal void End(NetworkRunner networkRunner)
        {
            if (networkRunner != null)
                networkRunner.RemoveCallbacks(this);

            runner = null;
        }

        private void Update()
        {
            if (!Input.GetMouseButtonDown(0))
                return;

            // 팀원 상호작용이 왼쪽 버튼을 받지 않는 상태면 멈추지 않는다 (행동 불가 · 메뉴 · 살인마 무기 공격)
            if ((health != null && !health.CanAct) || GameAudio.blocksPlayerInput || (combat != null && combat.usesWeaponAttackInput()))
                return;

            ITargetable target = detector != null ? detector.CurrentTarget : null;

            if (target is IInteractable || target is IDragInteractable)
                clickLatch = true;
        }

        public void OnInput(NetworkRunner networkRunner, NetworkInput input)
        {
            bool halt = clickLatch || (interaction != null && interaction.isInteracting);
            clickLatch = false;

            if (!input.TryGet(out NetworkInputData data))
            {
                if (!warnedMissingInput)
                {
                    warnedMissingInput = true;
                    Debug.LogWarning("[ZeroGMovement][진단] 팀원 입력이 아직 비어 있음 — 내 입력 콜백이 팀원 입력 제공자보다 먼저 불렸을 수 있음 (위험 3)");
                }

                return;
            }

            data.Buttons.Set(ZeroGMovement.HaltButton, halt);
            input.Set(data);

            if (halt != lastSent)
            {
                lastSent = halt;

                if (halt)
                    Debug.Log("[ZeroGMovement][진단] 멈춤 칸 보냄");
            }
        }

        public void OnPlayerJoined(NetworkRunner networkRunner, PlayerRef player) { }
        public void OnPlayerLeft(NetworkRunner networkRunner, PlayerRef player) { }
        public void OnInputMissing(NetworkRunner networkRunner, PlayerRef player, NetworkInput input) { }
        public void OnShutdown(NetworkRunner networkRunner, ShutdownReason shutdownReason) { }
        public void OnConnectedToServer(NetworkRunner networkRunner) { }
        public void OnDisconnectedFromServer(NetworkRunner networkRunner, NetDisconnectReason reason) { }
        public void OnConnectRequest(NetworkRunner networkRunner, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token) { }
        public void OnConnectFailed(NetworkRunner networkRunner, NetAddress remoteAddress, NetConnectFailedReason reason) { }
        public void OnUserSimulationMessage(NetworkRunner networkRunner, SimulationMessagePtr message) { }
        public void OnSessionListUpdated(NetworkRunner networkRunner, List<SessionInfo> sessionList) { }
        public void OnCustomAuthenticationResponse(NetworkRunner networkRunner, Dictionary<string, object> data) { }
        public void OnHostMigration(NetworkRunner networkRunner, HostMigrationToken hostMigrationToken) { }
        public void OnSceneLoadDone(NetworkRunner networkRunner) { }
        public void OnSceneLoadStart(NetworkRunner networkRunner) { }
        public void OnObjectExitAOI(NetworkRunner networkRunner, NetworkObject obj, PlayerRef player) { }
        public void OnObjectEnterAOI(NetworkRunner networkRunner, NetworkObject obj, PlayerRef player) { }
        public void OnReliableDataReceived(NetworkRunner networkRunner, PlayerRef player, ReliableKey key, ArraySegment<byte> data) { }
        public void OnReliableDataProgress(NetworkRunner networkRunner, PlayerRef player, ReliableKey key, float progress) { }
    }
}
