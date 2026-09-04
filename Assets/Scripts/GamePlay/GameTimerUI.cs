using TMPro;
using UnityEngine;

public class GameTimerUI : MonoBehaviour
{
    [SerializeField] private GameTimer gameTimer;
    [SerializeField] private TMP_Text timerText;

    private int displayedSeconds = -1;

    private void Update()
    {
        if (gameTimer == null)
            gameTimer = FindFirstObjectByType<GameTimer>();

        if (timerText == null)
            timerText = GetComponent<TMP_Text>();

        if (gameTimer == null || timerText == null)
            return;

        if (gameTimer.Object == null || !gameTimer.Object.IsValid)
            return;

        int remainingSeconds = Mathf.CeilToInt(gameTimer.RemainingSeconds);
        if (remainingSeconds == displayedSeconds)
            return;

        displayedSeconds = remainingSeconds;

        int minutes = remainingSeconds / 60;
        int seconds = remainingSeconds % 60;
        timerText.text = $"{minutes:00}:{seconds:00}";
    }
}
