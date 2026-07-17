using TMPro;
using UnityEngine;

/// <summary>
/// 하나의 미션 정보를 표시합니다.
/// </summary>
public class MissionItemUI : MonoBehaviour
{
    [SerializeField] private TMP_Text missionNameText;
    [SerializeField] private TMP_Text descriptionText;
    [SerializeField] private TMP_Text progressText;

    public void SetMission(MissionData missionData, MissionRuntimeState state, PlayerRole role)
    {
        if (missionData == null)
        {
            Debug.LogError("MissionData가 없습니다.", gameObject);
            return;
        }

        if (missionNameText == null || descriptionText == null || progressText == null)
        {
            Debug.LogError("MissionItemUI의 텍스트가 Inspector에 연결되지 않았습니다.", gameObject);
            return;
        }

        string kindText = state.MissionKind == MissionKind.Common ? "단체 미션" : "개인 미션";
        bool isCompleted = state.Progress >= state.TargetCount;

        missionNameText.text = $"[{kindText}] {missionData.MissionName}";
        descriptionText.text = missionData.GetDescription(role);
        progressText.text = isCompleted ? "완료" : $"{state.Progress} / {state.TargetCount}";

        Debug.Log($"MissionItem 표시 완료: {missionData.MissionName}", gameObject);
    }
}