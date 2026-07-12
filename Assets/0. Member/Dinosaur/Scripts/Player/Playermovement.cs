using Fusion;
using UnityEngine;

namespace LockdownProtocol.Networking
{
    /// <summary>
    /// 네트워크 동기화되는 플레이어 이동 로직.
    /// FixedUpdateNetwork()에서 입력을 받아 CharacterController로 실제 이동을 계산한다.
    /// 위치/회전 값 자체의 네트워크 전송은 NetworkTransform 컴포넌트가 대신 처리하므로
    /// 이 클래스는 "입력 → 이동 계산"만 책임진다 (SRP).
    ///
    /// 카메라 상하 시점(Pitch)은 순수 로컬 연출이라 이 클래스가 아닌 PlayerCameraController가 담당한다.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class PlayerMovement : NetworkBehaviour
    {
        [Header("Move Speeds")]
        [SerializeField] private float walkSpeed = 4f;
        [SerializeField] private float sprintSpeed = 6f;
        [SerializeField] private float crouchSpeed = 2f;

        [Header("Gravity")]
        [SerializeField] private float gravity = -9.81f;
        [SerializeField] private float jumpForce = 5f;

        [Header("Look")]
        [SerializeField] private float turnSpeed = 1f;

        private CharacterController _characterController;
        private PlayerStamina _stamina;

        /// <summary>
        /// 다른 스크립트(카메라, 애니메이션)가 참조할 수 있도록 이동 상태를 읽기 전용으로 노출.
        /// Networked 속성으로 선언해 State Authority가 계산한 값이 모든 클라이언트에 동기화되게 한다.
        /// </summary>
        [Networked] public bool IsGrounded { get; private set; }
        [Networked] public float CurrentSpeed { get; private set; }

        // 중요: FixedUpdateNetwork() 안에서 계산에 쓰이는 값은 반드시 [Networked]여야 한다.
        // 일반 private 필드로 두면 Fusion의 롤백(Rollback)/재시뮬레이션 시 이 값이 저장·복원되지 않아,
        // 클라이언트 예측값과 Host의 권위 있는 값이 점점 어긋나는 디싱크(desync)가 발생한다.
        [Networked] private Vector3 VerticalVelocity { get; set; }

        public override void Spawned()
        {
            _characterController = GetComponent<CharacterController>();
            _stamina = GetComponent<PlayerStamina>();
        }

        public override void FixedUpdateNetwork()
        {
            // Input Authority가 없는(관전 중이거나 남의 오브젝트인) 경우 입력이 없으므로 계산을 건너뛴다.
            if (!GetInput(out NetworkInputData input))
                return;

            ApplyRotation(input);
            ApplyMovement(input);
        }

        private void ApplyRotation(NetworkInputData input)
        {
            // 좌우 시점(Yaw)만 캐릭터 몸통 회전에 반영한다. 상하 시점은 로컬 카메라 전용.
            float yaw = input.LookRotation.x * turnSpeed;
            transform.Rotate(Vector3.up, yaw);
        }

        private void ApplyMovement(NetworkInputData input)
        {
            float speed = DetermineSpeed(input);
            CurrentSpeed = speed;

            Vector3 moveDirection = transform.forward * input.MoveDirection.y + transform.right * input.MoveDirection.x;
            moveDirection = Vector3.ClampMagnitude(moveDirection, 1f) * speed;

            IsGrounded = _characterController.isGrounded;

            Vector3 verticalVelocity = VerticalVelocity;

            if (IsGrounded && verticalVelocity.y < 0f)
            {
                verticalVelocity.y = -2f; // 지면에 밀착 유지 (isGrounded 오검출 방지용 관행값)
            }

            if (IsGrounded && input.IsPressed(InputButton.Jump))
            {
                verticalVelocity.y = jumpForce;
            }

            verticalVelocity.y += gravity * Runner.DeltaTime;
            VerticalVelocity = verticalVelocity;

            Vector3 finalMove = (moveDirection + verticalVelocity) * Runner.DeltaTime;
            _characterController.Move(finalMove);
        }

        private float DetermineSpeed(NetworkInputData input)
        {
            if (input.IsPressed(InputButton.Crouch))
                return crouchSpeed;

            bool wantsSprint = input.IsPressed(InputButton.Sprint);
            // _stamina가 없는 프리팹(예: 향후 NPC 재사용)에서는 제약 없이 스프린트를 허용한다.
            bool canSprint = _stamina == null || _stamina.HasStamina;

            if (wantsSprint && canSprint)
                return sprintSpeed;

            return walkSpeed;
        }
    }
}