using UnityEngine;

namespace TrustNoOne.Missions
{
    /// <summary>
    /// [역할] 생명 유지 장치 본체의 연출 묶음: 꽂힌 산소통 모습 · 경고 램프 · 폭발 섬광. 3b 명세 3-4.
    ///  무엇을 보일지는 LifeSupportStation 이 매 Render 에 정한다 (코드 자판의 CodeKeypadVisual 과 같은 역할).
    /// [경고 램프] 원래 재질(회색) = 꺼짐, 0 = 경고(발광 빨강 — 위험한 통 판정 중 깜빡임), 1 = 완료(발광 초록). MaterialSwapper (3a C9 · C10).
    /// [폭발 섬광] 구의 지름 = 크기. 피해 반경까지 커져 범위가 눈에 보인다 (LS5). 구 안쪽에서는 보이지 않는다 (자리 표시 한계 — 명세 7장).
    /// [성능] 바뀔 때만 적용하므로 매 Render 에 불러도 된다.
    /// </summary>
    public class LifeSupportVisual : MonoBehaviour
    {
        /// <summary>경고 램프 변형 번호: 경고 (빨강).</summary>
        public const int WarningLamp = 0;

        /// <summary>경고 램프 변형 번호: 완료 (초록).</summary>
        public const int DoneLamp = 1;

        [Tooltip("꽂힌 산소통 모습 (판정 중에만 보임)")]
        [SerializeField] private GameObject insertedTank;

        [Tooltip("경고 램프. 원래 재질 = 꺼짐")]
        [SerializeField] private Renderer warningLamp;

        [SerializeField] private Material warningMaterial;
        [SerializeField] private Material doneMaterial;

        [Tooltip("폭발 섬광 (구). 평소 숨김")]
        [SerializeField] private Transform flash;

        private MaterialSwapper lamp;
        private bool? insertedShown;
        private float shownFlash = -1f;

        private void Awake()
        {
            lamp = new MaterialSwapper(warningLamp, warningMaterial, doneMaterial);
        }

        /// <summary>꽂힌 산소통을 보이거나 숨긴다.</summary>
        public void ShowInserted(bool shown)
        {
            if (insertedShown == shown)
                return;

            insertedShown = shown;

            if (insertedTank != null)
                insertedTank.SetActive(shown);
        }

        /// <param name="variant">MaterialSwapper.Original(꺼짐) · WarningLamp · DoneLamp</param>
        public void ShowLamp(int variant)
        {
            lamp?.Show(variant);
        }

        /// <param name="diameter">섬광 지름(m). 0 이하면 숨김</param>
        public void ShowFlash(float diameter)
        {
            if (flash == null || diameter == shownFlash)
                return;

            shownFlash = diameter;
            bool visible = diameter > 0f;

            if (flash.gameObject.activeSelf != visible)
                flash.gameObject.SetActive(visible);

            if (visible)
                flash.localScale = Vector3.one * diameter;
        }
    }
}
