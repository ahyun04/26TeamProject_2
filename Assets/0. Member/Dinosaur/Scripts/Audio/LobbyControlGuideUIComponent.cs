using LockdownProtocol.Lobby;
using UnityEngine;

[DefaultExecutionOrder(-80)]
public class LobbyControlGuideUIComponent : MonoBehaviour
{
    [SerializeField] private GameObject guidePanel; //우측 하단의 조작키 안내

    private bool inStandBy; //대기실 표시 범위
    private bool inGameplay; //게임 중 표시 범위
    private bool requestedVisible = true; //본인이 선택한 안내 표시 상태

    private void Awake() //최초 씬 진입 전 표시 숨김
    {
        guidePanel.SetActive(false);
    }

    internal void changeScene(string sceneName) //대기실과 게임 진입 시 자동 표시 상태 복원
    {
        inStandBy = sceneName == "StandBy";
        inGameplay = NetworkBootstrap.isGameplayScene(sceneName);
        requestedVisible = true;
        guidePanel.SetActive(false);
    }

    private void Update() //게임 입력을 막지 않고 Tab으로 안내만 전환
    {
        RoomManager room = RoomManager.Instance; //현재 대기실의 서버 상태
        bool waiting = inStandBy && room != null && room.Object != null && room.Object.IsValid &&
            room.CurrentRoomState == RoomManager.RoomState.Waiting && room.Runner.IsRunning;
        Transform listener = PlayerCameraController.LocalListenerTransform; //현재 본인의 플레이·관전 시점
        PlayerCameraController localCamera = listener != null ? listener.GetComponentInParent<PlayerCameraController>() : null;
        bool available = (waiting || inGameplay) && localCamera != null && localCamera.isLocalCameraActive &&
            localCamera.Runner != null && localCamera.Runner.IsRunning && !SessionDisconnectUIComponent.IsOpen;
        bool blocked = GameAudio.blocksPlayerInput; //설정 입력 중 Tab 중복 처리 방지
        if (available && !blocked && Input.GetKeyDown(KeyCode.Tab)) requestedVisible = !requestedVisible;
        guidePanel.SetActive(available && !blocked && requestedVisible);
    }
}
