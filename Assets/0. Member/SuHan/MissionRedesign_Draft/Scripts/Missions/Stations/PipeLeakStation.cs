using System;
using System.Collections.Generic;
using Fusion;
using TMPro;
using UnityEngine;
using Random = UnityEngine.Random;

namespace TrustNoOne.Missions
{
    /// <summary>
    /// [역할] 배관 한 개 (단체 미션 TG012 "배관 누수 차단"의 부품 — PipeLeakSealed). 13a 명세 PL8 ~ PL19, 15 명세 NM2 · NM4 · NM6.
    ///  받은 배관 모델 4개(0번 구멍 없음 ~ 3번 구멍 3개) 중 새는 곳 비트 수 번호의 모델만 켠다 — 그 모델의 구멍 1 ~ 3곳이 샌다.
    ///  테이프를 손에 든 채 새는 곳(StationHoldPart)을 조준하고 F 를 2초 누르고 있으면 그 자리가 막힌다.
    ///  새는 곳이 다 막히면 완료(CompleteBy → 잠김 · 「수리 완료」 · TG012 개수 +1). 다음 배관까지 2분은 미션 정의(SinceLastProgress)가 센다.
    ///
    /// [기획서] "배관마다 가스 새는 곳은 랜덤 (1~3개)" / "누수가 발생하는 배관에는 가스가 새는 시각적 효과" / "테이프를 찾아 … 상호작용 키를 누르면 조작이 진행된다" /
    ///  "조작이 완료되면 해당 배관의 누수가 멈추고 「수리 완료」가 표시된다" / 실패 "미션이 초기화되며 다시 진행" → 공통 틀이 ResetStation (막은 배관도 다시 샘 · 다시 뽑기).
    /// [뽑기 — PL11, 계획 실행 중 보충] 호스트가 미션 배정이 끝난 순간 이번 판에 TG012 가 있을 때만 뽑는다 (없는 판은 새지 않는 배관 — 의미 없는 연기를 안 보이게).
    ///  2분 실패로 초기화될 때 다시 뽑는다 (모델이 바뀔 수 있다 — 예: 2번 → 3번). 상태는 "새는 곳" · "막힌 곳" 6칸 비트 묶음 2개 — 늦게 들어온 사람도 같은 모습.
    /// [시작 조건 — PL12] 공통 자격(TryBeginOperation) + 그 자리가 새는 중 + 손에 테이프(주머니 칸 아님). [취소 — PL13] 테이프를 손에서 놓으면 매 틱 검사로 진행 0.
    /// [한 사람 — PL14] 공통 틀대로 한 번에 한 명. 한 자리를 막으면 조작을 풀어 다음 자리는 F 를 다시 누른다.
    /// [표시 — PL18 · PL19 · 15 NM4] 새는 중 = 모델 구멍 + 연기 / 누르는 중 = 테이프 조각이 구멍 표면점에서 진행만큼 커짐 / 막힘 = 조각 다 붙음. 조준 문구는 내 손 상태로.
    /// [가스 피해 — 14a 명세 GD2 ~ GD12, 사용자 요청 · 기획서에 없음] 호스트가 1초마다 아직 새는 자리 둘레(수평 1.2m · 높이 차 2m)의 플레이어에게
    ///  팀원 PlayerHealth.ApplyDamage 로 5 (체력 10 아래로는 안 내려감 — 죽지 않음). 모두 대상(살인마 포함), 단 이 배관에서 테이프를 붙이는 사람(조작자)은 뺀다 —
    ///  피격 알림이 팀원 입력 코드의 F 누르고 있기를 끊어 2초 막기를 끝낼 수 없게 되므로. 한 사람은 여러 배관에 걸려도 1초에 한 번 (배관끼리 쓰는 기록).
    /// [근거] 13a 명세 PL2 · PL8 ~ PL19 · PL27, 기획서 "배관 누수 차단", 14a 명세 GD2 ~ GD12, 15 명세 NM2 · NM4 · NM6 (받은 모델 — 사용자 결정 A)
    /// </summary>
    public class PipeLeakStation : HoldStation
    {
        /// <summary>새는 곳 한 자리의 부품 (변환 도구가 채운다).</summary>
        [Serializable]
        public class LeakSpot
        {
            [Tooltip("조준 상자 (조준 레이어) — StationHoldPart 가 붙은 오브젝트")]
            public Collider Target;

