using UnityEngine;

namespace TrustNoOne.Missions
{
    /// <summary>
    /// [역할] 방화문 문짝 열림 연출 · 문짝 충돌. 열림 진행률은 FireDoorStation 이 시각 공식으로 준다. 3d 명세 3-5.
    /// [모양 — FD14] 원본 모델의 문짝이 위 · 아래 두 쪽 → 위 문짝은 자기 높이만큼 위로, 아래 문짝은 자기 높이만큼 아래로 (감속).
    /// [충돌] 다 열리면 문짝 충돌을 끈다 → 출입구로 지나갈 수 있다. 문틀 양쪽 기둥 충돌은 그대로 둔다 (변환기가 따로 만든다).
    /// [성능] 바뀔 때만 적용한다 (열리는 2초 동안만 매 프레임).
    /// </summary>
    public class FireDoorVisual : MonoBehaviour
    {
        [SerializeField] private Transform upperDoor;
        [SerializeField] private Transform lowerDoor;

        [Tooltip("위 문짝이 올라가는 거리 (m, 조립 때 문짝 높이)")]
        [SerializeField] private float upperTravel = 1.36f;

        [Tooltip("아래 문짝이 내려가는 거리 (m, 조립 때 문짝 높이)")]
        [SerializeField] private float lowerTravel = 1.35f;

        [Tooltip("다 열리면 끄는 문짝 충돌")]
        [SerializeField] private Collider[] doorColliders;

        private Vector3 upperStart;
        private Vector3 lowerStart;
        private float shownProgress = -2f;

        private void Awake()
        {
            if (upperDoor != null)
                upperStart = upperDoor.localPosition;

            if (lowerDoor != null)
                lowerStart = lowerDoor.localPosition;
        }

        /// <param name="progress">−1 = 닫힘, 0 ~ 1 = 열리는 중, 1 = 다 열림</param>
        public void ShowOpen(float progress)
        {
            if (progress == shownProgress)
                return;

            shownProgress = progress;
            float p = progress < 0f ? 0f : progress;
            float eased = 1f - (1f - p) * (1f - p);

            if (upperDoor != null)
                upperDoor.localPosition = upperStart + Vector3.up * (upperTravel * eased);

            if (lowerDoor != null)
                lowerDoor.localPosition = lowerStart + Vector3.down * (lowerTravel * eased);

            bool open = progress >= 1f;

            if (doorColliders == null)
                return;

            foreach (Collider doorCollider in doorColliders)
            {
                if (doorCollider != null)
                    doorCollider.enabled = !open;
            }
        }
    }
}
