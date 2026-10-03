using Fusion;
using Fusion.Addons.SimpleKCC;
using LockdownProtocol.Lobby;
using UnityEngine;

/// <summary>
/// 1인칭 카메라 관리 (Simple KCC 기반).
///
/// 이전 버전은 Pitch를 직접 마우스 입력에서 계산했지만, 이제는 PlayerMovement가
/// AddLookRotation()으로 KCC에 넘긴 Pitch/Yaw 값을 KCC.GetLookRotation()으로 읽어와서
/// 그대로 카메라 회전에 반영한다. 즉 이 클래스는 "값을 계산"하지 않고 "KCC가 계산한 값을 반영"만 한다.
/// </summary>
[RequireComponent(typeof(SimpleKCC))]
[RequireComponent(typeof(NetworkObject))]
public class PlayerCameraController : NetworkBehaviour
{
    [Header("References")]
    [SerializeField] private Camera playerCamera;
    [SerializeField] private AudioListener audioListener;
    [SerializeField] private Transform cameraPivot; // 머리 위치, Pitch 회전축

    private SimpleKCC _kcc;
    private LockdownProtocol.Networking.SpectatorManager _spectator;
    private Vector3 aimOffset; //카메라의 플레이어 기준 조준 위치
    private PlayerFeedback playerFeedback; //로컬 피격 화면 연출
    private Quaternion cameraLocalRotation; //흔들림을 누적하지 않을 카메라 기본 회전

    internal bool tryGetSimulationAimRay(out Ray ray) //호스트가 처리한 시점으로 공격 방향 계산
    {
        ray = default;
        if (_kcc == null || Object == null || !Object.IsValid) return false;
        Vector3 origin = _kcc.Position + _kcc.TransformRotation * aimOffset;
        ray = new Ray(origin, Quaternion.Euler(_kcc.GetLookRotation()) * Vector3.forward);
        return true;
    }

    internal Vector3 getSimulationEyePosition() //카메라가 벽 안으로 들어갔는지 검사할 기준점
    {
        return _kcc.Position + Vector3.up * aimOffset.y;
    }

    /// <summary>
    /// 이 클라이언트의 로컬 카메라 위치를 다른 시스템(음성 거리 감쇠 등)이 참조할 수 있게 노출.
    /// static인 이유: "지금 이 클라이언트의 귀가 어디 있는가"는 오브젝트 하나당 값이 아니라
    /// 클라이언트 전체에 딱 하나만 존재하는 값이라서다. 로컬 플레이어가 스폰될 때 설정되고,
    /// 디스폰 시 정리한다.
    /// </summary>
    public static Transform LocalListenerTransform { get; private set; }

    public override void Spawned()
    {
        _kcc = GetComponent<SimpleKCC>();
        _spectator = GetComponent<LockdownProtocol.Networking.SpectatorManager>();
        playerFeedback = GetComponent<PlayerFeedback>();
        cameraLocalRotation = playerCamera.transform.localRotation;
        aimOffset = transform.InverseTransformPoint(playerCamera.transform.position);

        bool isLocalPlayer = Object.HasInputAuthority;

        playerCamera.gameObject.SetActive(isLocalPlayer);
        if (audioListener != null)
            audioListener.enabled = isLocalPlayer;

        if (isLocalPlayer)
        {
            bool isMenuOpen = LobbyRoomUI.Instance != null && LobbyRoomUI.Instance.BlocksPlayerInput;
            Cursor.lockState = isMenuOpen ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = isMenuOpen;
            LocalListenerTransform = playerCamera.transform;
        }
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        if (Object != null && Object.HasInputAuthority && LocalListenerTransform == playerCamera.transform)
        {
            LocalListenerTransform = null;
        }
    }

    private void LateUpdate()
    {
        // LateUpdate를 쓰는 이유: KCC는 Render() 콜백에서 매 렌더 프레임마다 보간된 위치/회전을
        // 먼저 갱신하는데, LateUpdate는 그 이후에 실행되므로 최신 보간 결과를 반영할 수 있다.
        // (Fusion 공식 Simple KCC 샘플과 동일한 패턴)
        if (Object == null || !Object.IsValid || !Object.HasInputAuthority) return;
        if (_spectator != null && _spectator.IsSpectator)
        {
            NetworkObject target = _spectator.CurrentSpectateTarget != PlayerRef.None
                ? Runner.GetPlayerObject(_spectator.CurrentSpectateTarget) : null;
            PlayerCameraController targetCamera = target != null ? target.GetComponent<PlayerCameraController>() : null;
            if (targetCamera != null && targetCamera.playerCamera != null && targetCamera._kcc != null)
            {
                playerCamera.transform.SetPositionAndRotation(targetCamera.playerCamera.transform.position,
                    Quaternion.Euler(targetCamera._kcc.GetLookRotation()) *
                    (playerFeedback != null ? playerFeedback.getDamageShakeRotation() : Quaternion.identity));
            }
            return;
        }

        // KCC에 이미 누적된 Pitch/Yaw 중 Pitch만 꺼내와 카메라 피벗에 반영한다.
        // Yaw는 PlayerMovement가 이미 캐릭터 몸통(transform) 회전에 반영해뒀으므로 여기선 필요 없다.
        Vector2 pitchRotation = _kcc.GetLookRotation(true, false);
        cameraPivot.localRotation = Quaternion.Euler(pitchRotation);
        playerCamera.transform.localRotation = cameraLocalRotation *
            (playerFeedback != null ? playerFeedback.getDamageShakeRotation() : Quaternion.identity);
    }
}
