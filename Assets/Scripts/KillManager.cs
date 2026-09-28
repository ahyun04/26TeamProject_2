using Fusion;
using LockdownProtocol.Lobby;
using UnityEngine;

namespace LockdownProtocol.Networking
{
    [RequireComponent(typeof(PlayerHealth))]
    public class KillManager : NetworkBehaviour
    {
        [Header("Kill Settings")]
        [SerializeField, Min(0.1f)] private float killRange = 2f;
        [SerializeField, Min(0f)] private float killCooldownSeconds = 15f;
        [SerializeField] private KeyCode instantKillKey; //즉사 기술 입력

        [Networked] public NetworkBool IsMurderer { get; set; }
        [Networked] private TickTimer CooldownTimer { get; set; }
        [Networked] public NetworkBool CanKill { get; private set; }
        [Networked] private TickTimer attackTimer { get; set; } //일반 공격과 연속 공격 제한
        [Networked, OnChangedRender(nameof(playAttack))]
        private int attackSequence { get; set; } //호스트가 승인한 공격 횟수
        [Networked] private float attackDuration { get; set; } //공격 모션 재생 시간

        private PlayerHealth _health;
        private RoleAssignment _roles;
        private MissionSystem _missions;
        private PlayerItemController items; //장착 무기 진입점
        private PlayerCameraController cameraController; //호스트 기준 조준 정보
        private PlayerAnimation animationController; //캐릭터 공격 연출
        private PlayerInteraction interaction; //공격 시 미션 상호작용 중단
        private readonly RaycastHit[] rayHits = new RaycastHit[32]; //자기 충돌체를 제외한 근접 판정

        internal bool usesWeaponAttackInput() //좌클릭을 공격에 사용하는 상태
        {
            return Object != null && Object.IsValid && hasAssignedRole(out _) &&
                   items != null && items.tryGetEquippedWeapon(out _);
        }

        public override void Spawned()
        {
            _health = GetComponent<PlayerHealth>();
            items = GetComponent<PlayerItemController>();
            cameraController = GetComponent<PlayerCameraController>();
            animationController = GetComponent<PlayerAnimation>();
            interaction = GetComponent<PlayerInteraction>();
            findMatchSystems();
        }

        private void findMatchSystems() //같은 세션의 역할과 미션 연결
        {
            if (_roles == null)
                foreach (RoleAssignment roles in FindObjectsByType<RoleAssignment>(FindObjectsSortMode.None))
                    if (roles.Runner == Runner) { _roles = roles; break; }
            if (_missions == null)
                foreach (MissionSystem missions in FindObjectsByType<MissionSystem>(FindObjectsSortMode.None))
                    if (missions.Runner == Runner) { _missions = missions; break; }
        }

        public override void FixedUpdateNetwork()
        {
            if ((_roles == null || _missions == null) && Runner.Tick.Raw % 30 == 0) findMatchSystems();
            if (!HasStateAuthority) return;
            IsMurderer = hasKillerRole();
            CanKill = IsMurderer && _health != null && _health.CanAct &&
                      hasCompletedKillerMissions() && CooldownTimer.ExpiredOrNotRunning(Runner);
        }

        private bool hasAssignedRole(out PlayerRole role) //대기실과 역할 미배정 상태에서는 공격 차단
        {
            role = default;
            return _roles != null && _roles.Object != null && _roles.Object.IsValid && _roles.Initialized &&
                   _roles.TryGetRole(Object.InputAuthority, out role);
        }

        private bool hasKillerRole() //호스트의 실제 역할 배정 확인
        {
            return hasAssignedRole(out PlayerRole role) && role == PlayerRole.Killer;
        }

        private bool hasCompletedKillerMissions() //미배정 상태에서는 즉사 기술 잠금
        {
            return _missions != null && _missions.Object != null && _missions.Object.IsValid &&
                   _missions.IsPersonalMissionCompleted(Object.InputAuthority);
        }

        private bool canRequestAttack() //두 공격 경로에 공통으로 적용하는 호스트 검증
        {
            return HasStateAuthority && hasAssignedRole(out _) && _health != null && _health.CanAct &&
                   attackTimer.ExpiredOrNotRunning(Runner);
        }

