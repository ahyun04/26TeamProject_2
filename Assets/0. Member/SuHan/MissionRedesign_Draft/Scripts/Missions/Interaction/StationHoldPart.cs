using Fusion;
using UnityEngine;

namespace TrustNoOne.Missions
{
    /// <summary>
    /// [역할] 장치 안의 "누르고 있는 부품" (13a — 배관의 새는 곳, 13b — 좌표 장치의 초록 · 노랑 버튼). StationButton 의 누르고 있기 버전.
    ///  F 누름(IHoldInteractable.BeginHold) → 장치.BeginHoldPart(partIndex) / F 뗌 → EndHoldPart → 호스트의 장치가 판정한다.
    /// [받는 장치 — 13b CO27] 장치 칸은 MissionStation 이고, 누름 · 뗌은 IHoldPartReceiver 로 넘긴다 (배관 HoldStation · 좌표 CoordinateStation).
    ///  13a 에서는 칸 종류가 HoldStation 이었다 — 같은 부품을 가리키므로 배관 프리팹의 연결은 그대로 남는다.
    /// [NetworkObject 없음] 부품은 장치의 NetworkObject 를 대신 알려 준다 (TargetObject — StationButton 과 같은 규칙).
    /// [배치 규칙] 자기 콜라이더가 있는 오브젝트에 둔다. 감지기는 콜라이더에서 위로 올라가며 처음 만나는 ITargetable 을 잡는데,
    ///  장치(HoldStation)도 ITargetable · IHoldInteractable 이라 부품이 장치와 같은 오브젝트에 있으면 장치 자신의 F(번호 −1)가 된다.
    /// [팀원 코드] PlayerInteraction 은 수정하지 않는다 — 조준이 다른 대상으로 바뀌면 그쪽이 EndHold 를 불러 준다 (조준이 바뀌면 진행 0, PL13).
    /// [근거] 13a 명세 PL8 (접근법 1 — 장치 하나 + 누르고 있는 부품), PL9, 13b 명세 CO27
    /// </summary>
    public class StationHoldPart : MonoBehaviour, ITargetable, IHoldInteractable
    {
        [Tooltip("이 부품을 받는 장치 (IHoldPartReceiver 를 구현한 MissionStation — 배관 · 좌표 장치)")]
        [SerializeField] private MissionStation station;

        [Tooltip("장치가 어느 부품인지 구분하는 번호. 예) 배관 새는 곳 0 ~ 5, 좌표 장치 초록 0 · 노랑 1")]
        [SerializeField] private int partIndex;

        public NetworkObject TargetObject => station != null ? station.Object : null;

        /// <summary>이 부품이 붙은 장치.</summary>
        public MissionStation Station => station;

        /// <summary>장치가 어느 부품인지 구분하는 번호.</summary>
        public int PartIndex => partIndex;

        private void Awake()
        {
            if (station == null)
            {
                // 부모 중에서 누르고 있기를 받는 장치를 찾는다 (가장 가까운 것)
                foreach (MissionStation candidate in GetComponentsInParent<MissionStation>(true))
                {
                    if (candidate is IHoldPartReceiver)
                    {
                        station = candidate;
                        break;
                    }
                }
            }

            if (station != null && !(station is IHoldPartReceiver))
                Debug.LogWarning($"[StationHoldPart] {name}: 장치 {station.name} 이(가) 누르고 있기(IHoldPartReceiver)를 받지 않습니다.", this);
        }

        public void BeginHold()
        {
            if (station != null && !station.Completed && station is IHoldPartReceiver receiver)
                receiver.BeginHoldPart(partIndex);
        }

        public void EndHold()
        {
            if (station is IHoldPartReceiver receiver)
                receiver.EndHoldPart(partIndex);
        }
    }
}
