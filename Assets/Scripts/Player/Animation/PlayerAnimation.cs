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

    [Networked, OnChangedRender(nameof(playThumbsUp))]
    private int thumbsUpSequence { get; set; } //호스트가 승인한 제스처 횟수

    private PlayerHealth health; //플레이어 생존 상태
    private PlayerItemController items; //기존 네트워크 아이템 장착 상태
    private PlayerLookVisualComponent lookVisual; //시선과 분리된 모델 방향의 이동 애니메이션
    private int holdLayerIndex = -1; //팔 전용 아이템 들기 레이어
    private float holdResumeTime; //공격과 제스처 진입 중 Hold 덮어쓰기 방지
    private static readonly int isHoldingHash = Animator.StringToHash("IsHolding"); //아이템을 들고 있는 상태
    private static readonly int isDeadHash = Animator.StringToHash("IsDead"); //사망 상태
    private static readonly int attackHash = Animator.StringToHash("Attack"); //공격 재생 요청
    private static readonly int attackSpeedHash = Animator.StringToHash("AttackSpeed"); //공격 재생 배속

    internal void playAttack(float duration) //승인된 공격 간격에 맞춰 모션 재생
    {
        if (animator == null || (health != null && health.IsDead)) return;
        animator.SetFloat(attackSpeedHash, attackClip != null ? attackClip.length / Mathf.Max(0.01f, duration) : 1f);
        holdResumeTime = Mathf.Max(holdResumeTime, Time.time + Mathf.Max(0f, duration));
        if (holdLayerIndex >= 0) animator.SetLayerWeight(holdLayerIndex, 0f);
        animator.SetTrigger(attackHash);
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
        if (animator != null) holdLayerIndex = animator.GetLayerIndex("Item Hold");
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
    }

    private void updateHold() //장착 상태는 기존 동기화 값을 사용하고 팔 모션만 로컬 표시
    {
        if (holdLayerIndex < 0) return;
        bool isHolding = health != null && health.CanAct && items != null &&
                         items.CurrentItemObject != null && items.CurrentItemObject.IsValid; //미션 도구도 동일하게 들기 적용
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
        if (health != null && health.IsDead)
            return;

        Vector2 localVelocity = lookVisual != null ? lookVisual.getBodyLocalVelocity() : playerMovement.LocalMoveVelocity;

        animator.SetFloat(MoveXHash, localVelocity.x, speedDampTime, Time.deltaTime);
        animator.SetFloat(MoveYHash, localVelocity.y, speedDampTime, Time.deltaTime);
        animator.SetBool(IsGroundedHash, playerMovement.IsGrounded);
    }
}
