using Fusion;
using UnityEngine;

namespace TrustNoOne.Missions
{
    /// <summary>
    /// [역할] 전선 연결 (개인 미션 PS003 "전선 연결하기" · WiresConnected). 옛 WiringMission 이식. 2b 명세 2-7.
    ///  1) 시작점(StationDragPart 0~3)을 끌어 같은 색 도착점(StationDropTarget 0~3)에 놓으면 연결 (기획서 "같은 색 선끼리 연결")
    ///  2) 넷 다 이으면 레버(StationButton, LeverPart)를 당겨 완료 (결정 W1 — 옛 조작 유지) → 다음 사람을 위해 다시 섞임(ResetForNext)
    ///
    /// [색] 시작점 색은 판마다 무작위(WiringRules.ShuffleColors), 도착점 색은 프리팹 고정(endColors).
    /// [판정 순서 — W4] 색 · 빈 자리 판정(CanConnect)을 먼저 하고, 통과했을 때만 조작자를 확정한다 → 잘못 놓은 시도로 busy 가 되지 않는다.
    /// [조작자] 첫 연결 성공에서 TryBeginOperation, 이후는 "조작자 본인인가"만 본다 (차단기와 같음). 레버도 조작자 본인만.
    /// [중단 — W3] 범위 이탈 · 행동 불가로 취소되면 연결 전부 해제 + 색 다시 섞기 (2a P5 와 같은 규칙).
    /// [연출] 매 Render 에서 바뀐 값만 WireVisual 에 반영한다. 모든 피어가 같은 경로.
    /// </summary>
    public class WiringStation : MissionStation
    {
        /// <summary>완료 레버 부품 번호 (StationButton partIndex).</summary>
        public const int LeverPart = 0;

        private const int WireCount = WiringRules.WireCount;

        [Header("전선 (인덱스 = StationDragPart partIndex)")]
        [SerializeField] private WireVisual[] wires;

        [Header("도착점 (인덱스 = StationDropTarget partIndex)")]
        [SerializeField] private Transform[] endAnchors;
        [SerializeField] private WiringColor[] endColors;

        [Networked, Capacity(WireCount)]
        private NetworkArray<int> StartColors => default;

        [Networked, Capacity(WireCount)]
        private NetworkArray<int> ConnectedEnds => default;

        private readonly int[] shownColors = { -1, -1, -1, -1 };
        private readonly int[] shownEnds = { -2, -2, -2, -2 };
        private readonly int[] colorBuffer = new int[WireCount];
        private readonly int[] endBuffer = new int[WireCount];
        private readonly int[] endColorBuffer = new int[WireCount];
        private bool validSetup;

        protected override void OnStationSpawned()
        {
            base.OnStationSpawned();

            validSetup = IsLength(wires) && IsLength(endAnchors) && IsLength(endColors);

            if (!validSetup)
            {
                Debug.LogError($"[WiringStation] {name}: wires · endAnchors · endColors 는 {WireCount}개씩이어야 합니다.");
                return;
            }

            if (HasStateAuthority && !Completed)
                ResetWires();

            ApplyVisuals();
        }

        public override bool CanDragPart(int partIndex)
        {
            if (!base.CanDragPart(partIndex) || !validSetup)
                return false;

            return partIndex >= 0 && partIndex < WireCount && ConnectedEnds[partIndex] == WiringRules.NotConnected;
        }

        protected override void OnPartsConnected(PlayerRef actor, int fromPart, int toPart)
        {
            base.OnPartsConnected(actor, fromPart, toPart);

            if (!validSetup || Completed)
                return;

            // W4: 색 · 빈 자리 판정 먼저 (다른 색이면 조용히 취소 — 옛 코드와 같음)
            if (!WiringRules.CanConnect(ReadColors(), ReadEnds(), ReadEndColors(), fromPart, toPart))
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

            ConnectedEnds.Set(fromPart, toPart);
        }

        protected override void OnPartPressed(PlayerRef actor, int partIndex)
        {
            base.OnPartPressed(actor, partIndex);

            if (partIndex != LeverPart || Completed || !validSetup)
                return;

            if (Operator != actor || !WiringRules.AllConnected(ReadEnds()))
                return;

            CompleteBy(actor);
        }

        protected override void OnOperationCanceled(PlayerRef actor)
        {
            base.OnOperationCanceled(actor);
            ResetWires();
        }

        protected override void OnResetHost()
        {
            base.OnResetHost();
            ResetWires();
        }

        protected override void OnStationRender()
        {
            base.OnStationRender();
            ApplyVisuals();
        }

        private void ResetWires()
        {
            if (!validSetup)
                return;

            WiringRules.ShuffleColors(colorBuffer, n => Random.Range(0, n));

            for (int i = 0; i < WireCount; i++)
            {
                StartColors.Set(i, colorBuffer[i]);
                ConnectedEnds.Set(i, WiringRules.NotConnected);
            }
        }

        private void ApplyVisuals()
        {
            if (!validSetup || Object == null || !Object.IsValid)
                return;

            for (int i = 0; i < WireCount; i++)
            {
                WireVisual wire = wires[i];

                if (wire == null)
                    continue;

                int color = StartColors[i];

                if (shownColors[i] != color)
                {
                    shownColors[i] = color;
                    wire.SetColor((WiringColor)color);
                }

                int end = ConnectedEnds[i];

                if (shownEnds[i] == end)
                    continue;

                shownEnds[i] = end;

                if (end >= 0 && end < WireCount && endAnchors[end] != null)
                    wire.ShowConnection(endAnchors[end]);
                else
                    wire.Hide();
            }
        }

        private int[] ReadColors()
        {
            for (int i = 0; i < WireCount; i++)
                colorBuffer[i] = StartColors[i];

            return colorBuffer;
        }

        private int[] ReadEnds()
        {
            for (int i = 0; i < WireCount; i++)
                endBuffer[i] = ConnectedEnds[i];

            return endBuffer;
        }

        private int[] ReadEndColors()
        {
            for (int i = 0; i < WireCount; i++)
                endColorBuffer[i] = (int)endColors[i];

            return endColorBuffer;
        }

        private static bool IsLength<T>(T[] array)
        {
            return array != null && array.Length == WireCount;
        }
    }
}
