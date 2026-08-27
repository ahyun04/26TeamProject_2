using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class MissionProgressUI : MonoBehaviour
{
    [SerializeField] private MissionSystem missionSystem;
    [SerializeField] private Image gaugeFill;
    [SerializeField] private TMP_Text progressText;

    private float previousProgress = -1f;


    private void Update()
    {
        if (missionSystem == null)
            return;

        if (missionSystem.Object == null || !missionSystem.Object.IsValid)
            return;

        if (!missionSystem.Initialized)
            return;

        float progress = missionSystem.GetCitizenMissionProgress();

        if (Mathf.Approximately(previousProgress, progress))
            return;

        previousProgress = progress;

        Refresh(progress);
    }


    /// <summary>
    /// 전체 미션 진행률을 게이지와 퍼센트 텍스트에 표시
    /// </summary>
    private void Refresh(float progress)
    {
        if (gaugeFill != null)
        {
            RectTransform fillRect = gaugeFill.rectTransform;
            Vector2 anchorMax = fillRect.anchorMax;

            anchorMax.x = progress;
            fillRect.anchorMax = anchorMax;
        }

        if (progressText != null)
            progressText.text = $"{Mathf.RoundToInt(progress * 100f)}%";
    }
}