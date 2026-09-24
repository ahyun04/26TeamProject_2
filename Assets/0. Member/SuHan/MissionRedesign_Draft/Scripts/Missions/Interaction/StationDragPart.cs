using Fusion;
using UnityEngine;

namespace TrustNoOne.Missions
{
    /// <summary>
    /// [역할] 마우스를 누른 채 끌어서 쓰는 부품 (예: 전선 시작점). 옛 WireStartPoint 의 입력 부분. 2b 명세 2-2.
    ///  좌클릭 누름 → (스테이션이 허락하면) 미리보기 시작 → 끄는 동안 미리보기 갱신
    ///  → 뗀 곳이 같은 스테이션의 StationDropTarget 이면 station.ConnectParts(내 번호, 그 번호) → 미리보기 취소
    ///  입력 호출은 플레이어 쪽 PlayerInteraction 이 이미 해 준다 (IDragInteractable).
    ///
    /// [판정은 호스트] 여기서는 색 등을 검사하지 않는다. 연결 여부는 호스트의 OnPartsConnected 가 정하고, 결과는 동기화된 상태로 보인다.
    ///  CanDragPart 는 "이미 이은 전선은 끌리지 않게" 같은 연출용 검사일 뿐이다.
    /// [행동 불가 · 허공에 놓음] PlayerInteraction 이 EndDrag(null) 또는 다른 대상을 넘긴다 → 연결 없이 취소.
    /// [배치 규칙] StationButton 과 같다 — 자기 콜라이더가 있는 오브젝트에 둔다 (스테이션과 같은 오브젝트면 스테이션이 먼저 잡힌다).
    /// </summary>
    public class StationDragPart : MonoBehaviour, ITargetable, IDragInteractable
    {
        [SerializeField] private MissionStation station;

        [Tooltip("스테이션이 어느 끄는 부품인지 구분하는 번호. 예) 전선 시작점 0~3")]
        [SerializeField] private int partIndex;

        [Tooltip("끄는 동안 이 PC 에서만 보이는 연출 (선택).")]
        [SerializeField] private DragPreview preview;

        private bool dragging;

        public NetworkObject TargetObject => station != null ? station.Object : null;
        public MissionStation Station => station;
        public int PartIndex => partIndex;

        private void Awake()
        {
            if (station == null)
                station = GetComponentInParent<MissionStation>();
        }

        public void BeginDrag()
        {
            if (station == null || !station.CanDragPart(partIndex))
                return;

            dragging = true;

            if (preview != null)
                preview.BeginPreview();
        }

        public void UpdateDrag(Ray aimRay)
        {
            if (dragging && preview != null)
                preview.UpdatePreview(aimRay);
        }

        public void EndDrag(ITargetable releaseTarget)
        {
            if (!dragging)
                return;

            dragging = false;

            if (releaseTarget is StationDropTarget drop && station != null && drop.Station == station)
                station.ConnectParts(partIndex, drop.PartIndex);

            if (preview != null)
                preview.CancelPreview();
        }

        private void OnDisable()
        {
            dragging = false;

            if (preview != null)
                preview.CancelPreview();
        }
    }
}
