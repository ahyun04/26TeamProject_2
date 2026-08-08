using Fusion;
using Fusion.Addons.SimpleKCC;
using UnityEngine;

namespace LockdownProtocol.Networking
{
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

            bool isLocalPlayer = Object.HasInputAuthority;

            playerCamera.gameObject.SetActive(isLocalPlayer);
            if (audioListener != null)
                audioListener.enabled = isLocalPlayer;

            if (isLocalPlayer)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
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
            if (Object == null || !Object.HasInputAuthority) return;

            // KCC에 이미 누적된 Pitch/Yaw 중 Pitch만 꺼내와 카메라 피벗에 반영한다.
            // Yaw는 PlayerMovement가 이미 캐릭터 몸통(transform) 회전에 반영해뒀으므로 여기선 필요 없다.
            Vector2 pitchRotation = _kcc.GetLookRotation(true, false);
            cameraPivot.localRotation = Quaternion.Euler(pitchRotation);
        }
    }
}