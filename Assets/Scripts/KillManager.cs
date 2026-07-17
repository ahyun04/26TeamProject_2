using Fusion;
using UnityEngine;
using HyunWoo;
namespace LockdownProtocol.Networking
{
    [RequireComponent(typeof(PlayerHealth))]
    public class KillManager : NetworkBehaviour
    {
        [Header("Kill Settings")]
        [SerializeField] private float killRange = 2f;
        [SerializeField] private float killCooldownSeconds = 15f;

        [Header("Dependencies")]
        [SerializeField] private MissionSystem missionSystem;

        // 역할 배정 시스템 나오면 교체
        [Networked] public NetworkBool IsMurderer { get; set; }

        [Networked] private TickTimer CooldownTimer { get; set; }
        [Networked] public NetworkBool CanKill { get; private set; } = true;

        private PlayerHealth _health;

        public override void Spawned()
        {
            _health = GetComponent<PlayerHealth>();
        }

        public override void FixedUpdateNetwork()
        {
            if (!Object.HasStateAuthority) return;

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
            if (!IsMurderer)
            {
                RPC_KillFailed(Object.InputAuthority, "역할 아님");
                return;
            }

            // 2. 살인자 본인 생존 확인
            if (_health.IsDead)
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
            if (targetHealth == null || targetHealth.IsDead)
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

            // 6. 미션 완료 확인
            // 현우가 미션 시스템 재구성 사유로 주석 처리했음
            //if (missionSystem != null && !missionSystem.IsPersonalMissionComplete(Object.InputAuthority))
            //{
            //    RPC_KillFailed(Object.InputAuthority, "미션 미완료");
            //    return;
            //}

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

        // ================== 테스트용, 나중에 삭제 ==================
        private void Update()
        {
            if (Object == null || !Object.IsValid) return;
            if (!Object.HasStateAuthority) return;

            if (Input.GetKeyDown(KeyCode.M))
            {
                IsMurderer = !IsMurderer;
                Debug.Log($"[KillManager] IsMurderer = {IsMurderer}");
            }

            if (Input.GetKeyDown(KeyCode.L))
            {
                ProcessKillRequest(Object.Id);
            }
        }
    }
}