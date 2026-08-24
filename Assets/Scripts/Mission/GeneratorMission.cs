using Fusion;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class GeneratorMission : MissionMiniGameBase
{
    [Header("UI")]
    [SerializeField] private TMP_Text guideText;
    [SerializeField] private Image gaugeFill;
    [SerializeField] private TMP_Text completeText;

    [Header("설정")]
    [SerializeField] private float fillDuration = 3f;
    [SerializeField] private string waitingText = "발전기를 작동시키세요.";
    [SerializeField] private string runningText = "발전기 작동 중...";

    [Networked] private float Progress { get; set; }
    [Networked] private NetworkBool IsRunning { get; set; }

    public override void Spawned()
    {
        if (completeText != null)
            completeText.gameObject.SetActive(false);

        UpdateUI();
    }

    public override void FixedUpdateNetwork()
    {
        if (!Object.HasStateAuthority)
            return;

        if (!IsRunning || IsCompleted)
            return;

        Progress += Runner.DeltaTime / fillDuration;

        if (Progress >= 1f)
        {
            Progress = 1f;
            IsRunning = false;
            Complete();
        }
    }

    public override void Render()
    {
        UpdateUI();
    }

    public override void StartMission()
    {
        if (IsCompleted || IsRunning)
            return;

        RPC_StartGenerator();
    }

    public override void StopMission()
    {
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_StartGenerator()
    {
        if (IsCompleted || IsRunning)
            return;

        Progress = 0f;
        IsRunning = true;
    }

    private void UpdateUI()
    {
        if (gaugeFill != null)
            gaugeFill.fillAmount = Progress;

        if (guideText != null)
        {
            if (IsCompleted)
                guideText.text = "";
            else if (IsRunning)
                guideText.text = runningText;
            else
                guideText.text = waitingText;
        }

        if (completeText != null)
            completeText.gameObject.SetActive(IsCompleted);
    }
}