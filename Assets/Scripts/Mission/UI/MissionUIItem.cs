using TMPro;
using UnityEngine;

public class MissionUIItem : MonoBehaviour
{
    [SerializeField] private TMP_Text missionText;

    public void SetData(string typeLabel, string missionName, int progress, int requiredCount)
    {
        if (missionText == null)
            return;

        missionText.text = $"[{typeLabel}] {missionName} ( {progress} / {requiredCount} )";
    }
}