            [Tooltip("가스 연기")]
            public ParticleSystem Gas;

            [Tooltip("테이프 조각 기준점 (구멍 표면점) — 진행만큼 커진다 (15 NM4)")]
            public Transform Tape;

            [Tooltip("조준 문구 글자")]
            public TMP_Text Prompt;
        }

        public const string ReadyText = "[F] 테이프로 막기";
        public const string NeedTapeText = "테이프가 필요합니다";
        public const string DoneText = "수리 완료";

        // 진행 0 일 때 테이프 크기 — 0 이면 크기 0 행렬로 그려지지 않게 아주 작게
        private const float MinTapeScale = 0.05f;

        [Header("새는 곳 (모델 구멍 6자리 — 변환 도구가 채운다)")]
        [SerializeField] private LeakSpot[] spots = new LeakSpot[PipeLeakRules.SpotCount];

        [Header("배관 모델 (15 NM6 — 0번 구멍 없음 ~ 3번 구멍 3개, 변환 도구가 채운다)")]
        [Tooltip("새는 곳 비트 수 번호의 모델만 켠다. 새는 곳 자리는 속한 모델 아래에 있어 함께 켜지고 꺼진다")]
        [SerializeField] private GameObject[] pipeModels = new GameObject[PipeLeakRules.ModelCount];

        [Header("표시")]
        [Tooltip("완료하면 켜지는 「수리 완료」 글자")]
        [SerializeField] private GameObject doneLabel;

        [Header("테스트 (13a PL27)")]
        [Tooltip("0 = 무작위 1 ~ 3 (기획서). 1 ~ 3 = 그 개수로 고정 — 3개짜리를 바로 보고 싶을 때")]
        [Range(0, PipeLeakRules.MaxLeaks)]
        [SerializeField] private int fixedLeakCount;

        [Header("가스 피해 (14a — 사용자 요청, 기획서에 없음)")]
        [Tooltip("새는 곳에서 이 수평 거리 안이면 가스 속 (m) — 연기가 보이는 0.5 ~ 0.8m + 여유")]
        [SerializeField] private float gasRadius = 1.2f;

        [Tooltip("새는 곳과 높이 차가 이 안일 때만 (m) — 다른 층은 빼려고")]
        [SerializeField] private float gasHeightRange = 2f;

        [Tooltip("피해 간격 (초)")]
        [SerializeField] private float gasInterval = 1f;

        [Tooltip("한 번에 줄이는 체력 (최대 체력 100 기준)")]
        [SerializeField] private float gasDamage = 5f;

        [Tooltip("가스로는 이 체력 아래로 내려가지 않는다 — 죽지 않음 (GD4)")]
        [SerializeField] private float gasMinHealth = 10f;

        // 호스트: 배관끼리 나눠 쓰는 "사람 번호 → 마지막 가스 피해 시각" — 여러 배관에 걸려도 간격마다 한 번 (GD8). 판이 다시 시작되면 비운다
        private static readonly Dictionary<PlayerRef, float> lastGasHit = new Dictionary<PlayerRef, float>();

        // 호스트: 이 배관의 가스 속에 있다고 로그를 남긴 사람 (처음 · 벗어날 때만 로그 — GD12)
        private readonly HashSet<PlayerRef> gasLogged = new HashSet<PlayerRef>();
        private float gasTimer;

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

            // 새 판: 가스 피해 기록을 비운다 (네트워크 시각이 처음부터 다시 센다)
            lastGasHit.Clear();
            gasLogged.Clear();
            gasTimer = 0f;

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

