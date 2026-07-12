using Fusion;
using UnityEngine;

namespace HyunWoo
{
    [RequireComponent(typeof(NetworkCharacterController))]
    public class Player : NetworkBehaviour
    {
        [SerializeField] private float speed = 5f;
        [SerializeField] private float sensitivity = 2f;

        [Networked] public float Yaw { get; set; }
        [Networked] public float Pitch { get; set; }

        private NetworkCharacterController _cc;

        private void Awake()
        {
            _cc = GetComponent<NetworkCharacterController>();
        }

        public override void FixedUpdateNetwork()
        {
            if (!GetInput(out NetworkInputData data))
                return;

            // 회전값은 마우스 입력으로만 변경
            Yaw += data.LookDelta.y * sensitivity;
            Pitch = Mathf.Clamp(
                Pitch + data.LookDelta.x * sensitivity,
                -80f,
                80f
            );

            // 현재 카메라의 좌우 방향 기준으로 WASD 이동
            Quaternion rotation = Quaternion.Euler(0f, Yaw, 0f);

            Vector3 move = rotation * new Vector3(
                data.Move.x,
                0f,
                data.Move.y
            );

            move = Vector3.ClampMagnitude(move, 1f);

            _cc.Move(move * speed * Runner.DeltaTime);
        }
    }
}


