using Fusion;
using UnityEngine;

namespace HyunWoo
{
    [RequireComponent(typeof(Player))]
    public class PlayerLook : NetworkBehaviour
    {
        [SerializeField] private Transform viewRoot;
        [SerializeField] private Transform cameraPivot;

        private Player player;

        private void Awake()
        {
            player = GetComponent<Player>();
        }

        public override void Spawned()
        {
            bool isLocal = Object.HasInputAuthority;

            // 자신의 카메라 계층만 활성화
            viewRoot.gameObject.SetActive(isLocal);

            if (!isLocal)
                return;

            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        private void LateUpdate()
        {
            if (Object == null || !Object.HasInputAuthority)
                return;

            viewRoot.rotation =
                Quaternion.Euler(0f, player.Yaw, 0f);

            cameraPivot.localRotation =
                Quaternion.Euler(player.Pitch, 0f, 0f);
        }
    }
}