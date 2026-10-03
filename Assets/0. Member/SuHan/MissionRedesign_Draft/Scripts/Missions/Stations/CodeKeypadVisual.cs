using UnityEngine;

namespace TrustNoOne.Missions
{
    /// <summary>
    /// [역할] 코드 자판 숫자 버튼 9개의 불빛. CodeStation 이 매 Render 에 무엇을 보일지 알려 준다. 3a 명세 3-3.
    ///  - 점등(ShowLit): 지금 켜질 숫자 하나만 파랑, 나머지는 원래 재질
    ///  - 실패(ShowFailed): 전부 빨강 (보이는 시간은 CodeStation 이 판단)
    ///  - 완료(ShowDone): 전부 초록
    /// [재질 교체 — C9] 원본 자판 모델의 숫자 버튼에는 네온 재질이 연결만 돼 있고 칠해진 면이 없다
    ///  → 버튼 전체 재질을 네온으로 바꿔 끼운다 (MaterialSwapper). 바뀐 버튼만 재질을 바꾸므로 매 Render 에 불러도 된다.
    /// [순서] 배열 번호 = 숫자 − 1 = CodeStation 의 부품 번호 (0 ~ 8).
    /// </summary>
    public class CodeKeypadVisual : MonoBehaviour
    {
        // MaterialSwapper 변형 번호 (생성자에 넣는 순서)
        private const int LitVariant = 0;
        private const int FailedVariant = 1;
        private const int DoneVariant = 2;

        [Tooltip("숫자 1 ~ 9 버튼 렌더러 (순서대로 9개)")]
        [SerializeField] private Renderer[] digitRenderers;
        [Tooltip("점등했을 때 끼울 재질 (Neon_blue)")]
        [SerializeField] private Material litMaterial;
        [Tooltip("틀렸을 때 끼울 재질 (Neon_red)")]
        [SerializeField] private Material failMaterial;
        [Tooltip("완료했을 때 끼울 재질 (Neon_green)")]
        [SerializeField] private Material doneMaterial;

        private MaterialSwapper[] swappers;

        /// <summary>숫자 버튼 렌더러가 9개 모두 연결됐는가 (CodeStation 이 생성 때 검사).</summary>
        public bool IsValid
        {
            get
            {
                if (digitRenderers == null || digitRenderers.Length != CodeRules.DigitCount)
                    return false;

                foreach (Renderer digitRenderer in digitRenderers)
                {
                    if (digitRenderer == null)
                        return false;
                }

                return true;
            }
        }

        /// <param name="digit">켜질 숫자(0 ~ 8). −1 이면 모두 원래 재질</param>
        public void ShowLit(int digit)
        {
            MaterialSwapper[] all = Swappers();

            for (int i = 0; i < all.Length; i++)
                all[i].Show(i == digit ? LitVariant : MaterialSwapper.Original);
        }

        public void ShowFailed() => ShowAll(FailedVariant);

        public void ShowDone() => ShowAll(DoneVariant);

        private void ShowAll(int variant)
        {
            foreach (MaterialSwapper swapper in Swappers())
                swapper.Show(variant);
        }

        private MaterialSwapper[] Swappers()
        {
            if (swappers != null)
                return swappers;

            int count = digitRenderers != null ? digitRenderers.Length : 0;
            swappers = new MaterialSwapper[count];

            for (int i = 0; i < count; i++)
                swappers[i] = new MaterialSwapper(digitRenderers[i], litMaterial, failMaterial, doneMaterial);

            return swappers;
        }
    }
}
