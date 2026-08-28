using Fusion;
using UnityEngine;

/// <summary>
/// 플레이어가 바라보는 미션이 변경되면
/// 해당 미션의 Prompt 표시 상태를 변경
/// </summary>
public class PlayerMissionPromptController : NetworkBehaviour
{
    [SerializeField] private PlayerTargetDetector targetDetector;

    private MissionPrompt currentPrompt;


    public override void Spawned()
    {
        if (!HasInputAuthority)
            return;

        if (targetDetector == null)
            targetDetector = GetComponent<PlayerTargetDetector>();

        if (targetDetector == null)
        {
            Debug.LogError("[PlayerMissionPromptController] PlayerTargetDetector가 없습니다.");
            return;
        }

        targetDetector.TargetChanged -= OnTargetChanged;
        targetDetector.TargetChanged += OnTargetChanged;
    }


    private void OnDestroy()
    {
        if (targetDetector != null)
            targetDetector.TargetChanged -= OnTargetChanged;
    }


    /// <summary>
    /// 바라보는 대상이 변경되었을 때 이전 Prompt를 숨기고 새로운 Prompt를 표시
    /// </summary>
    private void OnTargetChanged(ITargetable target)
    {
        if (currentPrompt != null)
            currentPrompt.Hide();

        currentPrompt = FindPrompt(target);

        if (currentPrompt != null)
            currentPrompt.Show();
    }


    /// <summary>
    /// 감지된 대상에서 MissionPrompt를 찾음
    /// </summary>
    private MissionPrompt FindPrompt(ITargetable target)
    {
        Component targetComponent = target as Component;

        if (targetComponent == null)
            return null;

        return targetComponent.GetComponentInParent<MissionPrompt>(true);
    }
}