        [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
        private void RPC_RequestAttack() //장착 무기로 일반 피해 적용
        {
            if (!canRequestAttack() || items == null || !items.tryGetEquippedWeapon(out ItemData weapon)) return;
            beginAttack(weapon.AttackInterval);
            if (tryGetAttackTarget(out PlayerHealth target))
                target.ApplyDamage(weapon.AttackDamage, Object.InputAuthority);
        }

        [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
        public void RPC_RequestKill(NetworkId targetId)
        {
            ProcessKillRequest(targetId);
        }

        private void ProcessKillRequest(NetworkId targetId)
        {
            if (!canRequestAttack() || !hasKillerRole() || !hasCompletedKillerMissions() ||
                !CooldownTimer.ExpiredOrNotRunning(Runner)) return;
            if (!tryGetAttackTarget(out PlayerHealth target) || target.Object.Id != targetId) return;

            float duration = items != null && items.tryGetEquippedWeapon(out ItemData weapon)
                ? weapon.AttackInterval : 1f;
            beginAttack(duration);
            CanKill = false;
            CooldownTimer = TickTimer.CreateFromSeconds(Runner, killCooldownSeconds);
            target.Kill(Object.InputAuthority);
        }

        private void beginAttack(float duration) //일반 공격과 즉사 모션의 중복 실행 방지
        {
            attackDuration = duration;
            attackTimer = TickTimer.CreateFromSeconds(Runner, duration);
            attackSequence++;
        }

        private bool tryGetAttackTarget(out PlayerHealth target) //시점과 벽을 포함한 호스트 근접 판정
        {
            target = null;
            if (cameraController == null || !cameraController.tryGetSimulationAimRay(out Ray ray)) return false;
            Vector3 eye = cameraController.getSimulationEyePosition();
            Vector3 cameraOffset = ray.origin - eye;
            if (cameraOffset.sqrMagnitude > 0.0001f &&
                tryGetFirstHit(new Ray(eye, cameraOffset.normalized), cameraOffset.magnitude, out _)) return false;
            if (!tryGetFirstHit(ray, killRange, out RaycastHit hit) || hit.collider == null) return false;
            target = hit.collider.GetComponentInParent<PlayerHealth>();
            return target != null && target != _health && target.Object != null && target.Object.IsValid &&
                   target.Runner == Runner && target.CanAct &&
                   Vector3.Distance(transform.position, target.transform.position) <= killRange;
        }

        private bool tryGetFirstHit(Ray ray, float distance, out RaycastHit closest) //가장 가까운 벽이나 플레이어 선택
        {
            closest = default;
            int count = Runner.GetPhysicsScene().Raycast(ray.origin, ray.direction, rayHits, distance,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            if (count == rayHits.Length) return true; //버퍼가 가득 차면 안전하게 공격 차단
            float nearest = float.PositiveInfinity;
            bool found = false;
            for (int i = 0; i < count; i++)
            {
                if (rayHits[i].collider.transform.IsChildOf(transform) || rayHits[i].distance >= nearest) continue;
                closest = rayHits[i];
                nearest = closest.distance;
                found = true;
            }
            return found;
        }

        private void playAttack() //호스트 승인 후 각 클라이언트에서 모션 재생
        {
            animationController?.playAttack(attackDuration);
            if (HasInputAuthority) interaction?.cancelInteraction();
        }

        private void Update()
        {
            if (Object == null || !Object.IsValid || !HasInputAuthority ||
                _health == null || !_health.CanAct || Cursor.lockState != CursorLockMode.Locked ||
                SessionDisconnectUIComponent.IsOpen ||
                (LobbyRoomUI.Instance != null && LobbyRoomUI.Instance.BlocksPlayerInput)) return;

            if (instantKillKey != KeyCode.None && Input.GetKeyDown(instantKillKey) && CanKill)
            {
                if (tryGetAttackTarget(out PlayerHealth target)) RPC_RequestKill(target.Object.Id);
                return;
            }
            if (Input.GetMouseButtonDown(0) && usesWeaponAttackInput())
                RPC_RequestAttack();
        }
    }
}
