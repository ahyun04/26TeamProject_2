using UnityEngine;

namespace TrustNoOne.Missions
{
    /// <summary>
    /// [역할] 선반 칸 하나 (12b 명세 IS7). 선반의 자식 — 선반을 옮기면 같이 움직인다.
    ///  위치 = 칸 바닥판 가운데, 이 오브젝트의 x = 칸 폭 방향, y = 위, z(앞) = 칸이 열린 쪽. 칸 크기(폭 · 높이 · 깊이, m)는 메뉴가 선반 모델을 분석해 넣는다 (ShelfSlotRules).
    ///  기획자가 이 오브젝트를 지우면 그 칸에는 아이템이 생기지 않는다.
    /// [콜라이더 없음] 씬 뷰에만 칸 상자가 보인다 (Gizmos).
    /// [근거] 12b 명세 IS7 · IS8
    /// </summary>
    public class ShelfSlot : MonoBehaviour
    {
        [Tooltip("칸 폭 (m) — 이 오브젝트의 x 방향")]
        [SerializeField] private float width;

        [Tooltip("칸 높이 (m) — 판에서 윗판 아랫면까지")]
        [SerializeField] private float height;

        [Tooltip("칸 깊이 (m) — 이 오브젝트의 z(앞) 방향")]
        [SerializeField] private float depth;

        private static readonly Color LineColor = new Color(0.2f, 0.8f, 1f, 0.9f);

        public float Width => width;
        public float Height => height;
        public float Depth => depth;

        /// <summary>메뉴(에디터)가 칸 크기를 넣는다.</summary>
        public void Configure(float slotWidth, float slotHeight, float slotDepth)
        {
            width = slotWidth;
            height = slotHeight;
            depth = slotDepth;
        }

        private void OnDrawGizmos()
        {
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = LineColor;
            Gizmos.DrawWireCube(new Vector3(0f, height * 0.5f, 0f), new Vector3(width, height, depth));
        }
    }
}
