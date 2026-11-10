using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Fusion;
using Fusion.Sockets;
using UnityEngine;

namespace LockdownProtocol.Lobby
{
    /// <summary>
    /// [역할] Photon 로비에 접속해서 지금 열려 있는 방 목록(SessionInfo)을 받아 온다. (빠른 입장 방 목록용)
    /// [구조] 방에 들어가는 NetworkBootstrap 의 러너와 완전히 별개인 "조회 전용 러너"를 따로 만든다.
    ///  그래서 이 러너가 꺼져도 NetworkBootstrap 의 연결 종료 안내가 뜨지 않는다.
    /// [사용] StartBrowsing() 으로 목록 수신 시작, 방에 들어가기 전에 StopBrowsing() 으로 끈다.
    /// </summary>
    public class SessionBrowser : MonoBehaviour, INetworkRunnerCallbacks
    {
        public event Action<IReadOnlyList<SessionInfo>> ListUpdated;
        public event Action<string> Failed;

        private readonly List<SessionInfo> sessions = new List<SessionInfo>();
        private NetworkRunner runner;
        private GameObject runnerObject;
        private bool starting;

        public IReadOnlyList<SessionInfo> Sessions => sessions;
        public bool IsBrowsing => runner != null;

        public async Task StartBrowsing()
        {
            if (runner != null || starting) return;
            starting = true;

            runnerObject = new GameObject("SessionBrowserRunner");
            runnerObject.transform.SetParent(transform);
            NetworkRunner created = runnerObject.AddComponent<NetworkRunner>();
            created.ProvideInput = false;
            created.AddCallbacks(this);
            runner = created;

            StartGameResult result;
            try { result = await created.JoinSessionLobby(SessionLobby.ClientServer); }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                starting = false;
                cleanup();
                Failed?.Invoke("방 목록을 불러오지 못했습니다.");
                return;
            }

            starting = false;
            if (this == null) return;

            if (!result.Ok)
            {
                Debug.LogError($"[SessionBrowser] 로비 접속 실패: {result.ShutdownReason} / {result.ErrorMessage}");
                cleanup();
                Failed?.Invoke("방 목록을 불러오지 못했습니다.");
            }
        }

        public async Task StopBrowsing()
        {
            NetworkRunner stopping = runner;
            GameObject obj = runnerObject;
            runner = null;
            runnerObject = null;
            sessions.Clear();

            if (stopping != null)
            {
                try { await stopping.Shutdown(); }
                catch (Exception exception) { Debug.LogException(exception); }
            }
            if (obj != null) Destroy(obj);
        }

        private void cleanup()
        {
            runner = null;
            if (runnerObject != null) Destroy(runnerObject);
            runnerObject = null;
            sessions.Clear();
        }

        private void OnDestroy()
        {
            if (runner != null) _ = StopBrowsing();
        }

        // ---------- INetworkRunnerCallbacks ----------
        public void OnSessionListUpdated(NetworkRunner callbackRunner, List<SessionInfo> sessionList)
        {
            if (callbackRunner != runner) return;
            sessions.Clear();
            if (sessionList != null) sessions.AddRange(sessionList);
            ListUpdated?.Invoke(sessions);
        }

        public void OnShutdown(NetworkRunner callbackRunner, ShutdownReason shutdownReason)
        {
            if (callbackRunner != runner) return;
            runner = null;
            sessions.Clear();
        }

        public void OnPlayerJoined(NetworkRunner runner, PlayerRef player) { }
        public void OnPlayerLeft(NetworkRunner runner, PlayerRef player) { }
        public void OnConnectedToServer(NetworkRunner runner) { }
        public void OnDisconnectedFromServer(NetworkRunner runner, NetDisconnectReason reason) { }
        public void OnConnectFailed(NetworkRunner runner, NetAddress remoteAddress, NetConnectFailedReason reason) { }
        public void OnConnectRequest(NetworkRunner runner, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token) { }
        public void OnInput(NetworkRunner runner, NetworkInput input) { }
        public void OnInputMissing(NetworkRunner runner, PlayerRef player, NetworkInput input) { }
        public void OnSimulationMessage(NetworkRunner runner, SimulationMessagePtr message) { }
        public void OnCustomAuthenticationResponse(NetworkRunner runner, Dictionary<string, object> data) { }
        public void OnHostMigration(NetworkRunner runner, HostMigrationToken hostMigrationToken) { }
        public void OnReliableDataReceived(NetworkRunner runner, PlayerRef player, ReliableKey key, ArraySegment<byte> data) { }
        public void OnReliableDataProgress(NetworkRunner runner, PlayerRef player, ReliableKey key, float progress) { }
        public void OnSceneLoadDone(NetworkRunner runner) { }
        public void OnSceneLoadStart(NetworkRunner runner) { }
        public void OnObjectExitAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
        public void OnObjectEnterAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
        public void OnUserSimulationMessage(NetworkRunner runner, SimulationMessagePtr message) { }
    }
}