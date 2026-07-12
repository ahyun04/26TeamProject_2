using Fusion;
using UnityEngine;

namespace LockdownProtocol.Networking
{
    /// <summary>
    /// 1인칭 카메라 관리. 이 클라이언트가 실제로 조종하는 캐릭터(Input Authority)의 카메라만 활성화한다.
    /// 좌우 시점(Yaw)은 PlayerMovement가 NetworkInputData를 통해 캐릭터 몸통 회전으로 이미 처리하므로,
    /// 이 클래스는 상하 시점(Pitch)만 담당한다. Pitch는 순수 로컬 연출이라 네트워크 전송하지 않는다.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    public class PlayerCameraController : NetworkBehaviour
    {
        [Header("References")]
        [SerializeField] private Camera playerCamera;
        [SerializeField] private AudioListener audioListener;
        [SerializeField] private Transform cameraPivot; // 머리 위치, Pitch 회전축

        [Header("Look Settings")]
        [SerializeField] private float pitchSensitivity = 2f;
        [SerializeField] private float minPitch = -80f;
        [SerializeField] private float maxPitch = 80f;

        private float _pitch;

        public override void Spawned()
        {
            bool isLocalPlayer = Object.HasInputAuthority;

            // 로컬 플레이어(내가 조종하는 캐릭터)만 카메라/오디오 리스너를 켠다.
            // 다른 클라이언트의 캐릭터까지 카메라를 켜두면 씬에 활성 카메라와 AudioListener가
            // 여러 개 존재하게 되어 렌더링/오디오가 뒤섞인다.
            playerCamera.gameObject.SetActive(isLocalPlayer);
            if (audioListener != null)
                audioListener.enabled = isLocalPlayer;

            if (isLocalPlayer)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
        }

        private void Update()
        {
            if (!Object.HasInputAuthority) return;

            float mouseY = Input.GetAxisRaw("Mouse Y") * pitchSensitivity;
            _pitch = Mathf.Clamp(_pitch - mouseY, minPitch, maxPitch);

            cameraPivot.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
        }
    }
}