using System;
using Fusion;
using TMPro;
using UnityEngine;
using Random = UnityEngine.Random;

namespace TrustNoOne.Missions
{
    /// <summary>
    /// [역할] 배관 한 개 (단체 미션 TG012 "배관 누수 차단"의 부품 — PipeLeakSealed). 13a 명세 PL8 ~ PL19.
    ///  후보 6자리 중 1 ~ 3곳이 샌다. 테이프를 손에 든 채 새는 곳(StationHoldPart)을 조준하고 F 를 2초 누르고 있으면 그 자리가 막힌다.
    ///  새는 곳이 다 막히면 완료(CompleteBy → 잠김 · 「수리 완료」 · TG012 개수 +1). 다음 배관까지 2분은 미션 정의(SinceLastProgress)가 센다.
    ///
    /// [기획서] "배관마다 가스 새는 곳은 랜덤 (1~3개)" / "누수가 발생하는 배관에는 가스가 새는 시각적 효과" / "테이프를 찾아 … 상호작용 키를 누르면 조작이 진행된다" /
    ///  "조작이 완료되면 해당 배관의 누수가 멈추고 「수리 완료」가 표시된다" / 실패 "미션이 초기화되며 다시 진행" → 공통 틀이 ResetStation (막은 배관도 다시 샘 · 다시 뽑기).
    /// [뽑기 — PL11, 계획 실행 중 보충] 호스트가 미션 배정이 끝난 순간 이번 판에 TG012 가 있을 때만 뽑는다 (없는 판은 새지 않는 배관 — 의미 없는 연기를 안 보이게).
    ///  2분 실패로 초기화될 때 다시 뽑는다. 상태는 "새는 곳" · "막힌 곳" 6칸 비트 묶음 2개 — 늦게 들어온 사람도 같은 모습.
    /// [시작 조건 — PL12] 공통 자격(TryBeginOperation) + 그 자리가 새는 중 + 손에 테이프(주머니 칸 아님). [취소 — PL13] 테이프를 손에서 놓으면 매 틱 검사로 진행 0.
    /// [한 사람 — PL14] 공통 틀대로 한 번에 한 명. 한 자리를 막으면 조작을 풀어 다음 자리는 F 를 다시 누른다.
    /// [표시 — PL17 · PL18 · PL19] 새는 중 = 구멍 + 연기 / 누르는 중 = X자 테이프가 진행만큼 커짐 / 막힘 = 테이프 다 붙음. 조준 문구는 내 손 상태로.
    /// [근거] 13a 명세 PL2 · PL5 · PL8 ~ PL19 · PL27, 기획서 "배관 누수 차단"
    /// </summary>
    public class PipeLeakStation : HoldStation
    {
        /// <summary>새는 곳 한 자리의 부품 (변환 도구가 채운다).</summary>
        [Serializable]
        public class LeakSpot
        {
            [Tooltip("조준 상자 (조준 레이어) — StationHoldPart 가 붙은 오브젝트")]
            public Collider Target;

            [Tooltip("검은 구멍 원판")]
            public Renderer Hole;

            [Tooltip("가스 연기")]
            public ParticleSystem Gas;

            [Tooltip("X자 테이프 묶음 — 진행만큼 커진다")]
            public Transform Tape;

            [Tooltip("조준 문구 글자")]
            public TMP_Text Prompt;
        }

        public const string ReadyText = "[F] 테이프로 막기";
        public const string NeedTapeText = "테이프가 필요합니다";
        public const string DoneText = "수리 완료";

        // 진행 0 일 때 테이프 크기 — 0 이면 크기 0 행렬로 그려지지 않게 아주 작게
        private const float MinTapeScale = 0.05f;

        [Header("새는 곳 (후보 6자리 — 변환 도구가 채운다)")]
        [SerializeField] private LeakSpot[] spots = new LeakSpot[PipeLeakRules.SpotCount];

        [Header("표시")]
        [Tooltip("완료하면 켜지는 「수리 완료」 글자")]
        [SerializeField] private GameObject doneLabel;

        [Header("테스트 (13a PL27)")]
        [Tooltip("0 = 무작위 1 ~ 3 (기획서). 1 ~ 3 = 그 개수로 고정 — 3개짜리를 바로 보고 싶을 때")]
        [Range(0, PipeLeakRules.MaxLeaks)]
        [SerializeField] private int fixedLeakCount;

        [Networked] private int LeakMask { get; set; }
        [Networked] private int SealedMask { get; set; }

        // 표시 캐시 — 바뀔 때만 만진다 (Render 는 매 프레임)
        private int shownLeak = -1;
        private int shownSealed = -1;
        private int shownPart = -2;
        private float shownProgress = -1f;
        private int shownFlags = -1;

        // ═════════════════════════════════════════════════════════════
        //  호스트: 뽑기 · 시작 조건 · 막기
        // ═════════════════════════════════════════════════════════════

        protected override void OnMissionsAssignedHost(bool usedThisRound)
        {
            base.OnMissionsAssignedHost(usedThisRound);
            SealedMask = 0;

            if (usedThisRound)
            {
                Roll();
            }
            else
            {
                LeakMask = 0;
                Debug.Log($"[PipeLeakStation] {name}: 이번 판에 배관 미션이 없어 새지 않습니다.");
            }
        }

        protected override void OnResetHost()
        {
            base.OnResetHost();
            SealedMask = 0;
            Roll();
        }

