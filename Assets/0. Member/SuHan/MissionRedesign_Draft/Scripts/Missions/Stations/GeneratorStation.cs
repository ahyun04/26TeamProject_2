using Fusion;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TrustNoOne.Missions
{
    /// <summary>
    /// [역할] 발전기 한 대 (단체 미션 "발전기 작동시키기"의 부품). 옛 GeneratorMission 이식.
    ///  버튼(StationButton, partIndex 0) 클릭 → 3초 자동 작동 → 완료(GeneratorRepaired 발행, Lock → 「수리 완료」)
    ///
    /// [기획서] 단체 미션 기획서 "각 발전기에 있는 상호작용 버튼을 누르면 발전기 수리가 진행된다",
    ///  "발전기 한 대의 수리가 완료되면 해당 발전기에 「수리 완료」가 표시된다".
    /// [옛 코드와 다른 점] 작동 중 누른 사람이 범위를 벗어나면 취소된다 (기획서 공통 취소 조건, 명세 S7).
    /// [제한 시간] 발전기는 2분 규칙을 모른다. 미션이 제한 시간으로 초기화되면 베이스가 ResetStation 을 불러 줄 뿐이다 (명세 S6).
    /// [월드 UI] 기존 발전기 프리팹의 화면(안내 문구·게이지·완료 문구)을 그대로 쓴다 (명세 S2).
    /// </summary>
    public class GeneratorStation : MissionStation
    {
        [Header("월드 UI (기존 발전기 프리팹의 화면)")]
        [SerializeField] private TMP_Text guideText;
        [SerializeField] private Image gaugeFill;
        [SerializeField] private TMP_Text completeText;

        [Header("설정")]
        [Tooltip("버튼을 누른 뒤 자동으로 작동하는 시간(초).")]
        [SerializeField] private float fillDuration = 3f;
        [SerializeField] private string waitingText = "발전기를 작동시키세요.";
        [SerializeField] private string runningText = "발전기 작동 중...";

        [Networked] private float Progress { get; set; }
        [Networked] private NetworkBool Running { get; set; }

        /// <summary>버튼이 눌리면 작동 시작 (이미 작동 중이거나 잠겼으면 무시). 권한 검사는 TryBeginOperation.</summary>
        protected override void OnPartPressed(PlayerRef actor, int partIndex)
        {
            if (Running || Completed)
                return;

            TryBeginOperation(actor);
        }

        protected override void OnOperationStarted(PlayerRef actor)
        {
            Progress = 0f;
            Running = true;
        }

        /// <summary>누르고 있을 필요 없이 자동으로 찬다 (옛 코드와 같음). 취소 검사는 베이스가 먼저 한다.</summary>
        protected override void OnHostTick(PlayerRef actor)
        {
            if (!Running)
                return;

            Progress += Runner.DeltaTime / Mathf.Max(fillDuration, 0.01f);

            if (Progress < 1f)
                return;

            Progress = 1f;
            Running = false;
            CompleteBy(actor);
        }

        protected override void OnOperationCanceled(PlayerRef actor)
        {
            Running = false;
            Progress = 0f;
        }

        protected override void OnResetHost()
        {
            Running = false;
            Progress = 0f;
        }

        protected override void OnStationSpawned()
        {
            UpdateUI();
        }

        protected override void OnStationRender()
        {
            UpdateUI();
        }

        private void UpdateUI()
        {
            if (Object == null || !Object.IsValid)
                return;

            if (gaugeFill != null)
                gaugeFill.fillAmount = Completed ? 1f : Progress;

            if (guideText != null)
                guideText.text = Completed ? string.Empty : (Running ? runningText : waitingText);

            if (completeText != null)
                completeText.gameObject.SetActive(Completed);
        }
    }
}
