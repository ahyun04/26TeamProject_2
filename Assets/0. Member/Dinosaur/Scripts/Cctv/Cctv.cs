using System;
using Fusion;
using LockdownProtocol.Lobby;
using UnityEngine;

[RequireComponent(typeof(NetworkObject))]
public class Cctv : NetworkBehaviour
{
    [SerializeField] private CctvDisplayComponent display; //모니터와 구역별 카메라
    [SerializeField] private CctvButtonComponent[] buttons; //좌우 카메라 버튼과 번호별 방화문 버튼
    [SerializeField, Min(0.1f)] private float interactionDistance = 3f; //서버에서 검증할 조작 거리
    [SerializeField, Min(0.05f)] private float switchInterval = 0.15f; //버튼별 중복 입력 제한

    [Networked] private int cameraIndex { get; set; } //모든 모니터에 표시할 현재 카메라

    private readonly RaycastHit[] sightHits = new RaycastHit[32]; //벽 너머에서 조작하는 요청 검사
    private TickTimer[] pressTimers; //호스트의 버튼별 재입력 제한
    private bool spawned; //스폰 전 네트워크 상태 읽기 방지

    internal int selectedCamera => spawned ? cameraIndex : 0;

    public override void Spawned() //공유 카메라와 로컬 모니터 초기화
    {
        if (HasStateAuthority) cameraIndex = 0;
        pressTimers = new TickTimer[buttons != null ? buttons.Length : 0];
        spawned = true;
        display?.initialize(this);
    }

    internal bool canUseLocally(CctvButtonComponent button) //입력 권한과 현재 상호작용 상태 확인
    {
        if (!isReady() || button == null || Array.IndexOf(buttons, button) < 0 ||
            GameAudio.blocksPlayerInput || SessionDisconnectUIComponent.IsOpen || Cursor.lockState != CursorLockMode.Locked ||
            (LobbyRoomUI.Instance != null && LobbyRoomUI.Instance.BlocksPlayerInput)) return false;

        Transform listener = PlayerCameraController.LocalListenerTransform;
        PlayerHealth health = listener != null ? listener.GetComponentInParent<PlayerHealth>() : null;
        PlayerInteraction interaction = health != null ? health.GetComponent<PlayerInteraction>() : null;
        return health != null && health.Runner == Runner && health.HasInputAuthority && health.CanAct &&
               Vector3.Distance(health.transform.position, button.aimPoint) <= interactionDistance &&
               (interaction == null || !interaction.isInteracting) &&
               hasLineOfSight(listener.position, button);
    }

    internal bool requestPress(CctvButtonComponent button) //선택한 버튼의 번호만 호스트에게 전달
    {
        if (!canUseLocally(button)) return false;
        RPC_RequestPress(Array.IndexOf(buttons, button));
        return true;
    }

    private bool isReady() //씬 로드와 세션 종료 사이의 입력 차단
    {
        return spawned && Object != null && Object.IsValid && Runner.IsRunning && buttons != null;
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority, HostMode = RpcHostMode.SourceIsHostPlayer)]
    private void RPC_RequestPress(int index, RpcInfo info = default) //호스트가 버튼 입력을 검증하고 공유 상태 변경
    {
        if (!HasStateAuthority || !isReady() || index < 0 || index >= buttons.Length) return;

        CctvButtonComponent button = buttons[index];
        if (button == null || !pressTimers[index].ExpiredOrNotRunning(Runner) ||
            !canPress(info.Source, button)) return;

        if (button.direction != 0)
        {
            if (display == null || display.cameraCount == 0) return;
            cameraIndex = (cameraIndex + button.direction + display.cameraCount) % display.cameraCount;
        }
        else if (button.door != null && button.door.Runner == Runner)
        {
            button.door.toggle();
        }
        else return;

        pressTimers[index] = TickTimer.CreateFromSeconds(Runner, switchInterval);
    }

    private bool canPress(PlayerRef actor, CctvButtonComponent button) //요청자의 소유권, 생존, 거리와 시야 확인
    {
        if (actor.IsNone || !NetworkBootstrap.isGameplayScene(gameObject.scene.name)) return false;

        NetworkObject player = Runner.GetPlayerObject(actor);
        if (player == null || !player.IsValid || player.InputAuthority != actor ||
            !player.TryGetComponent(out PlayerHealth health) || !health.CanAct ||
            Vector3.Distance(player.transform.position, button.aimPoint) > interactionDistance ||
            !player.TryGetComponent(out PlayerCameraController camera)) return false;

        return hasLineOfSight(camera.getSimulationEyePosition(), button);
    }

    private bool hasLineOfSight(Vector3 eye, CctvButtonComponent button) //로컬 조준 표시와 호스트 요청에 공통 적용
    {
        Vector3 delta = button.aimPoint - eye;
        if (delta.sqrMagnitude <= 0.0001f) return true;

        int count = Runner.GetPhysicsScene().Raycast(eye, delta.normalized, sightHits, delta.magnitude, 1, QueryTriggerInteraction.Ignore);
        if (count == sightHits.Length) return false;
        for (int i = 0; i < count; i++)
        {
            Collider obstacle = sightHits[i].collider;
            if (!obstacle.transform.IsChildOf(transform) && obstacle.GetComponentInParent<PlayerHealth>() == null)
                return false;
        }
        return true;
    }

    public override void Despawned(NetworkRunner runner, bool hasState) //씬 전환 시 모니터 렌더 리소스 해제
    {
        spawned = false;
        display?.release();
    }
}
