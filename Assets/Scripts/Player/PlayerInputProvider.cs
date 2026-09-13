using Fusion;
using Fusion.Sockets;
using LockdownProtocol.Networking;
using LockdownProtocol.Lobby;
using UnityEngine;

public class PlayerInputProvider : MonoBehaviour, INetworkRunnerCallbacks
{
    private Vector2 accumulatedLook;
    private bool jumpPressed;

    private void Update()
    {
        LobbyRoomUI lobbyUI = LobbyRoomUI.Instance;
        if (lobbyUI != null && (lobbyUI.BlocksPlayerInput || Input.GetKeyDown(KeyCode.Escape)))
        {
            accumulatedLook = Vector2.zero;
            jumpPressed = false;
            return;
        }
        accumulatedLook += new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y"));
        jumpPressed |= Input.GetKeyDown(KeyCode.Space);
    }

    private void OnDisable()
    {
        accumulatedLook = Vector2.zero;
        jumpPressed = false;
    }

    public void OnInput(NetworkRunner runner, NetworkInput input)
    {
        NetworkInputData data = new NetworkInputData();
        if (LobbyRoomUI.Instance != null &&
            (LobbyRoomUI.Instance.BlocksPlayerInput || Input.GetKeyDown(KeyCode.Escape)))
        {
            accumulatedLook = Vector2.zero;
            jumpPressed = false;
            input.Set(data);
            return;
        }

        // 이동
        data.MoveDirection = new Vector2(
            Input.GetAxisRaw("Horizontal"),
            Input.GetAxisRaw("Vertical")
        );

        // 마우스 시점
        data.LookRotation = accumulatedLook;
        accumulatedLook = Vector2.zero;

        // 점프
        data.Buttons.Set(
            InputButton.Jump,
            jumpPressed
        );
        jumpPressed = false;

        // 달리기
        data.Buttons.Set(
            InputButton.Sprint,
            Input.GetKey(KeyCode.LeftShift)
        );

        // 앉기
        data.Buttons.Set(
            InputButton.Crouch,
            Input.GetKey(KeyCode.LeftControl)
        );

        // Fusion에게 입력 전달
        input.Set(data);
    }


    // 아래는 INetworkRunnerCallbacks 구현용
    public void OnPlayerJoined(NetworkRunner runner, PlayerRef player) { }
    public void OnPlayerLeft(NetworkRunner runner, PlayerRef player) { }
    public void OnInputMissing(NetworkRunner runner, PlayerRef player, NetworkInput input) { }
    public void OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason) { }
    public void OnConnectedToServer(NetworkRunner runner) { }
    public void OnDisconnectedFromServer(NetworkRunner runner, NetDisconnectReason reason) { }
    public void OnConnectRequest(NetworkRunner runner, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token) { }
    public void OnConnectFailed(NetworkRunner runner, NetAddress remoteAddress, NetConnectFailedReason reason) { }
    public void OnUserSimulationMessage(NetworkRunner runner, SimulationMessagePtr message) { }
    public void OnSessionListUpdated(NetworkRunner runner, System.Collections.Generic.List<SessionInfo> sessionList) { }
    public void OnCustomAuthenticationResponse(NetworkRunner runner, System.Collections.Generic.Dictionary<string, object> data) { }
    public void OnHostMigration(NetworkRunner runner, HostMigrationToken hostMigrationToken) { }
    public void OnSceneLoadDone(NetworkRunner runner) { }
    public void OnSceneLoadStart(NetworkRunner runner) { }
    public void OnObjectExitAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
    public void OnObjectEnterAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
    public void OnReliableDataReceived(NetworkRunner runner, PlayerRef player, ReliableKey key, System.ArraySegment<byte> data) { }
    public void OnReliableDataProgress(NetworkRunner runner, PlayerRef player, ReliableKey key, float progress) { }
}
