using Fusion;
using Fusion.Addons.SimpleKCC;
using LockdownProtocol.Networking;
using LockdownProtocol.Lobby;
using UnityEngine;

[RequireComponent(typeof(SimpleKCC))]
public class PlayerMovement : NetworkBehaviour
{
    [Header("이동 속도")]
    [SerializeField] private float walkSpeed = 4f;
    [SerializeField] private float sprintSpeed = 6f;
    [SerializeField] private float crouchSpeed = 2f;

    [Header("점프")]
    [SerializeField] private float jumpImpulseForce = 5f;

    [Header("시점 회전")]
    [SerializeField] private float minimumPitch = -80f;
    [SerializeField] private float maximumPitch = 80f;

    private SimpleKCC simpleKCC;
    private PlayerStamina stamina;
    private PlayerHealth health;

    public bool IsGrounded => simpleKCC != null && simpleKCC.IsGrounded;

    public float CurrentSpeed
    {
        get
        {
            if (simpleKCC == null)
                return 0f;

            Vector3 velocity = simpleKCC.RealVelocity;
            return new Vector2(velocity.x, velocity.z).magnitude;
        }
    }

    public override void Spawned()
    {
        simpleKCC = GetComponent<SimpleKCC>();
        stamina = GetComponent<PlayerStamina>();
        health = GetComponent<PlayerHealth>();
    }

    public override void FixedUpdateNetwork()
    {
        RoomManager room = RoomManager.Instance;
        bool isStarting = room != null && room.Object != null && room.Object.IsValid &&
                          room.Runner == Runner && room.CurrentRoomState == RoomManager.RoomState.Starting;
        if (isStarting || (health != null && !health.CanAct))
        {
            stamina?.updateStamina(default, false);
            if (HasStateAuthority || HasInputAuthority) simpleKCC.Move();
            return;
        }
        if (!GetInput(out NetworkInputData input))
        {
            stamina?.updateStamina(default, false);
            // Host가 특정 틱의 입력을 받지 못한 경우에도 중력과 접지를 처리한다.
            // 다른 플레이어를 보는 일반 프록시에서는 Move를 호출하지 않는다.
            if (Object.HasStateAuthority)
                simpleKCC.Move();

            return;
        }

        UpdateLookRotation(input);

        Vector2 moveInput = Vector2.ClampMagnitude(input.MoveDirection, 1f);
        bool isMoving = moveInput.sqrMagnitude > 0.0001f;
        bool isSprinting = stamina != null
            ? stamina.updateStamina(input, true)
            : input.IsPressed(InputButton.Sprint) && isMoving; //이번 틱에 허용된 달리기
        float moveSpeed = DetermineSpeed(input, isSprinting);

        Vector3 localDirection = new Vector3(moveInput.x, 0f, moveInput.y);
        Vector3 moveVelocity = simpleKCC.TransformRotation * localDirection * moveSpeed;

        float jumpImpulse = 0f;

        if (simpleKCC.IsGrounded && input.IsPressed(InputButton.Jump))
            jumpImpulse = jumpImpulseForce;

        simpleKCC.Move(moveVelocity, jumpImpulse);
    }

    private void UpdateLookRotation(NetworkInputData input)
    {
        float pitchDelta = -input.LookRotation.y;
        float yawDelta = input.LookRotation.x;

        simpleKCC.AddLookRotation(
            new Vector2(pitchDelta, yawDelta),
            minimumPitch,
            maximumPitch);
    }

    private float DetermineSpeed(NetworkInputData input, bool isSprinting)
    {
        if (input.IsPressed(InputButton.Crouch))
            return crouchSpeed;

        return isSprinting ? sprintSpeed : walkSpeed;
    }

    public Vector2 LocalMoveVelocity
    {
        get
        {
            if (simpleKCC == null)
                return Vector2.zero;

            Vector3 horizontalVelocity = simpleKCC.RealVelocity;
            horizontalVelocity.y = 0f;

            if (horizontalVelocity.sqrMagnitude < 0.0001f)
                return Vector2.zero;

            Vector3 localVelocity =
                Quaternion.Inverse(simpleKCC.TransformRotation) *
                horizontalVelocity;

            return new Vector2(localVelocity.x, localVelocity.z);
        }
    }
}
