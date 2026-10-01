using Fusion;
using UnityEngine;

[RequireComponent(typeof(PlayerHealth), typeof(PlayerStamina))]
public class PlayerFeedback : NetworkBehaviour
{
    [SerializeField] private PlayerHudUIComponent hudPrefab; //로컬 플레이어 표시 프리팹

    private PlayerHealth health; //피격 알림을 전달하는 체력
    private PlayerHudUIComponent hud; //본인에게만 생성하는 UI
    private RoleAssignment roleAssignment; //현재 게임의 역할 배정
    private GameEndSystem gameEndSystem; //역할 안내를 닫을 게임 종료 상태
    private bool roleShown; //이번 게임의 역할 안내 표시 여부

    public override void Spawned() //로컬 플레이어의 표시와 피격 알림 연결
    {
        if (!HasInputAuthority || hudPrefab == null) return;

        health = GetComponent<PlayerHealth>();
        roleAssignment = FindFirstObjectByType<RoleAssignment>();
        gameEndSystem = FindFirstObjectByType<GameEndSystem>();
        hud = Instantiate(hudPrefab);
        hud.initialize(health, GetComponent<PlayerStamina>());
        health.Damaged += handleDamage;
    }

    public override void Render() //Client에도 역할 정보가 도착한 뒤 한 번 안내
    {
        if (hud == null) return;

        if (gameEndSystem != null && gameEndSystem.Object != null &&
            gameEndSystem.Object.IsValid && gameEndSystem.IsGameEnded)
        {
            hud.hideRole();
            return;
        }
        if (roleShown) return;

        if (roleAssignment == null || roleAssignment.Object == null || !roleAssignment.Object.IsValid ||
            roleAssignment.Runner != Runner || !roleAssignment.Initialized ||
            !roleAssignment.TryGetRole(Object.InputAuthority, out PlayerRole role)) return;

        hud.showRole(role);
        roleShown = true;
    }

    internal Quaternion getDamageShakeRotation() //카메라에 로컬 피격 흔들림만 전달
    {
        return hud != null ? hud.getDamageShakeRotation() : Quaternion.identity;
    }

    private void handleDamage() //본인의 피격 화면 표시
    {
        if (hud != null) hud.showDamage();
    }

    public override void Despawned(NetworkRunner runner, bool hasState) //이벤트와 로컬 UI 정리
    {
        if (health != null) health.Damaged -= handleDamage;
        if (hud != null) Destroy(hud.gameObject);
        hud = null;
        roleAssignment = null;
        gameEndSystem = null;
        roleShown = false;
    }
}
