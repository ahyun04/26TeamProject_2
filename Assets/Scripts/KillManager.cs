using Fusion;
using UnityEngine;

namespace LockdownProtocol.Networking
{
    [RequireComponent(typeof(PlayerHealth))]
    public class KillManager : NetworkBehaviour
    {
        [Header("Kill Settings")]
        [SerializeField] private float killRange = 2f;
        [SerializeField] private float killCooldownSeconds = 15f;

        [Networked] public NetworkBool IsMurderer { get; set; }

        [Networked] private TickTimer CooldownTimer { get; set; }
        [Networked] public NetworkBool CanKill { get; private set; } = true;

        private PlayerHealth _health;
        private RoleAssignment _roles;
        private MissionSystem _missions;
        private Camera _camera;

        public override void Spawned()
        {
            _health = GetComponent<PlayerHealth>();
            _roles = FindFirstObjectByType<RoleAssignment>();
            _missions = FindFirstObjectByType<MissionSystem>();
            _camera = GetComponentInChildren<Camera>(true);
        }

        public override void FixedUpdateNetwork()
        {
            if (!Object.HasStateAuthority) return;
            IsMurderer = _roles != null && _roles.Object != null && _roles.Object.IsValid &&
                         _roles.TryGetRole(Object.InputAuthority, out PlayerRole role) && role == PlayerRole.Killer;

            // 쿨타임 종료 시 공격 가능 상태로 복귀
            if (!CanKill && CooldownTimer.Expired(Runner))
            {
                CanKill = true;
            }
        }

        /// <summary>공격 버튼 입력 시 로컬 클라가 호출 -> 서버가 최종 판정.</summary>
        [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
        public void RPC_RequestKill(NetworkId targetId)
        {
            ProcessKillRequest(targetId);
        }

        /// <summary>
        /// 실제 판정 로직 본체. RPC와 분리해둔 이유는 테스트 코드에서
        /// Input Authority 없이도 직접 호출해 로직만 검증할 수 있게 하기 위함.
        /// </summary>
        private void ProcessKillRequest(NetworkId targetId)
        {
            if (!Object.HasStateAuthority) return;

            // 1. 역할 확인
            if (_roles == null || !_roles.TryGetRole(Object.InputAuthority, out PlayerRole role) || role != PlayerRole.Killer)
            {
                RPC_KillFailed(Object.InputAuthority, "역할 아님");
                return;
            }

            // 2. 살인자 본인 생존 확인
            if (_health == null || !_health.CanAct)
            {
                RPC_KillFailed(Object.InputAuthority, "본인 사망 상태");
                return;
            }

            // 3. 쿨타임 확인
            if (!CanKill)
            {
                RPC_KillFailed(Object.InputAuthority, "쿨타임 중");
                return;
            }

            // 4. 대상 유효성 확인
            if (!Runner.TryFindObject(targetId, out var targetObj))
            {
                RPC_KillFailed(Object.InputAuthority, "대상 없음");
                return;
            }

            var targetHealth = targetObj.GetComponent<PlayerHealth>();
            if (targetObj == Object || targetHealth == null || !targetHealth.CanAct)
            {
                RPC_KillFailed(Object.InputAuthority, "대상 이미 사망");
                return;
            }

            // 5. 거리 확인
            float distance = Vector3.Distance(transform.position, targetObj.transform.position);
            if (distance > killRange)
            {
                RPC_KillFailed(Object.InputAuthority, "거리 초과");
                return;
            }

            if (_missions == null || !_missions.IsPersonalMissionCompleted(Object.InputAuthority))
            {
                RPC_KillFailed(Object.InputAuthority, "개인 미션 미완료");
                return;
            }

            // ---- 살인 승인 ----
            targetHealth.Kill(Object.InputAuthority);

            CanKill = false;
            CooldownTimer = TickTimer.CreateFromSeconds(Runner, killCooldownSeconds);

            SpawnEvidence(targetObj.transform.position);

            RPC_KillSuccess(Object.InputAuthority);
        }

        // 증거 프리팹 나오면 구현
        private void SpawnEvidence(Vector3 position)
        {
            Debug.Log($"[KillManager] {position} 위치에 증거 생성 (미구현) - 서버 전용");
        }

        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        private void RPC_KillSuccess([RpcTarget] PlayerRef killer)
        {
            Debug.Log($"[KillManager] 살인 성공");
            //공격 성공 연출 나오면 여기서 로컬 재생
        }

        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        private void RPC_KillFailed([RpcTarget] PlayerRef killer, string reason)
        {
            Debug.Log($"[KillManager] 살인 실패: {reason}");
            // 실패 UI/효과음 나오면 여기서 로컬 재생
        }

        private void Update()
        {
            if (Object == null || !Object.IsValid) return;
            if (!HasInputAuthority || _health == null || !_health.CanAct || _camera == null) return;
            if (!Input.GetMouseButtonDown(0) || !IsMurderer) return;
            if (!Physics.Raycast(_camera.transform.position, _camera.transform.forward,
                    out RaycastHit hit, killRange, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) return;
            PlayerHealth target = hit.collider.GetComponentInParent<PlayerHealth>();
            if (target != null && target != _health && target.Object != null && target.Object.IsValid)
                RPC_RequestKill(target.Object.Id);
        }
    }
}
