using System.Collections.Generic;
using Fusion;
using LockdownProtocol.Lobby;
using UnityEngine;

[DefaultExecutionOrder(-100)]
[RequireComponent(typeof(NetworkObject))]
public class Cctv : NetworkBehaviour, ITargetable
{
    [SerializeField] private CctvDisplayComponent display; //모니터와 구역별 카메라 표시
    [SerializeField] private Transform interactionPoint; //접근과 시야를 검사할 모니터 위치
    [SerializeField, Min(0.1f)] private float interactionDistance = 3f; //관제 장치를 이용할 최대 거리
    [SerializeField, Min(0.05f)] private float switchInterval = 0.15f; //연속 화면 전환 제한
    [SerializeField] private KeyCode interactKey = KeyCode.F; //관제 시작과 종료 키
    [SerializeField] private KeyCode exitKey = KeyCode.Escape; //관제 종료 키

    [Networked] private int cameraIndex { get; set; } //모든 참가자가 보는 모니터 화면
    [Networked, Capacity(RoomManager.AbsoluteMaxPlayers)]
    private NetworkDictionary<PlayerRef, byte> viewers => default; //호스트가 승인한 현재 이용자
    [Networked] private TickTimer switchTimer { get; set; } //호스트의 중복 전환 제한

    private static readonly List<Cctv> consoles = new(); //같은 세션의 관제 장치 검색
    private static Cctv localConsole; //본인이 사용 중이거나 응답을 기다리는 장치
    private static int exitFrame = -1; //종료 키가 같은 프레임의 설정창까지 열지 않도록 차단
    private readonly List<PlayerRef> departed = new(); //호스트가 제거할 이용자 임시 목록
    private readonly RaycastHit[] sightHits = new RaycastHit[32]; //벽 뒤에서 이용하는 요청 차단
    private PlayerHealth localHealth; //피격·사망·탈출 시 이용 종료
    private bool spawned; //Spawn 이전 네트워크 속성 접근 방지
    private bool awaitingEnter; //서버 응답 전 로컬 이동 입력 차단
    private float requestTime; //응답 누락 시 로컬 잠금 복구 기준

    public NetworkObject TargetObject => Object;
    internal int selectedCamera => spawned ? cameraIndex : 0; //모니터가 표시할 현재 구역
    internal int viewerCount => spawned ? viewers.Count : 0; //표시등에 반영할 이용자 수
    internal Transform promptPoint => interactionPoint != null ? interactionPoint : transform; //조준 안내 위치
    internal string interactionHint => $"[{interactKey}] CCTV 보기"; //현재 설정 키와 일치하는 진입 안내
    internal static bool blocksLocalInput => localConsole != null || exitFrame == Time.frameCount; //기존 입력 제한 경로와 연결

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void resetLocalState() //도메인 재시작 없이 재생해도 이전 이용 상태 제거
    {
        consoles.Clear();
        localConsole = null;
        exitFrame = -1;
    }

    internal static bool isPlayerViewing(NetworkRunner runner, PlayerRef player) //호스트와 예측 클라이언트의 행동 제한
    {
        if (runner == null || player.IsNone) return false;
        foreach (Cctv console in consoles)
            if (console != null && console.spawned && console.Object != null && console.Object.IsValid &&
                console.Runner == runner && console.viewers.ContainsKey(player)) return true;
        return false;
    }

    public override void Spawned() //씬의 공유 화면과 로컬 영상 표시 초기화
    {
        if (HasStateAuthority)
        {
            cameraIndex = 0;
            viewers.Clear();
        }
        spawned = true;
        if (!consoles.Contains(this)) consoles.Add(this);
        if (display == null) display = GetComponent<CctvDisplayComponent>();
        display?.initialize(this);
    }

