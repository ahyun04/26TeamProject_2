using Fusion;
using UnityEngine;

namespace TrustNoOne.Missions
{
    /// <summary>
    /// [역할] 장치 안의 "누르고 있는 부품" (13a — 배관의 새는 곳). StationButton 의 누르고 있기 버전.
    ///  F 누름(IHoldInteractable.BeginHold) → station.BeginHoldPart(partIndex) / F 뗌 → EndHoldPart → 호스트의 HoldStation 이 판정한다.
    /// [NetworkObject 없음] 부품은 장치의 NetworkObject 를 대신 알려 준다 (TargetObject — StationButton 과 같은 규칙).
    /// [배치 규칙] 자기 콜라이더가 있는 오브젝트에 둔다. 감지기는 콜라이더에서 위로 올라가며 처음 만나는 ITargetable 을 잡는데,
    ///  장치(HoldStation)도 ITargetable · IHoldInteractable 이라 부품이 장치와 같은 오브젝트에 있으면 장치 자신의 F(번호 −1)가 된다.
    /// [팀원 코드] PlayerInteraction 은 수정하지 않는다 — 조준이 다른 대상으로 바뀌면 그쪽이 EndHold 를 불러 준다 (조준이 바뀌면 진행 0, PL13).
    /// [근거] 13a 명세 PL8 (접근법 1 — 장치 하나 + 누르고 있는 부품), PL9
    /// </summary>
    public class StationHoldPart : MonoBehaviour, ITargetable, IHoldInteractable
    {
        [SerializeField] private HoldStation station;

        [Tooltip("장치가 어느 부품인지 구분하는 번호. 예) 배관 새는 곳 0 ~ 5")]
        [SerializeField] private int partIndex;

        public NetworkObject TargetObject => station != null ? station.Object : null;

        /// <summary>이 부품이 붙은 장치.</summary>
        public HoldStation Station => station;

        /// <summary>장치가 어느 부품인지 구분하는 번호.</summary>
        public int PartIndex => partIndex;

        private void Awake()
        {
            if (station == null)
                station = GetComponentInParent<HoldStation>();
        }

        public void BeginHold()
        {
            if (station != null && !station.Completed)
                station.BeginHoldPart(partIndex);
        }

        public void EndHold()
        {
            if (station != null)
                station.EndHoldPart(partIndex);
        }
    }
}
