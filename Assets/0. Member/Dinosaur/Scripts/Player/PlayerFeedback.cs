using Fusion;
using LockdownProtocol.Lobby;
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
        health.Died += handleDeath;
        health.revived += handleRevived;
        if (health.IsDead) handleDeath();
    }

    public override void Render() //Client에도 역할 정보가 도착한 뒤 한 번 안내
    {
        if (hud == null) return;

        bool canShowCrosshair = health != null && health.CanAct && !GameAudio.blocksPlayerInput &&
            !SessionDisconnectUIComponent.IsOpen &&
            (LobbyRoomUI.Instance == null || !LobbyRoomUI.Instance.BlocksPlayerInput); //현재 화면에서 조준 가능한 상태
        hud.setCrosshairVisible(canShowCrosshair);

        if (gameEndSystem != null && gameEndSystem.Object != null &&
            gameEndSystem.Object.IsValid && gameEndSystem.IsGameEnded)
        {
            hud.hideRole();
            hud.cancelDeath();
            return;
        }
        if (SessionDisconnectUIComponent.IsOpen) hud.cancelDeath();
        if (health != null && health.IsDead) return;
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

    internal void setVisible(bool visible) //관전자에게 체력과 스태미나 UI 숨김
    {
        if (hud != null) hud.setStatusVisible(visible);
    }

    private void handleDamage() //본인의 피격 화면 표시
    {
        if (hud != null && !health.IsDead) hud.showDamage();
    }

    private void handleDeath() //게임과 대기실에서 본인의 사망 화면 연출
    {
        if (hud == null || Runner.SceneManager == null) return;
        string sceneName = Runner.SceneManager.MainRunnerScene.name; //사망이 발생한 실제 네트워크 씬
        if (!NetworkBootstrap.isGameplayScene(sceneName) && sceneName != "StandBy") return;
        hud.showDeath(Runner);
    }

    private void handleRevived() //부활 시 남아 있는 로컬 연출 복원
    {
        if (hud == null) return;
        hud.setStatusVisible(true);
        if (Runner.SceneManager != null && Runner.SceneManager.MainRunnerScene.name == "StandBy")
            hud.showDeath(Runner); //같은 틱의 즉시 부활로 Died가 누락된 Client에도 연출 전달
        else hud.cancelDeath();
    }

    public override void Despawned(NetworkRunner runner, bool hasState) //이벤트와 로컬 UI 정리
    {
        if (health != null)
        {
            health.Damaged -= handleDamage;
            health.Died -= handleDeath;
            health.revived -= handleRevived;
        }
        if (hud != null) Destroy(hud.gameObject);
        hud = null;
        roleAssignment = null;
        gameEndSystem = null;
        roleShown = false;
    }
}