        /// <summary>새는 곳을 다시 뽑는다 (15 NM2 — 개수 1 ~ 3 → 그 개수 모델의 구멍 자리 묶음).</summary>
        private void Roll()
        {
            LeakMask = PipeLeakRules.PickMask(fixedLeakCount, Random.value);
            Debug.Log($"[PipeLeakStation] {name}: 새는 곳 {PipeLeakRules.Count(LeakMask)}개 — 배관 모델 {PipeLeakRules.ModelIndex(LeakMask)}번 (자리 {DescribeSpots(LeakMask)})");
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
        //  호스트: 누수 가스 피해 (14a)
        // ═════════════════════════════════════════════════════════════

        protected override void OnHostTickAlways()
        {
            base.OnHostTickAlways();

            gasTimer += Runner.DeltaTime;

            if (gasTimer < gasInterval)
                return;

            gasTimer = 0f;
            DamagePlayersInGas();
        }

        /// <summary>
        /// 아직 새는 자리 둘레의 플레이어에게 가스 피해 (GD2 ~ GD9). 이 배관의 조작자(테이프를 붙이는 사람)는 뺀다 (GD3).
        /// 플레이어는 생명 유지 장치 폭발처럼 씬의 PlayerHealth 를 돌며 찾는다 (3b LS5 와 같은 길).
        /// </summary>
        private void DamagePlayersInGas()
        {
            if (Completed || PipeLeakRules.Remaining(LeakMask, SealedMask) == 0)
            {
                gasLogged.Clear();
                return;
            }

            float now = Runner.SimulationTime;

            foreach (PlayerHealth health in FindObjectsByType<PlayerHealth>(FindObjectsSortMode.None))
            {
                if (health == null || health.Object == null || !health.Object.IsValid)
                    continue;

                PlayerRef player = health.Object.InputAuthority;
                int spot = FindGasSpot(health.transform.position);

                if (spot < 0)
                {
                    if (gasLogged.Remove(player))
                        Debug.Log($"[PipeLeakStation] {name}: 플레이어 {player.PlayerId} 가스에서 벗어남");

                    continue;
                }

                // 테이프를 붙이는 중 — 피격 알림이 F 누르고 있기를 끊지 않게 (GD3)
                if (player == Operator)
                    continue;

                bool hasHit = lastGasHit.TryGetValue(player, out float lastHit);

                if (!GasRules.CanHitAgain(hasHit, lastHit, now, gasInterval))
                    continue;

                float amount = GasRules.DamageAboveFloor(health.CurrentHealth, gasDamage, gasMinHealth);

                if (amount <= 0f)
                    continue;

                health.ApplyDamage(amount);
                lastGasHit[player] = now;

                if (gasLogged.Add(player))
                    Debug.Log($"[PipeLeakStation] {name}: 플레이어 {player.PlayerId} 가스 피해 시작 (자리 {spot + 1}, 1초마다 {gasDamage:0.#} · 체력 {gasMinHealth:0.#} 까지)");
            }
        }

        /// <summary>그 위치가 아직 새는 자리 하나의 가스 속이면 그 자리 번호, 아니면 −1 (GD2 · GD7).</summary>
        private int FindGasSpot(Vector3 position)
        {
            for (int i = 0; i < spots.Length; i++)
            {
                LeakSpot spot = spots[i];

                if (spot == null || spot.Target == null || !PipeLeakRules.IsLeaking(LeakMask, SealedMask, i))
                    continue;

                Vector3 d = position - spot.Target.transform.position;

                if (GasRules.InGas(d.x, d.y, d.z, gasRadius, gasHeightRange))
                    return i;
            }

            return -1;
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

            // 켤 배관 모델 = 새는 곳 비트 수 (NM6). 먼저 켜야 그 아래 새는 곳의 연기가 재생된다 (꺼진 오브젝트의 파티클은 Play 해도 나오지 않음)
            int model = PipeLeakRules.ModelIndex(leak);

            if (pipeModels != null)
            {
                for (int m = 0; m < pipeModels.Length; m++)
                {
                    if (pipeModels[m] != null && pipeModels[m].activeSelf != (m == model))
                        pipeModels[m].SetActive(m == model);
                }
            }

            for (int i = 0; i < spots.Length; i++)
            {
                LeakSpot spot = spots[i];

                if (spot == null)
                    continue;

                bool isLeak = (leak & (1 << i)) != 0;
                bool isSealed = (sealedMask & (1 << i)) != 0;
                bool leaking = isLeak && !isSealed;

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
