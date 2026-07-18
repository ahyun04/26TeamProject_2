using Fusion;
using Fusion.Addons.SimpleKCC;
using UnityEngine;

namespace LockdownProtocol.Networking
{
    /// <summary>
    /// 네트워크 동기화되는 플레이어 이동 로직 (Simple KCC 기반).
    ///
    /// 이전 CharacterController 버전과 달리, 중력/접지/충돌/위치 동기화를 SimpleKCC가
    /// 내부적으로 전부 처리한다. 이 클래스는 "입력을 받아 KCC에 전달"하는 것만 책임진다.
    /// NetworkTransform은 더 이상 쓰지 않는다 — SimpleKCC 자체가 Sync Component 역할을 겸한다.
    ///
    /// 주의: NetworkInputData.LookRotation은 (X=Yaw, Y=Pitch) 순서인데,
    /// SimpleKCC.AddLookRotation()은 (X=Pitch, Y=Yaw) 순서를 기대하므로 호출 시 순서를 바꿔서 전달한다.
    /// (실제 테스트 시 시점이 반대로 돌면 이 부분을 다시 확인할 것)
    /// </summary>
    [RequireComponent(typeof(SimpleKCC))]
    public class PlayerMovement : NetworkBehaviour
    {
        [Header("Move Speeds")]
        [SerializeField] private float walkSpeed = 4f;
        [SerializeField] private float sprintSpeed = 6f;
        [SerializeField] private float crouchSpeed = 2f;

        [Header("Jump")]
        [SerializeField] private float jumpImpulse = 5f;

        private SimpleKCC _kcc;
        private PlayerStamina _stamina;

        /// <summary>
        /// 다른 스크립트(애니메이션 등)가 참조할 수 있도록 이동 상태를 읽기 전용으로 노출.
        /// </summary>
        [Networked] public bool IsGrounded { get; private set; }
        [Networked] public float CurrentSpeed { get; private set; }

        public override void Spawned()
        {
            _kcc = GetComponent<SimpleKCC>();
            _stamina = GetComponent<PlayerStamina>();
        }

        public override void FixedUpdateNetwork()
        {
            // 접지 상태는 입력 유무와 무관하게 매 틱 갱신한다.
            IsGrounded = _kcc.IsGrounded;

            float jumpImpulseValue = 0f;
            Vector3 moveVelocity = Vector3.zero;

            if (GetInput(out NetworkInputData input))
            {
                // NetworkInputData는 (Yaw, Pitch) 순서, KCC는 (Pitch, Yaw) 순서를 기대하므로 스왑.
                // Pitch는 부호도 반전해야 한다: 마우스를 위로 움직이면(양수) 화면이 위를 봐야 하는데,
                // Unity 오일러 각 관례상 "위를 보는" 회전은 X축 기준 음수 방향이기 때문이다.
                _kcc.AddLookRotation(new Vector2(-input.LookRotation.y, input.LookRotation.x));

                float speed = DetermineSpeed(input);
                CurrentSpeed = speed;

                Vector3 worldDirection = _kcc.TransformRotation * new Vector3(input.MoveDirection.x, 0f, input.MoveDirection.y);
                moveVelocity = Vector3.ClampMagnitude(worldDirection, 1f) * speed;

                if (_kcc.IsGrounded && input.IsPressed(InputButton.Jump))
                {
                    jumpImpulseValue = jumpImpulse;
                }
            }

            // 입력이 없는 틱에도 Move()는 호출해야 중력/접지 처리가 끊기지 않는다.
            _kcc.Move(moveVelocity, jumpImpulseValue);
        }

        private float DetermineSpeed(NetworkInputData input)
        {
            if (input.IsPressed(InputButton.Crouch))
                return crouchSpeed;

            bool wantsSprint = input.IsPressed(InputButton.Sprint);
            // _stamina가 없는 프리팹(예: 향후 NPC 재사용)에서는 제약 없이 스프린트를 허용한다.
            bool canSprint = _stamina == null || _stamina.HasStamina;

            return (wantsSprint && canSprint) ? sprintSpeed : walkSpeed;
        }
    }
}