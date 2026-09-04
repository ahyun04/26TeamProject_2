using TMPro;
using UnityEngine;

public class PlayerResultEntryUI : MonoBehaviour
{
    [SerializeField] private TMP_Text entryText;

    public void SetData(string playerName, string role, string result)
    {
        if (entryText != null)
            entryText.text = $"{playerName}  |  {role}  |  {result}";
    }
}
