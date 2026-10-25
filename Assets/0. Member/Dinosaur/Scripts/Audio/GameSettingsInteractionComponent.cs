using LockdownProtocol.Lobby;
using Fusion;
using UnityEngine;
using UnityEngine.SceneManagement;

[DefaultExecutionOrder(-90)]
public class GameSettingsInteractionComponent : MonoBehaviour, ITargetable
{
    [SerializeField, Min(0.1f)] private float interactionDistance = 2.5f; //게임 설정 단말기의 접근 범위

    public NetworkObject TargetObject => null; //집기와 네트워크 아이템 사용 대상이 아닌 로컬 단말기

    internal bool canInteract //기존 아이템 탐지기로 단말기를 조준한 방장만 진입
    {
        get
        {
            if (!canUse) return false;
            PlayerTargetDetector detector = PlayerCameraController.LocalListenerTransform.GetComponentInParent<PlayerTargetDetector>(); //현재 본인의 조준 대상 탐지기
            return detector != null && ReferenceEquals(detector.DetectNow(), this);
        }
    }

    internal bool canUse //대기 중인 방장의 유효한 접근 여부
    {
        get
        {
            RoomManager room = RoomManager.Instance; //서버가 확정한 방과 권한
            Transform listener = PlayerCameraController.LocalListenerTransform; //현재 본인 시점
            if (!isActiveAndEnabled || SceneManager.GetActiveScene().name != "StandBy" ||
                room == null || room.Object == null || !room.Object.IsValid ||
                room.HostPlayerId != room.Runner.LocalPlayer || room.CurrentRoomState != RoomManager.RoomState.Waiting || listener == null)
                return false;
            PlayerHealth health = listener.GetComponentInParent<PlayerHealth>(); //관전자와 사망 상태 확인
            PlayerInteraction interaction = listener.GetComponentInParent<PlayerInteraction>(); //미션 진행 중 설정 열기 제한
            return health != null && health.CanAct && (interaction == null || !interaction.isInteracting) &&
                Vector3.Distance(health.transform.position, transform.position) <= interactionDistance;
        }
    }

    private void Update() //아이템 입력보다 먼저 G키 설정 진입 처리
    {
        bool available = canInteract && !GameAudio.blocksPlayerInput && !SessionDisconnectUIComponent.IsOpen &&
            Cursor.lockState == CursorLockMode.Locked; //다른 창과 게임 시작 전환 중 표시 제한
        GameAudio.setGameSettingsHint(available);
        if (available && Input.GetKeyDown(KeyCode.G)) GameAudio.showGameSettings(this);
    }

    private void OnDisable() //단말기와 씬이 사라지면 접근 안내 제거
    {
        GameAudio.setGameSettingsHint(false);
    }
}