    internal bool canUseLocally() //살아 있는 본인이 장치에 접근했는지 확인
    {
        Transform listener = PlayerCameraController.LocalListenerTransform; //현재 본인의 실제 시점
        PlayerHealth health = listener != null ? listener.GetComponentInParent<PlayerHealth>() : null;
        PlayerInteraction interaction = health != null ? health.GetComponent<PlayerInteraction>() : null;
        return spawned && Object != null && Object.IsValid && Runner.IsRunning && health != null &&
            health.Runner == Runner && health.HasInputAuthority && health.CanAct &&
            Vector3.Distance(health.transform.position, promptPoint.position) <= interactionDistance &&
            (interaction == null || !interaction.isInteracting);
    }

    internal bool requestEnter() //입력은 본인만 요청하고 이용 허가는 호스트가 결정
    {
        if (localConsole != null || !canUseLocally() || GameAudio.blocksMenuInput || SessionDisconnectUIComponent.IsOpen) return false;
        localConsole = this;
        awaitingEnter = true;
        requestTime = Time.unscaledTime;
        localHealth = PlayerCameraController.LocalListenerTransform.GetComponentInParent<PlayerHealth>();
        localHealth.Damaged += requestExit;
        localHealth.Died += requestExit;
        localHealth.Escaped += requestExit;
        RPC_RequestEnter();
        return true;
    }

    internal void requestExit() //본인 입력을 복구하고 호스트의 이용자 목록에서도 제거 요청
    {
        if (localConsole != this) return;
        clearLocalView();
        if (spawned && Object != null && Object.IsValid && Runner.IsRunning) RPC_RequestExit();
    }

    private void clearLocalView() //종료·씬 전환·연결 해제에 공통인 로컬 정리
    {
        if (localHealth != null)
        {
            localHealth.Damaged -= requestExit;
            localHealth.Died -= requestExit;
            localHealth.Escaped -= requestExit;
        }
        localHealth = null;
        awaitingEnter = false;
        if (localConsole != this) return;
        localConsole = null;
        exitFrame = Time.frameCount;
    }

    private void Update() //설정창보다 먼저 종료 키를 소비하고 관제 키만 허용
    {
        if (!spawned || Object == null || !Object.IsValid) return;
        if (localConsole == this)
        {
            if (!Runner.IsRunning || SessionDisconnectUIComponent.IsOpen || GameAudio.blocksMenuInput ||
                localHealth == null || !localHealth.canActWithoutCctv || localHealth.isStunned || Input.GetKeyDown(exitKey) || Input.GetKeyDown(interactKey))
            { requestExit(); return; }
            if (viewers.ContainsKey(Runner.LocalPlayer)) awaitingEnter = false;
            else if (!awaitingEnter) { clearLocalView(); return; }
            if (awaitingEnter)
            {
                if (Time.unscaledTime - requestTime >= 3f) requestExit();
                return;
            }
            if (Input.GetKeyDown(KeyCode.LeftArrow)) RPC_RequestSwitch(-1);
            else if (Input.GetKeyDown(KeyCode.RightArrow)) RPC_RequestSwitch(1);
            return;
        }
        if (localConsole != null || GameAudio.blocksPlayerInput || SessionDisconnectUIComponent.IsOpen ||
            Cursor.lockState != CursorLockMode.Locked || !Input.GetKeyDown(interactKey) || !canUseLocally()) return;
        PlayerTargetDetector detector = PlayerCameraController.LocalListenerTransform.GetComponentInParent<PlayerTargetDetector>(); //기존 조준 대상 탐지
        if (detector != null && ReferenceEquals(detector.DetectNow(), this)) requestEnter();
    }

