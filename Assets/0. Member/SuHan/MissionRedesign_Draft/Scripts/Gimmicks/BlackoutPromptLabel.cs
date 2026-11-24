using TMPro;
using UnityEngine;

namespace TrustNoOne.Gimmicks
{
    /// <summary>
    /// [역할] 살인마 암전 중 조준점 바로 아래에 화면 고정 글자 하나를 보인다 — (16d) 지도 닫기 함수를 못 찾아 투명하게만 가렸는데
    ///  지도가 열려 있을 때의 "지도 닫기: M" 만 (BlackoutVisualRules.PickLabel). 진하기 = 어두움.
    /// [이유] (16d KB56 — KB27 · KB36 바뀜) 16b 에서는 3D 를 다 가려 장치의 3D 안내 글자를 이 화면 글자로 옮겼지만,
    ///  이제 가까운 곳(조준 거리 2m 포함)은 보이므로 장치 글자 · 외곽선을 평소대로 쓴다. 지도 안내는 지도 쪽 문제라 남긴다.
    ///  글꼴은 화면에 있는 다른 TMP 글자에서 빌려 한글이 깨지지 않게 한다.
    /// [근거] 16b 명세 KB29 · KB38, 16d 명세 KB56 · KB61
    /// </summary>
    public class BlackoutPromptLabel : MonoBehaviour
    {
        [Tooltip("조준점(화면 가운데)에서 아래로 몇 픽셀 (Play 중에 바꿔 보고 정한 숫자를 알려 주세요)")]
        [SerializeField] private float offsetBelowCenter = 70f;

        [Tooltip("글자 크기")]
        [SerializeField] private float fontSize = 28f;

        [Tooltip("화면 캔버스 순서 — PlayerHUD(10) 위, 사망 화면 · 연결 끊김 창(1000) 아래 (조사 1-5)")]
        [SerializeField] private int sortingOrder = 20;

        private Canvas canvas;
        private TextMeshProUGUI label;

        /// <summary>지금 띄운 글자 (없으면 null) — 디버그 패널 (KB40)</summary>
        public string CurrentText { get; private set; }

        /// <summary>규칙의 결과대로 글자를 띄우거나 숨긴다</summary>
        public void Apply(BlackoutVisualState state, bool mapOpenWithoutClose)
        {
            if (!state.Active)
            {
                Hide();
                return;
            }

            string text = BlackoutVisualRules.PickLabel(mapOpenWithoutClose);

            if (text == null)
            {
                Hide();
                return;
            }

            EnsureLabel();

            label.text = text;
            label.fontSize = fontSize;
            label.color = new Color(1f, 1f, 1f, state.LabelAlpha);
            label.rectTransform.anchoredPosition = new Vector2(0f, -offsetBelowCenter);
            canvas.sortingOrder = sortingOrder;
            canvas.enabled = true;
            CurrentText = text;
        }

        /// <summary>KB35: 글자를 숨긴다</summary>
        public void Hide()
        {
            if (canvas != null)
                canvas.enabled = false;

            CurrentText = null;
        }

        private void OnDisable()
        {
            Hide();
        }

        private void OnDestroy()
        {
            // 캔버스는 씬 맨 위에 따로 만들었다 → 같이 지운다
            if (canvas != null)
                Destroy(canvas.gameObject);
        }

        /// <summary>처음 필요할 때 화면 캔버스(Overlay) + 가운데 정렬 글자 하나를 코드로 만든다 (프리팹 · 에셋 없음)</summary>
        private void EnsureLabel()
        {
            if (canvas != null)
                return;

            // 글꼴은 화면에 있는 다른 TMP 글자에서 빌린다 (기본 글꼴엔 한글이 없을 수 있음).
            // 내 글자를 만들기 전에 찾아야 자기 자신을 집지 않는다
            TextMeshProUGUI other = FindFirstObjectByType<TextMeshProUGUI>();

            GameObject canvasObject = new GameObject("KillerBlackoutPromptLabel");
            canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;

            GameObject textObject = new GameObject("Label");
            textObject.transform.SetParent(canvasObject.transform, false);
            label = textObject.AddComponent<TextMeshProUGUI>();
            label.alignment = TextAlignmentOptions.Center;
            label.textWrappingMode = TextWrappingModes.NoWrap;   // 줄바꿈 안 함 (Unity 6 · ugui 2.0 — 옛 enableWordWrapping = false)
            label.raycastTarget = false;   // 클릭을 막지 않는다

            if (other != null && other.font != null)
                label.font = other.font;

            RectTransform rect = label.rectTransform;
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(900f, 60f);
        }
    }
}
