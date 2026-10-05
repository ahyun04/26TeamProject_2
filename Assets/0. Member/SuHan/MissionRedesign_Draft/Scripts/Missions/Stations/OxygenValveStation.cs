using UnityEngine;

namespace TrustNoOne.Missions
{
    /// <summary>
    /// [역할] 산소 밸브 하나 (단체 미션 TG004 "생명 유지 장치 복구" · LifeSupportRestored). 신규. 3b 명세 3-1.
    ///  F 를 누르고 있는 동안 손잡이가 돌고, 이번 판 목표 회전량(360~720°)에 도달하면 열림 → 완료 이벤트 1개 + 잠금(Lock).
    ///  손을 떼거나 범위를 벗어나면 이 밸브만 처음 각도로 돌아간다 (개인 밸브와 같은 조작 — 명세 L4).
    ///
    /// [미션 규칙은 여기 없다 — L2 · L6] "4개 모두 · 첫 밸브가 열린 순간부터 90초"는 미션 데이터(TG004: 개수 4, 제한 시간,
    ///  SinceFirstProgress)가 센다. 시간이 초과되면 MissionManager 가 미션을 초기화하고, 같은 완료 이벤트를 쓰는 밸브들이
    ///  스스로 ResetStation 으로 잠김 상태(처음 각도 · 새 목표 각도)로 돌아간다 — 발전기와 같은 경로.
    /// [상태 램프 — L5] 손잡이가 둥근 바퀴라 돌아간 각도로는 열렸는지 알 수 없다 → 배관의 램프로 보여 준다. 잠김 빨강 / 열림 초록.
    ///  원본 모델 기본 재질(아틀라스)은 발광이 꺼져 있어 발광 네온 재질을 통째로 바꿔 끼운다 (MaterialSwapper, 3a C9 · C10).
    /// [동기화 — L8] 새 동기화 값 없음. 램프는 동기화되는 Completed, 손잡이 각도는 동기화되는 진행률(부모)로 그린다 → 게스트도 같은 장면.
    /// [개인 밸브와의 관계 — L6] ValveStation(개인 미션 PS001 전용)을 상속하지 않고 같은 부모(RotationHoldStation)를 쓰는 형제로 둔다.
    /// </summary>
    public class OxygenValveStation : RotationHoldStation
    {
        /// <summary>MaterialSwapper 변형 번호: 잠김 (빨강).</summary>
        public const int ClosedLamp = 0;

        /// <summary>MaterialSwapper 변형 번호: 열림 (초록).</summary>
        public const int OpenLamp = 1;

        [Header("상태 램프")]
        [Tooltip("배관의 상태 램프. 비어 있으면 램프 없이 동작")]
        [SerializeField] private Renderer lampRenderer;

        [Tooltip("잠김 램프 재질 (발광 빨강)")]
        [SerializeField] private Material closedMaterial;

        [Tooltip("열림 램프 재질 (발광 초록)")]
        [SerializeField] private Material openMaterial;

        private MaterialSwapper lamp;

        // 손잡이 원판은 정면 축으로 돈다. 실제 축(여는 방향)은 조립할 때 rotationAxis 에 넣는다
        protected override Vector3 FallbackAxis => Vector3.forward;

        protected override void OnStationSpawned()
        {
            base.OnStationSpawned();
            lamp = new MaterialSwapper(lampRenderer, closedMaterial, openMaterial);
            ShowLamp();
        }

        protected override void OnStationRender()
        {
            base.OnStationRender();
            ShowLamp();
        }

        /// <summary>열림(잠금 완료)이면 초록, 아니면 빨강. 같은 값이면 MaterialSwapper 가 아무것도 하지 않는다.</summary>
        private void ShowLamp()
        {
            if (lamp == null || Object == null || !Object.IsValid)
                return;

            lamp.Show(Completed ? OpenLamp : ClosedLamp);
        }
    }
}
