using Fusion;
using UnityEngine;

public class PlayerAnimation : NetworkBehaviour
{
    [Header("참조")]
    [SerializeField] private Animator animator;
    [SerializeField] private PlayerMovement playerMovement;

    [Header("애니메이션 설정")]
    [SerializeField] private float speedDampTime = 0.1f;
    [SerializeField] private AnimationClip attackClip; //공격 속도 계산에 사용하는 클립

    private PlayerHealth health; //플레이어 생존 상태
    private static readonly int isDeadHash = Animator.StringToHash("IsDead"); //사망 상태
    private static readonly int attackHash = Animator.StringToHash("Attack"); //공격 재생 요청
    private static readonly int attackSpeedHash = Animator.StringToHash("AttackSpeed"); //공격 재생 배속

    internal void playAttack(float duration) //승인된 공격 간격에 맞춰 모션 재생
    {
        if (animator == null || (health != null && health.IsDead)) return;
        animator.SetFloat(attackSpeedHash, attackClip != null ? attackClip.length / Mathf.Max(0.01f, duration) : 1f);
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
    }

    private void Update()
    {
        if (Object == null || !Object.IsValid || !HasInputAuthority || animator == null ||
            (health != null && !health.CanAct))
            return;

        if (Input.GetKeyDown(KeyCode.Z))
            animator.SetTrigger(IsThumbsUp);
    }

    public override void Render()
    {
        if (animator == null || playerMovement == null)
            return;

        animator.SetBool(isDeadHash, health != null && health.IsDead);
        if (health != null && health.IsDead)
            return;

        Vector2 localVelocity = playerMovement.LocalMoveVelocity;

        animator.SetFloat(MoveXHash, localVelocity.x, speedDampTime, Time.deltaTime);
        animator.SetFloat(MoveYHash, localVelocity.y, speedDampTime, Time.deltaTime);
        animator.SetBool(IsGroundedHash, playerMovement.IsGrounded);
    }
}