        /// <summary>새는 곳을 다시 뽑는다 (PL5 — 개수 1 ~ 3 · 후보 6곳 중 서로 다른 자리).</summary>
        private void Roll()
        {
            float[] orderRolls = new float[PipeLeakRules.SpotCount];

            for (int i = 0; i < orderRolls.Length; i++)
                orderRolls[i] = Random.value;

            LeakMask = PipeLeakRules.PickMask(fixedLeakCount, Random.value, orderRolls);
            Debug.Log($"[PipeLeakStation] {name}: 새는 곳 {PipeLeakRules.Count(LeakMask)}개 (자리 {DescribeSpots(LeakMask)})");
        }

        protected override bool CanBeginHoldPart(PlayerRef actor, int part)
        {
            return PipeLeakRules.IsLeaking(LeakMask, SealedMask, part) && MissionItems.TryGetHeld(Runner, actor, out TapeTool _);
        }

        /// <summary>테이프를 손에서 놓았거나(내려놓기 · 주머니) 누르던 자리가 이미 막혔으면 취소 — 진행 0 (PL13).</summary>
        protected override void OnHostTick(PlayerRef actor)
        {
            if (!MissionItems.TryGetHeld(Runner, actor, out TapeTool _) || !PipeLeakRules.IsLeaking(LeakMask, SealedMask, HoldPart))
            {
                CancelOperation();
                return;
            }

            base.OnHostTick(actor);
        }

        /// <summary>2초가 찼다: 그 자리를 막고 조작을 푼다. 새는 곳이 다 막혔으면 완료 (PL13 · PL15).</summary>
        protected override void OnHoldReached(PlayerRef actor)
        {
            int part = HoldPart;
            SealedMask = PipeLeakRules.Seal(SealedMask, part);
            CancelOperation();

            int remaining = PipeLeakRules.Remaining(LeakMask, SealedMask);
            Debug.Log($"[PipeLeakStation] {name}: 자리 {part + 1} 막음 (남은 {remaining}곳)");

            if (PipeLeakRules.AllSealed(LeakMask, SealedMask))
                CompleteBy(actor);
        }

        // ═════════════════════════════════════════════════════════════
        //  모든 PC: 표시
        // ═════════════════════════════════════════════════════════════

        protected override void OnStationSpawned()
        {
            base.OnStationSpawned();
            UpdateVisuals(true);
        }

        protected override void OnStationRender()
        {
            base.OnStationRender();
            UpdateVisuals(false);
        }

        protected override void OnCompletedVisual()
        {
            base.OnCompletedVisual();
            UpdateVisuals(true);
        }

        protected override void OnResetVisual()
        {
            base.OnResetVisual();
            UpdateVisuals(true);
        }

        private void UpdateVisuals(bool force)
        {
            if (Object == null || !Object.IsValid || spots == null)
                return;

            int leak = LeakMask;
            int sealedMask = SealedMask;
            int part = Operator.IsNone ? -1 : HoldPart;
            float progress = part >= 0 ? HoldProgress01 : 0f;
            bool hasTape = MissionItems.IsLocalHolding<TapeTool>();
            int flags = (LocalInteractionAllowed ? 1 : 0) | (hasTape ? 2 : 0) | (Completed ? 4 : 0);

            if (!force && leak == shownLeak && sealedMask == shownSealed && part == shownPart && Mathf.Approximately(progress, shownProgress) && flags == shownFlags)
                return;

            shownLeak = leak;
            shownSealed = sealedMask;
            shownPart = part;
            shownProgress = progress;
            shownFlags = flags;

            for (int i = 0; i < spots.Length; i++)
            {
                LeakSpot spot = spots[i];

                if (spot == null)
                    continue;

                bool isLeak = (leak & (1 << i)) != 0;
                bool isSealed = (sealedMask & (1 << i)) != 0;
                bool leaking = isLeak && !isSealed;

                if (spot.Hole != null)
                    spot.Hole.enabled = leaking;

                if (spot.Gas != null)
                {
                    // 멈출 때는 새 연기만 끊는다 — 이미 나온 연기는 자연스럽게 사라지게
                    if (leaking && !spot.Gas.isPlaying)
                        spot.Gas.Play(true);
                    else if (!leaking && spot.Gas.isPlaying)
                        spot.Gas.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                }

                if (spot.Tape != null)
                {
                    bool showTape = isLeak && (isSealed || part == i);
                    spot.Tape.gameObject.SetActive(showTape);
                    spot.Tape.localScale = Vector3.one * (isSealed ? 1f : Mathf.Max(MinTapeScale, progress));
                }

                // 조준은 새는 중 && 내 미션 && 안 잠김일 때만 (연출 — 규칙은 호스트가 따로)
                if (spot.Target != null)
                    spot.Target.enabled = leaking && LocalInteractionAllowed;

                if (spot.Prompt != null)
                    spot.Prompt.text = hasTape ? ReadyText : NeedTapeText;
            }

            if (doneLabel != null && doneLabel.activeSelf != Completed)
                doneLabel.SetActive(Completed);
        }

        private static string DescribeSpots(int mask)
        {
            System.Text.StringBuilder text = new System.Text.StringBuilder();

            for (int i = 0; i < PipeLeakRules.SpotCount; i++)
            {
                if ((mask & (1 << i)) == 0)
                    continue;

                if (text.Length > 0)
                    text.Append(" · ");

                text.Append(i + 1);
            }

            return text.Length > 0 ? text.ToString() : "없음";
        }
    }
}
