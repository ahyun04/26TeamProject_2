using Fusion;
using LockdownProtocol.Lobby;
using UnityEngine;

public class PlayerAnimation : NetworkBehaviour
{
    [Header("참조")]
    [SerializeField] private Animator animator;
    [SerializeField] private PlayerMovement playerMovement;

    [Header("애니메이션 설정")]
    [SerializeField] private float speedDampTime = 0.1f;
    [SerializeField] private AnimationClip attackClip; //공격 속도 계산에 사용하는 클립

    [Header("미션 상호작용 (14b)")]
    [SerializeField] private bool showMissionInFirstPerson = true; //버튼·밸브 동작을 본인 1인칭 팔에도 표시

    [Networked, OnChangedRender(nameof(playThumbsUp))]
    private int thumbsUpSequence { get; set; } //호스트가 승인한 제스처 횟수

    private PlayerHealth health; //플레이어 생존 상태
    private PlayerItemController items; //기존 네트워크 아이템 장착 상태
    private PlayerLookVisualComponent lookVisual; //시선과 분리된 모델 방향의 이동 애니메이션
    private PlayerFirstPersonItemView firstPersonView; //본인에게 표시할 제스처 진입점
    private int holdLayerIndex = -1; //팔 전용 아이템 들기 레이어
    private float holdResumeTime; //공격과 제스처 진입 중 Hold 덮어쓰기 방지
    private static readonly int isHoldingHash = Animator.StringToHash("IsHolding"); //아이템을 들고 있는 상태
    private static readonly int holdStyleHash = Animator.StringToHash("HoldStyle"); //한손과 양손 들기 모션 선택
    private static readonly int isDeadHash = Animator.StringToHash("IsDead"); //사망 상태
    private static readonly int attackHash = Animator.StringToHash("Attack"); //공격 재생 요청
    private static readonly int attackSpeedHash = Animator.StringToHash("AttackSpeed"); //공격 재생 배속
    private int missionLayerIndex = -1; //미션 버튼·밸브 상체 레이어 (14b — 메뉴 SuHan/Add Mission Interaction Animations 로 추가)
    private float missionPressLength = 1.43f; //버튼 동작 길이 (컨트롤러 클립에서 다시 읽음)
    private float missionPressEndTime; //버튼 동작이 끝나는 시각
    private bool missionTurning; //밸브·안테나를 돌리는 중
    private float missionLayerWeight; //동작 중에만 1로 올리는 레이어 무게
    private bool missionArmsShown; //본인 1인칭 팔에 알린 동작 상태
    private bool missionArmsTurning; //본인 1인칭 팔에 알린 돌리기 여부 (버튼은 손끝, 돌리기는 양손 가운데를 조준점에)
    private const float missionFadeSeconds = 0.1f; //레이어를 켜고 끄는 시간
    private const string missionPressClipName = "ANM_Player_Interaction_button"; //버튼 동작 클립 이름
    private const float missionRestartSeconds = 0.05f; //다시 누를 때 처음으로 돌아가는 짧은 섞임
    private static readonly int missionPressStateHash = Animator.StringToHash("Mission Press"); //버튼 동작 상태 (누를 때마다 처음부터)
    private static readonly int missionTurningHash = Animator.StringToHash("MissionTurning"); //돌리는 동안 반복

    internal void playAttack(float duration) //승인된 공격 간격에 맞춰 모션 재생
    {
        if (animator == null || (health != null && health.IsDead)) return;
        if (HasInputAuthority) firstPersonView?.stopGesture();
        animator.SetFloat(attackSpeedHash, attackClip != null ? attackClip.length / Mathf.Max(0.01f, duration) : 1f);
        holdResumeTime = Mathf.Max(holdResumeTime, Time.time + Mathf.Max(0f, duration));
        if (holdLayerIndex >= 0) animator.SetLayerWeight(holdLayerIndex, 0f);
        animator.SetTrigger(attackHash);
    }

    internal void playMissionPress() //미션 버튼·레버를 누를 때마다 처음부터 동작 (14b — 재생 중에 다시 눌러도 다시 시작)
    {
        if (animator == null || missionLayerIndex < 0 || (health != null && !health.CanAct)) return;
        if (missionTurning) return;
        missionPressEndTime = Time.time + missionPressLength;
        animator.CrossFadeInFixedTime(missionPressStateHash, missionRestartSeconds, missionLayerIndex, 0f);
    }

    internal void setMissionTurning(bool turning) //밸브·안테나를 돌리는 동안 반복 재생 (14b)
    {
        if (animator == null || missionLayerIndex < 0) return;
        missionTurning = turning && (health == null || health.CanAct);
        animator.SetBool(missionTurningHash, missionTurning);
    }

    private static readonly int MoveXHash = Animator.StringToHash("MoveX");

    private static readonly int MoveYHash = Animator.StringToHash("MoveY");

    private static readonly int IsGroundedHash = Animator.StringToHash("IsGrounded");

    private static readonly int IsThumbsUp = Animator.StringToHash("IsThumbsUp");

    private void Awake()
    {
        if (animator == null)
            animator = GetComponentInChildren<Animator>(true);

        if (playerMovement == null)
            playerMovement = GetComponent<PlayerMovement>();

        health = GetComponent<PlayerHealth>();
        items = GetComponent<PlayerItemController>();
        lookVisual = GetComponent<PlayerLookVisualComponent>();
        firstPersonView = GetComponent<PlayerFirstPersonItemView>();
        if (animator != null) holdLayerIndex = animator.GetLayerIndex("Item Hold");
        if (animator != null) missionLayerIndex = animator.GetLayerIndex("Mission Interaction");
        readMissionPressLength();
    }