    private bool canWatch(PlayerRef actor) //접속·생존·거리·벽을 포함한 서버의 실제 이용 자격
    {
        if (actor.IsNone || !Runner.IsRunning || display == null || display.cameraCount == 0 ||
            !NetworkBootstrap.isGameplayScene(gameObject.scene.name)) return false;
        NetworkObject player = Runner.GetPlayerObject(actor); //요청자가 소유한 실제 플레이어
        PlayerHealth health = player != null && player.IsValid ? player.GetComponent<PlayerHealth>() : null;
        if (health == null || player.InputAuthority != actor || !health.canActWithoutCctv || health.isStunned ||
            Vector3.Distance(player.transform.position, promptPoint.position) > interactionDistance) return false;
        PlayerCameraController camera = player.GetComponent<PlayerCameraController>(); //호스트가 확정한 눈높이
        if (camera == null) return false;
        Vector3 eye = camera.getSimulationEyePosition(); //본인의 충돌체 내부에서 시작하는 검사점
        Vector3 delta = promptPoint.position - eye; //모니터까지의 실제 방향
        if (delta.sqrMagnitude <= 0.0001f) return true;
        int count = Runner.GetPhysicsScene().Raycast(eye, delta.normalized, sightHits, delta.magnitude, 1, QueryTriggerInteraction.Ignore);
        if (count == sightHits.Length) return false;
        for (int i = 0; i < count; i++)
            if (!sightHits[i].collider.transform.IsChildOf(transform) &&
                sightHits[i].collider.GetComponentInParent<PlayerHealth>() == null) return false;
        return true;
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority, HostMode = RpcHostMode.SourceIsHostPlayer)]
    private void RPC_RequestEnter(RpcInfo info = default) //호스트가 중복 없는 이용자 목록에 등록
    {
        if (!HasStateAuthority || info.Source.IsNone) return;
        bool accepted = canWatch(info.Source) && (!isPlayerViewing(Runner, info.Source) || viewers.ContainsKey(info.Source));
        if (accepted && !viewers.ContainsKey(info.Source)) viewers.Add(info.Source, 1);
        RPC_EnterResult(info.Source, accepted);
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_EnterResult([RpcTarget] PlayerRef target, bool accepted) //거절 시 로컬 입력 잠금을 즉시 복구
    {
        if (!accepted && localConsole == this) clearLocalView();
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority, HostMode = RpcHostMode.SourceIsHostPlayer)]
    private void RPC_RequestExit(RpcInfo info = default) //다른 이용자의 상태는 변경하지 않음
    {
        if (HasStateAuthority) viewers.Remove(info.Source);
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority, HostMode = RpcHostMode.SourceIsHostPlayer)]
    private void RPC_RequestSwitch(int direction, RpcInfo info = default) //호스트가 허가한 이용자만 공유 화면 전환
    {
        if (!HasStateAuthority || (direction != -1 && direction != 1) || !viewers.ContainsKey(info.Source) ||
            !canWatch(info.Source) || !switchTimer.ExpiredOrNotRunning(Runner)) return;
        cameraIndex = (cameraIndex + direction + display.cameraCount) % display.cameraCount;
        switchTimer = TickTimer.CreateFromSeconds(Runner, switchInterval);
    }

    public override void FixedUpdateNetwork() //사망·탈출·거리 이탈·연결 종료 시 서버에서 이용 상태 해제
    {
        if (!HasStateAuthority) return;
        departed.Clear();
        foreach (var entry in viewers)
            if (!canWatch(entry.Key)) departed.Add(entry.Key);
        foreach (PlayerRef actor in departed) viewers.Remove(actor);
    }

    public override void Despawned(NetworkRunner runner, bool hasState) //씬을 넘어 이용자와 로컬 잠금이 남지 않게 정리
    {
        if (localConsole == this) clearLocalView();
        consoles.Remove(this);
        spawned = false;
        display?.release();
    }

    private void OnDisable() //오브젝트 비활성화와 종료 시 로컬 잠금 복구
    {
        if (localConsole == this) clearLocalView();
        if (spawned && Object != null && Object.IsValid && HasStateAuthority) viewers.Clear();
        consoles.Remove(this);
    }

    private void OnEnable() //임시 비활성화 뒤에도 같은 세션의 이용 제한 검색에 다시 등록
    {
        if (spawned && !consoles.Contains(this)) consoles.Add(this);
    }
}
