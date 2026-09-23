using Fusion;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TrustNoOne.Missions
{
    /// <summary>
    /// [역할] 안테나 (개인 미션 PS004 "안테나 방향 맞추기" · AntennaAligned). 옛 AntennaMission + AntennaLockButton 이식. 2a 명세 2-4.
    ///  1) F 를 누르고 있으면 안테나가 돈다 (RotationHoldStation)
    ///  2) 목표 회전량에 도달하면 "준비 완료" — 완료가 아니다. F 를 떼도 유지된다.
    ///  3) 고정 버튼(StationButton, partIndex 0)을 누르면 완료 → 다음 사람을 위해 원래대로(ResetForNext)
    ///
    /// [옛 코드와 다른 점 — 명세 P6] 1)~3)을 같은 사람이 이어서 해야 한다.
    ///  옛 코드는 준비 상태가 남아 아무나 고정할 수 있었다. 개인 미션은 본인 행동으로만 진행해야 한다(stage1 S3).
    ///  준비 후 범위를 벗어나거나 행동 불가가 되면 베이스가 취소 → 처음부터 (명세 P5).
    /// [화면 UI] 옛 안테나 프리팹의 게이지·퍼센트 글자를 그대로 쓴다.
    /// </summary>
    public class AntennaStation : RotationHoldStation
    {
        /// <summary>고정 버튼 부품 번호 (변환기가 ButtonHitbox 의 StationButton 에 넣는다).</summary>
        public const int LockButtonPart = 0;

        [Header("안테나 화면 UI (기존 프리팹)")]
        [SerializeField] private Image gaugeFill;
        [SerializeField] private TMP_Text percentText;

        /// <summary>준비 완료 뒤에는 F 를 떼도 취소하지 않는다 (고정 버튼으로 가야 하므로).</summary>
        protected override bool CancelOnRelease => !HoldReached;

        /// <summary>
        /// base(= CompleteBy)를 부르지 않는다: 목표 회전량 도달은 "준비 완료"일 뿐이다.
        /// HoldStation 이 HoldReached = true 로 두고 Operator 를 유지하므로, 여기서는 할 일이 없다.
        /// </summary>
        protected override void OnHoldReached(PlayerRef actor)
        {
        }

        /// <summary>고정 버튼: 준비 완료 + 돌린 본인일 때만 완료. 그 밖(준비 전, 다른 사람)은 조용히 무시.</summary>
        protected override void OnPartPressed(PlayerRef actor, int partIndex)
        {
            base.OnPartPressed(actor, partIndex);

            if (partIndex != LockButtonPart || Completed)
                return;

            if (!HoldReached || Operator != actor)
                return;

            CompleteBy(actor);
        }

        protected override void OnStationSpawned()
        {
            base.OnStationSpawned();
            UpdateUI();
        }

        protected override void OnStationRender()
        {
            base.OnStationRender();
            UpdateUI();
        }

        private void UpdateUI()
        {
            if (Object == null || !Object.IsValid)
                return;

            float progress = HoldProgress01;

            if (gaugeFill != null)
                gaugeFill.fillAmount = progress;

            if (percentText != null)
                percentText.text = $"{Mathf.RoundToInt(progress * 100f)}%";
        }
    }
}
