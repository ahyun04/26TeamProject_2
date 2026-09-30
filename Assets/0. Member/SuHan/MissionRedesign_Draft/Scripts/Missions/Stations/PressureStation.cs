using Fusion;
using UnityEngine;

namespace TrustNoOne.Missions
{
    /// <summary>
    /// [역할] 압력 수치 맞추기 (개인 미션 PS005 · PressureStabilized). 신규 미니게임. 2d 명세 3-3.
    ///  피스톤 A/B/C 가 각자 속도로 오르내린다. 버튼(StationButton 0~2)은 판마다 무작위로 피스톤 하나에 몰래 연결된다(PR7).
    ///  버튼을 눌러 연결된 피스톤이 초록 구간이면 고정(초록불), 아니면 1초 멈춤(빨간불) 후 재개(PR2). 셋 다 고정하면 완료 → 다시 뽑기(ResetForNext).
    ///
    /// [동기화 — PR3] 높이는 매 틱 보내지 않는다. 공식 값(속도 · 위상 · 누적 정지 시간 · 멈춤 구간 · 고정)만 동기화하고
    ///  모든 PC 가 PressureRules.HeightAt 으로 같은 높이를 계산한다. 화면은 Object.RenderTime(이 오브젝트가 지금 그려지는 시각) 기준.
    /// [공정한 판정 — PR4] 버튼 요청에 누른 사람 화면의 시각이 실려 온다(MissionStation.PressPart). 호스트는 그 시각의 높이로 판정한다.
    ///  시각은 [지금 − 0.5초, 지금 + 0.1초] 로 제한한다 (조작 방지). 게스트도 보이는 그대로 판정된다.
    /// [재개 — PR12] 멈춤이 끝난 피스톤은 조작자가 없어도 다시 움직여야 해서 OnHostTickAlways 에서 처리한다.
    /// [조작자] 첫 버튼에서 TryBeginOperation, 이후 조작자 본인만. 범위 이탈 · 행동 불가 · 제한 시간 초기화 → 전부 다시 뽑기 (PR8).
    /// </summary>
    public class PressureStation : MissionStation
    {
        public const int PistonCount = 3;

        private const float MaxRewind = 0.5f;
        private const float MaxAhead = 0.1f;

        [Header("피스톤 (인덱스 = A / B / C)")]
        [SerializeField] private PressureVisual[] pistons;

        [Header("규칙")]
        [Tooltip("초록 구간 (높이 0~1). 압력계 그림의 초록 부분에 맞춘다")]
        [Range(0f, 1f)]
        [SerializeField] private float zoneMin = 0f;
        // 압력계 그림(Texture_Atlas 텍스처): 바늘 −120°(8시) ~ +120°(4시) 눈금 중 초록 = 8시 ~ 11시(−30°) → 높이 0 ~ 0.37.
        //  노랑 11시 ~ 2시 반, 빨강 2시 반 ~ 4시. (2d 테스트 스크린샷으로 확인 — 처음 값 0.6~0.8 은 노란 부분이었다)
        [Range(0f, 1f)]
        [SerializeField] private float zoneMax = 0.37f;

        [Tooltip("초당 높이 변화량 (0~1 을 오르내림). 피스톤마다 이 범위에서 무작위")]
        [SerializeField] private float minSpeed = 0.3f;
        [SerializeField] private float maxSpeed = 0.6f;

        [Tooltip("초록 구간 밖에서 눌렀을 때 멈추는 시간(초)")]
        [SerializeField] private float stallDuration = 1f;

        [Networked, Capacity(PistonCount)] private NetworkArray<float> Speed => default;
        [Networked, Capacity(PistonCount)] private NetworkArray<float> Phase => default;
        [Networked, Capacity(PistonCount)] private NetworkArray<float> TimeOffset => default;
        [Networked, Capacity(PistonCount)] private NetworkArray<float> StallStart => default;
        [Networked, Capacity(PistonCount)] private NetworkArray<float> StallEnd => default;
        [Networked, Capacity(PistonCount)] private NetworkArray<float> ResumedAt => default;
        [Networked, Capacity(PistonCount)] private NetworkArray<NetworkBool> Locked => default;
        [Networked, Capacity(PistonCount)] private NetworkArray<float> LockedHeight => default;
        [Networked, Capacity(PistonCount)] private NetworkArray<int> Mapping => default;

        private bool validSetup;
        private readonly int[] mapBuffer = new int[PistonCount];

        protected override void OnStationSpawned()
        {
            base.OnStationSpawned();

            validSetup = ValidateSetup();

            if (!validSetup)
            {
                Debug.LogError($"[PressureStation] {name}: 피스톤 연출(PressureVisual)이 {PistonCount}개 연결돼야 합니다.");
                return;
            }

            if (HasStateAuthority)
                Reroll();

            ApplyVisuals();
        }

