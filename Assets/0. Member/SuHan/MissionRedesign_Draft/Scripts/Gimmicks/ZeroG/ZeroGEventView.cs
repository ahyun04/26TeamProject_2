using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TrustNoOne.Gimmicks
{
    /// <summary>
    /// [역할] 무중력 사건 화면 (관리자 오브젝트에 붙음 — 모든 PC, 죽은 관전자도): 경고 단계에는 경고 문구 + 카운트다운 숫자,
    ///  떠오르기 · 무중력 단계에는 팀원 시간 글자 바로 아래 게이지(얇은 막대 + "중력 복구까지 0:42"), 그 밖에는 아무것도 없음.
    ///  처음 필요할 때 Overlay 캔버스를 코드로 만든다 (프리팹 · 에셋 없음 — 16b BlackoutPromptLabel 과 같은 방식).
    /// [이유] 팀원 시간 글자 프리팹(GameFlowUI)은 고치지 않는다 — 같은 기준 화면(1920 × 1080 · 맞춤 0.5)과 같은 글꼴로 내 캔버스에 그려
    ///  그 아래 자리에 맞춘다. 정렬 4990 = 팀원 캔버스(5000) 바로 아래 — 결과 화면을 가리지 않게.
    /// [근거] 2차 피드백 "경고 문구("5초 뒤에 무중력 상태에 들어갑니다") 뜬 후에 5초 카운트 다운" · "맵 시작 시간 하단에 … 게이지" —
    ///  17b 명세 ZG49 · ZG61 (자리 · 크기 · 색은 시안 Verification~/Plan17/mock17b_gauge.py · mock17b_warning.py 와 같은 숫자)
    /// </summary>
    [RequireComponent(typeof(ZeroGEventManager))]
    public class ZeroGEventView : MonoBehaviour
    {
        // 글꼴: 팀원 시간 글자와 같은 NotoSansKR Medium (Resources 폴더 — 1-3). Inspector 칸이 비었을 때 이 경로로 불러온다
        private const string FontResourcePath = "Fonts & Materials/NotoSansKR/NotoSansKR-Medium SDF";

        [Tooltip("글꼴 — 비어 있으면 TextMesh Pro Resources 의 NotoSansKR-Medium SDF (팀원 시간 글자와 같은 글꼴)")]
        [SerializeField] private TMP_FontAsset font;

        [Tooltip("팀원 캔버스(GameFlowUI 5000) 바로 아래 — 결과 화면을 가리지 않게 (Claude 제안)")]
        [SerializeField] private int sortingOrder = 4990;

        [Header("경고 (Claude 제안 — 시안 mock17b_warning.png)")]
        [Tooltip("{0} = 경고 길이(초). 피드백 문구 그대로")]
        [SerializeField] private string warningFormat = "{0}초 뒤에 무중력 상태에 들어갑니다";
        [Tooltip("화면 위에서부터 경고 문구 가운데까지 (기준 1080)")]
        [SerializeField] private float warningY = 250f;
        [SerializeField] private float warningFontSize = 34f;
        [SerializeField] private Color warningColor = new Color(1f, 196f / 255f, 64f / 255f);
        [Tooltip("화면 위에서부터 카운트다운 숫자 가운데까지")]
        [SerializeField] private float countdownY = 340f;
        [SerializeField] private float countdownFontSize = 96f;

        [Header("게이지 (질문 7 A — 팀원 시간 글자 y 90 · 높이 60 바로 아래)")]
        [Tooltip("화면 위에서부터 막대 윗변까지 (시간 글자 아랫변 120 + 12)")]
        [SerializeField] private float gaugeTop = 132f;
        [SerializeField] private Vector2 gaugeSize = new Vector2(320f, 8f);
        [SerializeField] private Color gaugeTrackColor = new Color(1f, 1f, 1f, 0.25f);
        [SerializeField] private Color gaugeFillColor = new Color(95f / 255f, 211f / 255f, 1f);
        [Tooltip("화면 위에서부터 게이지 글자 가운데까지")]
        [SerializeField] private float gaugeLabelY = 160f;
        [SerializeField] private float gaugeLabelFontSize = 18f;
        [SerializeField] private Color gaugeLabelColor = new Color(220f / 255f, 240f / 255f, 1f);
        [SerializeField] private string gaugeFormat = "중력 복구까지 {0}";

        private ZeroGEventManager manager;
        private Canvas canvas;
        private TextMeshProUGUI warningLabel;
        private TextMeshProUGUI countdownLabel;
        private RectTransform gaugeRoot;
        private RectTransform gaugeFill;
        private TextMeshProUGUI gaugeLabel;
        private int shownCountdown = -1;
        private string shownClock;

        private void Awake()
        {
            manager = GetComponent<ZeroGEventManager>();
        }

        private void OnDestroy()
        {
            if (canvas != null)
                Destroy(canvas.gameObject);
        }

        private void LateUpdate()
        {
            ZeroGState state = manager != null ? manager.Evaluate() : ZeroGState.Of(ZeroGPhase.None);
            bool warning = state.Phase == ZeroGPhase.Warning;
            bool gauge = ZeroGSchedule.UsesZeroGMovement(state.Phase);

            if (!warning && !gauge)
            {
                if (canvas != null && canvas.enabled)
                    canvas.enabled = false;

                return;
            }

            EnsureCanvas();
            canvas.enabled = true;
            warningLabel.gameObject.SetActive(warning);
            countdownLabel.gameObject.SetActive(warning);
            gaugeRoot.gameObject.SetActive(gauge);

            if (warning)
            {
                warningLabel.text = string.Format(warningFormat, Mathf.RoundToInt(manager.WarningSeconds));

                if (shownCountdown != state.Countdown)
                {
                    shownCountdown = state.Countdown;
                    countdownLabel.text = state.Countdown.ToString();
                }
            }

            if (gauge)
            {
                gaugeFill.sizeDelta = new Vector2(gaugeSize.x * Mathf.Clamp01(state.Gauge), gaugeSize.y);
                string clock = ZeroGSchedule.FormatClock(state.SecondsToRestore);

                if (shownClock != clock)
                {
                    shownClock = clock;
                    gaugeLabel.text = string.Format(gaugeFormat, clock);
                }
            }
        }

        // ═════════════════════════════════════════════════════════════
        //  만들기 (처음 한 번)
        // ═════════════════════════════════════════════════════════════

        private void EnsureCanvas()
        {
            if (canvas != null)
                return;

            if (font == null)
                font = Resources.Load<TMP_FontAsset>(FontResourcePath);

            if (font == null)
                Debug.LogWarning($"[ZeroGEventView] 글꼴을 찾지 못해 TMP 기본 글꼴로 그립니다 (한글이 깨질 수 있음): Resources/{FontResourcePath}");

            GameObject canvasObject = new GameObject("ZeroGEventView (화면)");
            canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;

            // 팀원 GameFlowUI 와 같은 기준 화면 — 해상도가 달라도 시간 글자와 같은 비율로 커지고 작아진다 (1-3)
            CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            warningLabel = CreateLabel(canvasObject.transform, "경고 문구", warningY, new Vector2(1200f, 60f), warningFontSize, warningColor, true);
            countdownLabel = CreateLabel(canvasObject.transform, "카운트다운", countdownY, new Vector2(300f, 130f), countdownFontSize, Color.white, true);

            gaugeRoot = CreateRect(canvasObject.transform, "게이지", gaugeTop + gaugeSize.y * 0.5f, gaugeSize);
            Image track = gaugeRoot.gameObject.AddComponent<Image>();
            track.color = gaugeTrackColor;
            track.raycastTarget = false;

            // 채움: 왼쪽 끝 기준 사각형의 너비를 줄인다 (그림 없이 Image.Filled 를 쓰지 않으려고)
            gaugeFill = new GameObject("채움", typeof(RectTransform)).GetComponent<RectTransform>();
            gaugeFill.SetParent(gaugeRoot, false);
            gaugeFill.anchorMin = new Vector2(0f, 0.5f);
            gaugeFill.anchorMax = new Vector2(0f, 0.5f);
            gaugeFill.pivot = new Vector2(0f, 0.5f);
            gaugeFill.anchoredPosition = Vector2.zero;
            gaugeFill.sizeDelta = gaugeSize;
            Image fill = gaugeFill.gameObject.AddComponent<Image>();
            fill.color = gaugeFillColor;
            fill.raycastTarget = false;

            gaugeLabel = CreateLabel(gaugeRoot, "게이지 글자", 0f, new Vector2(400f, 30f), gaugeLabelFontSize, gaugeLabelColor, false);
            // 게이지 글자는 막대 아래 — 막대 가운데에서 (글자 가운데 − 막대 가운데) 만큼 아래
            gaugeLabel.rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            gaugeLabel.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            gaugeLabel.rectTransform.anchoredPosition = new Vector2(0f, -(gaugeLabelY - (gaugeTop + gaugeSize.y * 0.5f)));

            canvas.enabled = false;
        }

        /// <summary>위 가운데 기준 사각형 — y = 화면 위에서부터 가운데까지 (기준 1080)</summary>
        private static RectTransform CreateRect(Transform parent, string name, float y, Vector2 size)
        {
            RectTransform rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = new Vector2(0.5f, 1f);
            rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = new Vector2(0f, -y);
            rect.sizeDelta = size;
            return rect;
        }

        private TextMeshProUGUI CreateLabel(Transform parent, string name, float y, Vector2 size, float fontSize, Color color, bool bold)
        {
            RectTransform rect = CreateRect(parent, name, y, size);
            TextMeshProUGUI label = rect.gameObject.AddComponent<TextMeshProUGUI>();

            if (font != null)
                label.font = font;

            label.fontSize = fontSize;
            label.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal;   // 팀원 시간 글자처럼 굵게 (m_fontStyle 1)
            label.color = color;
            label.alignment = TextAlignmentOptions.Center;
            label.textWrappingMode = TextWrappingModes.NoWrap;   // 줄바꿈 안 함 (Unity 6 · ugui 2.0)
            label.raycastTarget = false;   // 클릭을 막지 않는다
            return label;
        }
    }
}
