using UnityEngine;

namespace TrustNoOne.Missions
{
    /// <summary>
    /// [역할] 렌더러 하나의 재질을 "원래 / 변형 N개" 중 하나로 바꿔 끼우는 작은 도우미 (불빛 표현용). 3a 명세 3-4 · C10.
    /// [왜 재질 교체인가] 원본 모델의 기본 재질(Texture_Atlas)은 발광이 꺼져 있어 색만 덧칠하면 빛나지 않는다.
    ///  발광이 켜진 네온 재질(Neon_blue · Neon_red · Neon_green)을 통째로 끼우면 불빛처럼 보인다.
    /// [안전] renderer.sharedMaterials 에 넣으므로 재질 에셋은 바뀌지 않고 복제본도 생기지 않는다.
    ///  바뀌는 건 "이 렌더러가 어느 재질을 쓰는지" 뿐이다.
    /// [성능] 변형마다 "모든 재질 칸을 그 재질로 채운 배열"을 처음에 한 번 만들어 둔다.
    ///  같은 번호를 다시 요청하면 아무것도 하지 않으므로 매 Render 에 불러도 된다.
    /// [쓰는 곳] 압력 피스톤 글자(PressureVisual), 코드 자판 숫자 버튼(CodeKeypadVisual).
    /// </summary>
    public sealed class MaterialSwapper
    {
        /// <summary>원래 재질로 되돌리는 번호.</summary>
        public const int Original = -1;

        private readonly Renderer renderer;
        private readonly Material[] originalMaterials;
        private readonly Material[][] variantMaterials;
        private int current = Original;

        /// <param name="renderer">바꿀 렌더러. 비어 있으면 이 도우미는 아무것도 하지 않는다</param>
        /// <param name="variants">변형 재질 (번호 0, 1, 2 …). 비어 있는 변형은 원래 재질로 보인다</param>
        public MaterialSwapper(Renderer renderer, params Material[] variants)
        {
            this.renderer = renderer;

            if (renderer == null)
                return;

            originalMaterials = renderer.sharedMaterials;
            int slots = Mathf.Max(1, originalMaterials.Length);
            variantMaterials = new Material[variants != null ? variants.Length : 0][];

            for (int v = 0; v < variantMaterials.Length; v++)
            {
                if (variants[v] == null)
                    continue;

                Material[] filled = new Material[slots];

                for (int i = 0; i < slots; i++)
                    filled[i] = variants[v];

                variantMaterials[v] = filled;
            }
        }

        /// <param name="variant">Original(−1) = 원래 재질, 0 이상 = 그 변형. 범위 밖이거나 비어 있는 변형이면 원래 재질</param>
        public void Show(int variant)
        {
            if (renderer == null || variant == current)
                return;

            current = variant;
            Material[] target = variant >= 0 && variant < variantMaterials.Length ? variantMaterials[variant] : null;
            renderer.sharedMaterials = target ?? originalMaterials;
        }
    }
}
