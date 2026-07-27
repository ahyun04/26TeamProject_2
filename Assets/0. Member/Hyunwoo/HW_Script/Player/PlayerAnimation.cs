using Fusion;
using UnityEngine;

public class PlayerAnimation : NetworkBehaviour
{
    [Header("참조")]
    [SerializeField] private Animator animator;
    [SerializeField] private PlayerMovement playerMovement;

    [Header("애니메이션 설정")]
    [SerializeField] private float speedDampTime = 0.1f;

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
    }

    private void Update()
    {
        if (!HasInputAuthority || animator == null)
            return;

        if (Input.GetKeyDown(KeyCode.Z))
            animator.SetTrigger(IsThumbsUp);
    }

    public override void Render()
    {
        if (animator == null || playerMovement == null)
            return;

        Vector2 localVelocity = playerMovement.LocalMoveVelocity;

        animator.SetFloat(MoveXHash, localVelocity.x, speedDampTime, Time.deltaTime);
        animator.SetFloat(MoveYHash, localVelocity.y, speedDampTime, Time.deltaTime);
        animator.SetBool(IsGroundedHash, playerMovement.IsGrounded);
    }
}