using Fusion;
using UnityEngine;

namespace LockdownProtocol.Lobby
{
    /// <summary>
    /// 로비 씬에서의 이동을 담당한다. 기획서 흐름(WASD -> 로컬 입력 -> 캐릭터 이동 ->
    /// 애니메이션 재생 -> 서버 위치 동기화 -> 다른 플레이어에게 표시)을 그대로 따른다.
    ///
    /// 로비는 전투가 없는 자유 이동 공간이라 엄격한 서버 권위 이동(풀 리컨실리에이션)까지는
    /// 필요 없다고 판단해, Input Authority 클라가 로컬로 먼저 움직이고 주기적으로 위치를
    /// State Authority에 보고 -> 최소한의 유효성 검사(최대 이동 속도 초과 여부)만 거쳐
    /// Networked 값으로 다른 클라에 뿌리는 방식을 쓴다. 핵심 게임플레이(Kill 판정 등)처럼
    /// 완전한 서버 신뢰 검증이 필요한 영역이 아니므로 이 정도 타협이 적절하다고 봄.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class LobbyMovementController : NetworkBehaviour
    {
        public enum MovementState
        {
            Idle,
            Walk,
            Run,
            Jump
        }

        [Header("Movement Settings")]
        [SerializeField] private float walkSpeed = 3f;
        [SerializeField] private float runSpeed = 6f;
        [SerializeField] private float jumpForce = 5f;
        [SerializeField] private float gravity = -9.81f;

        [Header("Anti-Cheat 여유값")]
        [SerializeField] private float maxAllowedSpeedMultiplier = 1.5f; // runSpeed 대비 여유

        [Header("Camera")]
        [SerializeField] private Transform cameraPivot;
        [SerializeField] private float mouseSensitivity = 2f;

        [Networked, OnChangedRender(nameof(HandleRemoteTransformChanged))]
        public Vector3 NetworkedPosition { get; private set; }

        [Networked, OnChangedRender(nameof(HandleRemoteTransformChanged))]
        public float NetworkedYRotation { get; private set; }

        [Networked, OnChangedRender(nameof(HandleRemoteStateChanged))]
        public MovementState CurrentState { get; private set; }

        private CharacterController _controller;
        private Vector3 _velocity;
        private float _cameraPitch;
        private float _reportTimer;
        private float _lastAcceptedReportTime;
        private MovementState _localState;

        private const float ReportIntervalSeconds = 0.05f; // 20Hz 정도로 위치 보고

        public override void Spawned()
        {
            _controller = GetComponent<CharacterController>();
            if (HasStateAuthority)
            {
                NetworkedPosition = transform.position;
                NetworkedYRotation = transform.eulerAngles.y;
                _lastAcceptedReportTime = Time.time;
            }

            if (Object.HasInputAuthority && cameraPivot != null)
            {
                LocalListenerTransform = cameraPivot;
                // 커서 잠금/해제는 여기서 하지 않는다 - LobbyRoomUI가 ESC 토글로 중앙에서 관리한다.
                // (여기서도 잠가버리면 UI 열림 상태와 상관없이 계속 잠기려고 해서 충돌났었음)
            }

            // 리모트(내가 조작하지 않는) 캐릭터는 CharacterController가 물리 이동과 충돌하지 않도록
            // 순수 Transform 보간만 담당하게 비활성화
            if (!Object.HasInputAuthority)
            {
                _controller.enabled = false;
            }
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            if (Object != null && Object.HasInputAuthority && LocalListenerTransform == cameraPivot)
            {
                LocalListenerTransform = null;
            }
        }

        /// <summary>로컬 플레이어의 카메라 위치. LobbyVoiceManager가 거리 계산 리스너로 참조한다.</summary>
        public static Transform LocalListenerTransform { get; private set; }

        public override void FixedUpdateNetwork()
        {
            if (Object == null || !Object.IsValid || !Object.HasInputAuthority) return;

            HandleLocalMovement();
        }

        private void Update()
        {
            if (Object == null || !Object.IsValid || !Object.HasInputAuthority) return;

            HandleCameraLook();
            ReportPositionIfNeeded();
        }

        // ================== 로컬 이동 (Input Authority) ==================

        private void HandleLocalMovement()
        {
            if (RoomManager.Instance != null && RoomManager.Instance.CurrentRoomState != RoomManager.RoomState.Waiting)
                return;
            float h = Input.GetAxisRaw("Horizontal");
            float v = Input.GetAxisRaw("Vertical");
            bool isRunning = Input.GetKey(KeyCode.LeftShift);
            bool jumpPressed = Input.GetKeyDown(KeyCode.Space);

            Vector3 moveDir = (transform.right * h + transform.forward * v).normalized;
            float speed = isRunning ? runSpeed : walkSpeed;

            if (_controller.isGrounded)
            {
                _velocity.y = -1f; // 살짝 눌러줘서 접지 유지

                if (jumpPressed)
                {
                    _velocity.y = jumpForce;
                    SetState(MovementState.Jump);
                }
            }
            else
            {
                _velocity.y += gravity * Runner.DeltaTime;
            }

            Vector3 horizontalMove = moveDir * speed;
            _controller.Move((horizontalMove + Vector3.up * _velocity.y) * Runner.DeltaTime);

            if (_controller.isGrounded && jumpPressed == false)
            {
                MovementState state = moveDir.sqrMagnitude < 0.01f
                    ? MovementState.Idle
                    : (isRunning ? MovementState.Run : MovementState.Walk);
                SetState(state);
            }
        }

        private void SetState(MovementState state)
        {
            if (_localState == state) return;
            _localState = state;
            if (HasStateAuthority) CurrentState = state;
            PlayMovementAnimation(state);
        }

        // ================== 카메라 ==================

        private void HandleCameraLook()
        {
            if (cameraPivot == null) return;

            // 우클릭을 누르고 있을 때만 카메라가 돈다. 커서는 항상 보이는 상태로 둔다
            // (기획서: 미니 HUD의 "방 나가기" 등이 ESC 없이도 항상 클릭 가능해야 하므로,
            // 커서를 평소에 잠갔다 ESC로 풀었다 하는 방식 대신 이 방식을 택함).
            if (!Input.GetMouseButton(1)) return;

            float mouseX = Input.GetAxis("Mouse X") * mouseSensitivity;
            float mouseY = Input.GetAxis("Mouse Y") * mouseSensitivity;

            transform.Rotate(Vector3.up * mouseX);

            _cameraPitch = Mathf.Clamp(_cameraPitch - mouseY, -80f, 80f);
            cameraPivot.localEulerAngles = new Vector3(_cameraPitch, 0f, 0f);
        }

        // ================== 서버 위치 보고 ==================

        private void ReportPositionIfNeeded()
        {
            _reportTimer += Time.deltaTime;
            if (_reportTimer < ReportIntervalSeconds) return;
            _reportTimer = 0f;

            RPC_ReportPosition(transform.position, transform.eulerAngles.y, _localState);
        }

        [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
        private void RPC_ReportPosition(Vector3 position, float yRotation, MovementState state)
        {
            if (RoomManager.Instance != null && RoomManager.Instance.CurrentRoomState != RoomManager.RoomState.Waiting)
                return;
            if (float.IsNaN(position.x) || float.IsInfinity(position.x) ||
                float.IsNaN(position.y) || float.IsInfinity(position.y) ||
                float.IsNaN(position.z) || float.IsInfinity(position.z) ||
                float.IsNaN(yRotation) || float.IsInfinity(yRotation)) return;
            // 최소한의 유효성 검사: 한 번에 비정상적으로 멀리 이동했는지만 체크.
            // (텔레포트/스피드핵 방지 목적이며, 로비는 전투가 없어 엄격한 리컨실리에이션까지는 불필요)
            float elapsed = Mathf.Max(ReportIntervalSeconds * 2f, Time.time - _lastAcceptedReportTime);
            float maxDeltaPerReport = runSpeed * maxAllowedSpeedMultiplier * elapsed;
            if (NetworkedPosition != Vector3.zero &&
                Vector3.Distance(NetworkedPosition, position) > maxDeltaPerReport)
            {
                Debug.LogWarning($"[LobbyMovementController] {Object.InputAuthority} 비정상 이동 감지 - 위치 무시");
                RPC_CorrectPosition(NetworkedPosition, NetworkedYRotation);
                _lastAcceptedReportTime = Time.time;
                return;
            }

            NetworkedPosition = position;
            NetworkedYRotation = yRotation;
            _lastAcceptedReportTime = Time.time;
            if (state >= MovementState.Idle && state <= MovementState.Jump)
                CurrentState = state;
        }

        [Rpc(RpcSources.StateAuthority, RpcTargets.InputAuthority)]
        private void RPC_CorrectPosition(Vector3 position, float yRotation)
        {
            _controller.enabled = false;
            transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yRotation, 0f));
            _controller.enabled = true;
            _velocity = Vector3.zero;
        }

        // ================== 리모트 캐릭터 반영 ==================

        private void HandleRemoteTransformChanged()
        {
            if (Object.HasInputAuthority) return; // 본인은 로컬 이동이 우선

            transform.position = NetworkedPosition;
            transform.eulerAngles = new Vector3(0f, NetworkedYRotation, 0f);
        }

        private void HandleRemoteStateChanged()
        {
            if (Object.HasInputAuthority) return;
            PlayMovementAnimation(CurrentState);
        }

        // Animator 세팅되면 구현
        private void PlayMovementAnimation(MovementState state)
        {
            Debug.Log($"[LobbyMovementController] {name} 애니메이션 상태 변경: {state} (미구현)");
        }
    }
}
