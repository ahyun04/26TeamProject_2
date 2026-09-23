using Fusion;
using UnityEngine;

namespace TrustNoOne.Missions
{
    /// <summary>
    /// [역할] 차단기 (개인 미션 PS002 "차단기 올리기" · BreakerRestored). 옛 BreakerMission 이식. 2a 명세 2-6.
    ///  레버(StationButton, partIndex = 레버 번호)를 클릭해 켜짐/꺼짐을 바꾸고, 전부 켜지면 완료 → 다시 섞임(ResetForNext).
    ///
    /// [시작 상태 — 결정 P2] 무작위로 섞고 최소 1개는 꺼 둔다 (BreakerRules). 섞는 때:
    ///  스폰 / 초기화(ResetForNext · 제한 시간) / 조작 중단(범위 이탈 · 행동 불가 — "중단하면 처음부터", 명세 P5)
    /// [조작자 규칙] 첫 레버 클릭에서만 TryBeginOperation (권한·범위·내 미션 검사). 이후 클릭은 "조작자 본인인가"만 본다.
    ///  재요청으로 OnOperationStarted 가 다시 불리는 문제(HoldStation 주석 참고)를 피하고, 남이 내 차단기를 건드리지 못하게 한다.
    ///  사망 · 범위 이탈은 베이스 FixedUpdateNetwork 가 매 틱 검사해 취소한다.
    /// [알려진 제약] 조작 중인 사람은 busy 라 다른 미션을 못 한다. 멈추려면 범위 밖으로 나간다 (발전기와 같음).
    /// </summary>
    public class BreakerStation : MissionStation
    {
        public const int MaxLevers = 6;

        [Header("레버 (배열 인덱스 = StationButton partIndex)")]
        [SerializeField] private BreakerLeverVisual[] levers;

        [Networked, Capacity(MaxLevers)]
        private NetworkArray<NetworkBool> LeverStates => default;

        private bool[] buffer;

        private int LeverCount => levers == null ? 0 : Mathf.Min(levers.Length, MaxLevers);

        protected override void OnStationSpawned()
        {
            base.OnStationSpawned();

            if (levers == null || levers.Length == 0 || levers.Length > MaxLevers)
                Debug.LogError($"[BreakerStation] {name}: 레버는 1~{MaxLevers}개여야 합니다 (현재 {(levers == null ? 0 : levers.Length)}개).");

            if (HasStateAuthority && !Completed)
                Shuffle();

            ApplyLevers();
        }

        protected override void OnPartPressed(PlayerRef actor, int partIndex)
        {
            base.OnPartPressed(actor, partIndex);

            if (partIndex < 0 || partIndex >= LeverCount || Completed)
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

            LeverStates.Set(partIndex, !LeverStates.Get(partIndex));

            if (BreakerRules.AllOn(ReadStates()))
                CompleteBy(actor);
        }

        protected override void OnOperationCanceled(PlayerRef actor)
        {
            base.OnOperationCanceled(actor);
            Shuffle();
        }

        protected override void OnResetHost()
        {
            base.OnResetHost();
            Shuffle();
        }

        protected override void OnStationRender()
        {
            base.OnStationRender();
            ApplyLevers();
        }

        private void Shuffle()
        {
            if (LeverCount == 0)
                return;

            bool[] states = Buffer();
            BreakerRules.Shuffle(states, n => Random.Range(0, n));

            for (int i = 0; i < states.Length; i++)
                LeverStates.Set(i, states[i]);
        }

        private bool[] ReadStates()
        {
            bool[] states = Buffer();

            for (int i = 0; i < states.Length; i++)
                states[i] = LeverStates.Get(i);

            return states;
        }

        private bool[] Buffer()
        {
            if (buffer == null || buffer.Length != LeverCount)
                buffer = new bool[LeverCount];

            return buffer;
        }

        private void ApplyLevers()
        {
            if (Object == null || !Object.IsValid)
                return;

            for (int i = 0; i < LeverCount; i++)
            {
                if (levers[i] != null)
                    levers[i].SetState(LeverStates.Get(i));
            }
        }
    }
}
