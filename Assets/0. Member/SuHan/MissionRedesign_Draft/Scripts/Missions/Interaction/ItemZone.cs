using UnityEngine;

namespace TrustNoOne.Missions
{
    /// <summary>
    /// [역할] 아이템이 바닥에 생길 수 있는 구역 상자 하나 (12b 명세 IS6). 자리 관리자(ItemSpotDirector)의 자식으로 둔다.
    ///  상자 = 이 오브젝트의 위치 · 회전 · 크기(Scale)를 씌운 단위 상자 — 기획자가 씬에서 이동 · 크기 도구로 바로 고친다.
    /// [콜라이더 없음] 플레이어 이동 · 조준 · NavMesh 굽기에 영향을 주지 않는다. 씬 뷰에만 반투명 상자로 보인다 (Gizmos — 게임 화면에는 안 보임).
    /// [근거] 12b 명세 IS4 (사용자 결정 A — 메뉴가 씬에 구역 상자) · IS6
    /// </summary>
    public class ItemZone : MonoBehaviour
    {
        private static readonly Color FillColor = new Color(1f, 0.35f, 0.25f, 0.15f);
        private static readonly Color LineColor = new Color(1f, 0.35f, 0.25f, 0.9f);

        /// <summary>상자 크기 (m) = 오브젝트 크기.</summary>
        public Vector3 Size => transform.lossyScale;

        /// <summary>단위 상자 바닥 공간(x, z: −0.5 ~ 0.5)의 점 → 월드 (높이는 상자 가운데).</summary>
        public Vector3 ToWorld(Vector2 local)
        {
            return transform.TransformPoint(new Vector3(local.x, 0f, local.y));
        }

        /// <summary>월드 점이 상자 가로 범위 안인가 (높이는 보지 않는다).</summary>
        public bool Contains(Vector3 world)
        {
            Vector3 local = transform.InverseTransformPoint(world);
            return Mathf.Abs(local.x) <= 0.5f && Mathf.Abs(local.z) <= 0.5f;
        }

        private void OnDrawGizmos()
        {
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = FillColor;
            Gizmos.DrawCube(Vector3.zero, Vector3.one);
            Gizmos.color = LineColor;
            Gizmos.DrawWireCube(Vector3.zero, Vector3.one);
        }
    }
}
