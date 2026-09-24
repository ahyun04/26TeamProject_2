using Fusion;
using UnityEngine;

namespace TrustNoOne.Missions
{
    /// <summary>
    /// [역할] 끄는 부품을 "놓는 곳" (예: 전선 도착점). 조준 대상이기만 하고 입력은 받지 않는다. 2b 명세 2-3.
    ///  StationDragPart 가 놓을 때 이 컴포넌트를 보고 station.ConnectParts(끄는 번호, 이 번호) 를 부른다.
    /// [ITargetable 인 이유] 플레이어 감지기(PlayerTargetDetector)가 놓는 순간 조준한 대상을 알려 주는데, ITargetable 만 잡힌다.
    /// [배치 규칙] StationButton 과 같다 — 자기 콜라이더가 있는 오브젝트에 둔다.
    /// </summary>
    public class StationDropTarget : MonoBehaviour, ITargetable
    {
        [SerializeField] private MissionStation station;

        [Tooltip("스테이션이 어느 놓는 곳인지 구분하는 번호. 예) 전선 도착점 0~3")]
        [SerializeField] private int partIndex;

        public NetworkObject TargetObject => station != null ? station.Object : null;
        public MissionStation Station => station;
        public int PartIndex => partIndex;

        private void Awake()
        {
            if (station == null)
                station = GetComponentInParent<MissionStation>();
        }
    }
}
