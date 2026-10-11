using UnityEngine;

namespace TrustNoOne.Missions
{
    /// <summary>
    /// [역할] 생명 유지 장치 본체의 연출 묶음: 꽂힌 산소통 모습 · 폭발 섬광. 3b 명세 3-4.
    ///  무엇을 보일지는 LifeSupportStation 이 매 Render 에 정한다 (코드 자판의 CodeKeypadVisual 과 같은 역할).
    /// [경고 램프 없음 — 3b 보강 명세 LM7] 실제 모델에 램프가 없고 램프를 덧붙이지 않기로 했다 (사용자 결정 B).
    ///  위험한 통 판정 중 경고는 본체 화면의 WARNING 글자 깜빡임이 대신한다 (LM8 — LifeSupportStation).
    /// [폭발 섬광] 구의 지름 = 크기. 피해 반경까지 커져 범위가 눈에 보인다 (LS5). 구 안쪽에서는 보이지 않는다 (3b 명세 7장).
    /// [성능] 바뀔 때만 적용하므로 매 Render 에 불러도 된다.
    /// </summary>
    public class LifeSupportVisual : MonoBehaviour
    {
        [Tooltip("꽂힌 산소통 모습 (판정 중에만 보임)")]
        [SerializeField] private GameObject insertedTank;

        [Tooltip("폭발 섬광 (구). 평소 숨김")]
        [SerializeField] private Transform flash;

        private bool? insertedShown;
        private float shownFlash = -1f;

        /// <summary>꽂힌 산소통을 보이거나 숨긴다.</summary>
        public void ShowInserted(bool shown)
        {
            if (insertedShown == shown)
                return;

            insertedShown = shown;

            if (insertedTank != null)
                insertedTank.SetActive(shown);
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