        /// <summary>호스트: 버튼 + 누른 사람 화면의 시각. 시각이 필요 없는 OnPartPressed 는 쓰지 않는다 (base 를 부르지 않음).</summary>
        protected override void OnPartPressedAt(PlayerRef actor, int partIndex, float pressTime)
        {
            if (!validSetup || Completed || partIndex < 0 || partIndex >= PistonCount)
                return;

            if (Operator.IsNone)
            {
                if (!TryBeginOperation(actor))
                    return;
            }
            else if (Operator != actor)
            {
                return;
            }

            float now = Runner.SimulationTime;
            float t = PressureRules.ClampPressTime(pressTime, now, MaxRewind, MaxAhead);
            int piston = Mapping[partIndex];

            if (piston < 0 || piston >= PistonCount || Locked[piston])
                return;

            // PR6: 그 시각에 멈춰 있었으면(재개 전에 누른 요청 포함) 무시
            if (PressureRules.IsStalledAt(t, StallStart[piston], ResumedAt[piston]))
                return;

            float height = HeightFor(piston, t);

            if (PressureRules.InZone(height, zoneMin, zoneMax))
            {
                Locked.Set(piston, true);
                LockedHeight.Set(piston, height);
            }
            else
            {
                StallStart.Set(piston, t);
                StallEnd.Set(piston, t + Mathf.Max(0.05f, stallDuration));
            }

            if (AllLocked())
                CompleteBy(actor);
        }

        protected override void OnHostTickAlways()
        {
            base.OnHostTickAlways();

            if (!validSetup)
                return;

            float now = Runner.SimulationTime;

            for (int i = 0; i < PistonCount; i++)
            {
                float stallStart = StallStart[i];

                if (stallStart < 0f || now < StallEnd[i])
                    continue;

                // 멈춘 높이에서 이어서 움직이도록 누적 정지 시간 반영
                TimeOffset.Set(i, PressureRules.ResumeOffset(TimeOffset[i], stallStart, StallEnd[i]));
                ResumedAt.Set(i, StallEnd[i]);
                StallStart.Set(i, PressureRules.NotStalled);
            }
        }

        protected override void OnOperationCanceled(PlayerRef actor)
        {
            base.OnOperationCanceled(actor);
            Reroll();
        }

        protected override void OnResetHost()
        {
            base.OnResetHost();
            Reroll();
        }

        protected override void OnStationRender()
        {
            base.OnStationRender();
            ApplyVisuals();
        }

        /// <summary>호스트: 연결 · 속도 · 위상을 새로 뽑고 멈춤 · 고정을 푼다 (PR7 · PR8).</summary>
        private void Reroll()
        {
            if (!validSetup)
                return;

            float now = Runner.SimulationTime;
            PressureRules.ShuffleMapping(mapBuffer, n => Random.Range(0, n));

            for (int i = 0; i < PistonCount; i++)
            {
                Mapping.Set(i, mapBuffer[i]);
                Speed.Set(i, Random.Range(Mathf.Min(minSpeed, maxSpeed), Mathf.Max(minSpeed, maxSpeed)));
                Phase.Set(i, Random.Range(0f, 2f));
                TimeOffset.Set(i, 0f);
                StallStart.Set(i, PressureRules.NotStalled);
                StallEnd.Set(i, 0f);
                ResumedAt.Set(i, now);
                Locked.Set(i, false);
                LockedHeight.Set(i, 0f);
            }
        }

        private void ApplyVisuals()
        {
            if (!validSetup || Object == null || !Object.IsValid)
                return;

            float t = Object.RenderTime;

            for (int i = 0; i < PistonCount; i++)
            {
                if (Locked[i])
                {
                    pistons[i].Apply(LockedHeight[i], PistonState.Locked);
                    continue;
                }

                // 표시용 멈춤은 멈춤 구간만 본다 (재개 이전 판정용 ResumedAt 은 쓰지 않음 — 다시 뽑은 직후 빨간불이 켜지지 않게)
                float stallStart = StallStart[i];
                PistonState state = stallStart >= 0f && t >= stallStart ? PistonState.Stalled : PistonState.Moving;
                pistons[i].Apply(HeightFor(i, t), state);
            }
        }

        private float HeightFor(int piston, float t)
        {
            return PressureRules.HeightAt(t, Speed[piston], Phase[piston], TimeOffset[piston], StallStart[piston]);
        }

        private bool AllLocked()
        {
            for (int i = 0; i < PistonCount; i++)
            {
                if (!Locked[i])
                    return false;
            }

            return true;
        }

        private bool ValidateSetup()
        {
            if (pistons == null || pistons.Length != PistonCount)
                return false;

            foreach (PressureVisual piston in pistons)
            {
                if (piston == null)
                    return false;
            }

            return true;
        }
    }
}
