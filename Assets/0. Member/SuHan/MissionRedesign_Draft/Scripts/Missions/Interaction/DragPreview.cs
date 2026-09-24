using UnityEngine;

namespace TrustNoOne.Missions
{
    /// <summary>
    /// [역할] 끄는 부품(StationDragPart)을 끄는 동안 이 PC 에서만 보이는 연출 (예: 따라오는 전선). 2b 명세 2-4.
    ///  동기화하지 않는다 — 결과(연결됨)는 스테이션의 [Networked] 상태로 모두에게 보인다.
    /// [abstract 로 둔 이유] 끄는 부품은 "무엇을 그리는지" 몰라도 되게. 전선은 WireVisual, 다른 미니게임은 자기 연출을 만든다.
    /// </summary>
    public abstract class DragPreview : MonoBehaviour
    {
        public abstract void BeginPreview();

        public abstract void UpdatePreview(Ray aimRay);

        public abstract void CancelPreview();
    }
}
