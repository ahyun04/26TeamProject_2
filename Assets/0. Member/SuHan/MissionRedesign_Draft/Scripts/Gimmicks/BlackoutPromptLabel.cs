using TMPro;
using TrustNoOne.Missions;
using UnityEngine;

namespace TrustNoOne.Gimmicks
{
    /// <summary>
    /// [역할] 살인마 암전 중 조준점 바로 아래에 화면 고정 글자 하나를 보인다.
    ///  내용 = 지금 켜진 조준 문구(PromptBillboard.LatestShown)의 글자 그대로 — 예: 밸브 "[F]키를 눌러 상호작용", 좌표 "[F] X축 이동".
    ///  지도 닫기 함수를 못 찾아 투명하게만 가렸는데 지도가 열려 있으면 "지도 닫기: M" 이 먼저 (BlackoutVisualRules.PickLabel).
    ///  진하기 = 어두움 → 어두워지는 1초 동안 장치 글자가 판 뒤로 사라지면서 화면 글자가 같이 나타난다.
    /// [이유] 장치 안내 글자는 장치에 붙은 3D 글자라 3D 를 가리면 같이 사라진다. 그대로 보이면 어둠 속에 장치 위치가 드러나므로
    ///  내용만 화면 가운데로 옮긴다 (질문 2 C). 문구가 없는 장치(외곽선만)는 아무것도 띄우지 않는다 (계획 중 질문 A).
    ///  글꼴은 원래 문구의 글꼴을 그대로 써서 한글이 깨지지 않게 한다.
    /// [근거] 16b 명세 KB27 · KB36 · KB38, 조사 1-5
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

            PromptBillboard billboard = PromptBillboard.LatestShown;
            TMP_Text source = billboard != null ? billboard.GetComponentInChildren<TMP_Text>(false) : null;
            string text = BlackoutVisualRules.PickLabel(source != null ? source.text : null, mapOpenWithoutClose);

            if (text == null)
            {
                Hide();
                return;
            }

            EnsureLabel();

            // 원래 문구의 글꼴을 따라간다 (한글 글자가 들어 있는 글꼴)
            if (source != null && source.font != null && label.font != source.font)
                label.font = source.font;

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

            // 지도 안내처럼 원래 문구가 없을 때를 위해 처음 글꼴은 화면에 있는 다른 TMP 글자에서 빌린다 (기본 글꼴엔 한글이 없을 수 있음).
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
            label.enableWordWrapping = false;
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