    private void readMissionPressLength() //버튼 동작 길이를 컨트롤러 클립에서 읽음 (14b — 없으면 기본값)
    {
        if (animator == null || animator.runtimeAnimatorController == null) return;
        foreach (AnimationClip clip in animator.runtimeAnimatorController.animationClips)
        {
            if (clip == null || clip.name != missionPressClipName) continue;
            missionPressLength = clip.length;
            return;
        }
    }

    private void updateMissionLayer() //동작 중에만 상체 레이어를 켜고 본인 팔에 알림 (14b — 평소 무게 0 이라 기존 모션 그대로)
    {
        if (missionLayerIndex < 0) return;
        bool canAct = health == null || health.CanAct;
        if (!canAct && missionTurning) setMissionTurning(false);
        bool active = canAct && (missionTurning || Time.time < missionPressEndTime);
        missionLayerWeight = Mathf.MoveTowards(missionLayerWeight, active ? 1f : 0f, Time.deltaTime / missionFadeSeconds);
        animator.SetLayerWeight(missionLayerIndex, missionLayerWeight);
        bool showArms = active && showMissionInFirstPerson && HasInputAuthority;
        if (showArms == missionArmsShown && missionTurning == missionArmsTurning) return;
        missionArmsShown = showArms;
        missionArmsTurning = missionTurning;
        firstPersonView?.setMissionAction(showArms, missionTurning);
    }

    private void Update()
    {
        if (Object == null || !Object.IsValid || !HasInputAuthority || !canPlayThumbsUp() ||
            Cursor.lockState != CursorLockMode.Locked || SessionDisconnectUIComponent.IsOpen || GameAudio.blocksPlayerInput ||
            (LobbyRoomUI.Instance != null && (LobbyRoomUI.Instance.BlocksPlayerInput ||
                Input.GetKeyDown(KeyCode.Escape))))
            return;

        if (Input.GetKeyDown(KeyCode.Z))
            RPC_RequestThumbsUp();
    }

    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    private void RPC_RequestThumbsUp() //본인 제스처 요청을 호스트가 확인
    {
        if (!HasStateAuthority || !canPlayThumbsUp()) return;
        thumbsUpSequence++;
    }

    private bool canPlayThumbsUp() //사망·탈출·결과·씬 전환 중 제스처 차단
    {
        if (animator == null || health == null || !health.CanAct) return false;
        RoomManager room = RoomManager.Instance; //이 세션의 게임 시작 상태
        return room == null || room.Object == null || !room.Object.IsValid || room.Runner != Runner ||
            room.CurrentRoomState != RoomManager.RoomState.Starting;
    }

    private void playThumbsUp() //승인된 제스처를 각 클라이언트에서 한 번 재생
    {
        if (!canPlayThumbsUp()) return;
        holdResumeTime = Mathf.Max(holdResumeTime, Time.time + 0.25f);
        if (holdLayerIndex >= 0) animator.SetLayerWeight(holdLayerIndex, 0f);
        animator.SetTrigger(IsThumbsUp);
        if (HasInputAuthority) firstPersonView?.showThumbsUp();
    }

    private void updateHold() //장착 상태는 기존 동기화 값을 사용하고 팔 모션만 로컬 표시
    {
        if (holdLayerIndex < 0) return;
        bool isHolding = health != null && health.CanAct && items != null &&
                         items.CurrentItemObject != null && items.CurrentItemObject.IsValid; //미션 도구도 동일하게 들기 적용
        ItemBase heldItem = isHolding ? items.CurrentItemObject.GetComponentInChildren<ItemBase>(true) : null; //장착한 아이템의 모션 설정
        animator.SetFloat(holdStyleHash, heldItem != null && heldItem.Data != null && heldItem.Data.usesTwoHandedHold ? 1f : 0f);
        AnimatorStateInfo current = animator.GetCurrentAnimatorStateInfo(0); //현재 기본 모션
        AnimatorStateInfo next = animator.IsInTransition(0) ? animator.GetNextAnimatorStateInfo(0) : default; //전환 대상 모션
        bool actionPlaying = current.shortNameHash == attackHash || next.shortNameHash == attackHash ||
                             current.shortNameHash == IsThumbsUp || next.shortNameHash == IsThumbsUp; //공격과 제스처 우선
        animator.SetBool(isHoldingHash, isHolding);
        animator.SetLayerWeight(holdLayerIndex, isHolding && !actionPlaying && Time.time >= holdResumeTime ? 1f : 0f);
    }

    public override void Render()
    {
        if (animator == null || playerMovement == null)
            return;

        animator.SetBool(isDeadHash, health != null && health.IsDead);
        updateHold();
        updateMissionLayer();
        if (health != null && health.IsDead)
            return;

        Vector2 localVelocity = lookVisual != null ? lookVisual.getBodyLocalVelocity() : playerMovement.LocalMoveVelocity;

        animator.SetFloat(MoveXHash, localVelocity.x, speedDampTime, Time.deltaTime);
        animator.SetFloat(MoveYHash, localVelocity.y, speedDampTime, Time.deltaTime);
        animator.SetBool(IsGroundedHash, playerMovement.IsGrounded);
    }
}
