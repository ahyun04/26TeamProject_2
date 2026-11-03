using TMPro;
using UnityEngine;

namespace TrustNoOne.Missions
{
    /// <summary>담당 자리 하나의 화면 표시 상태 (13b CO8 · CO24).</summary>
    public enum CoordinateSeatState
    {
        /// <summary>아무도 안 맡음 — 막대 흐림 "X 비어 있음"</summary>
        Empty = 0,

        /// <summary>다른 사람이 맡음 — 막대 켜짐 "X 담당 중"</summary>
        Taken = 1,

        /// <summary>내가 맡음 — 막대 켜짐 "X 내 담당"</summary>
        Mine = 2,
    }

    /// <summary>좌표 장치가 매 Render 에 화면으로 넘기는 상태 (13b CO19 — 모두 [Networked] 에서 계산한 값).</summary>
    public struct CoordinateView
    {
        public bool On;
        public bool Completed;
        public float X;
        public float Y;
        public float TargetX;
        public float TargetY;
        public float Fill;
        public bool Aligned;
        public int DirX;
        public int DirY;
        public bool PressX;
        public bool PressY;
        public CoordinateSeatState Green;
        public CoordinateSeatState Yellow;
        public bool NeedGreen;
        public bool NeedYellow;

        public bool SameAs(in CoordinateView other)
        {
            return On == other.On && Completed == other.Completed && X == other.X && Y == other.Y && TargetX == other.TargetX && TargetY == other.TargetY
                   && Fill == other.Fill && Aligned == other.Aligned && DirX == other.DirX && DirY == other.DirY && PressX == other.PressX && PressY == other.PressY
                   && Green == other.Green && Yellow == other.Yellow && NeedGreen == other.NeedGreen && NeedYellow == other.NeedYellow;
        }
    }

    /// <summary>
    /// [역할] 좌표 장치 화면 그림 (13b 명세 CO31 — 연출만, 규칙은 CoordinateStation 이 호스트에서). 장치가 매 Render 에 상태(CoordinateView)를 넘기면 바뀐 것만 만진다.
    ///  탐색 영역(고리) · 십자선 · 목표(노란 별) · 조작 원(고리 + 안 채움) · 축 글자(다음 방향만 밝게) · 담당 막대 2개 + 글자 · 꺼짐 안내(가운데) · 「설정 완료」(아래 가운데)
    /// [좌표] 그림들은 화면 판 기준 로컬 x(보는 사람 오른쪽) · y(위). 조작 원 · 목표 자리 = 값 × 탐색 영역 반지름(m).
    /// [색] 판마다 색만 다르게 — 재질은 그림마다 하나라 MaterialPropertyBlock 으로 색을 넣는다 (URP Unlit 의 _BaseColor, 다른 셰이더는 _Color).
    /// [근거] 13b 명세 CO4 (장치 위 화면) · CO7 (목표 보임 — 숨기려면 showTarget 끔) · CO8 (막대 = 담당 표시) · CO13 · CO14 (화살표) · CO16 (채움 · 맞음) · CO18 (켜짐) · CO28
    /// </summary>
    public class CoordinateScreen : MonoBehaviour
    {
        public const string IdleText = "초록 · 노랑 버튼을 F로 누르세요";
        public const string DoneText = "설정 완료";
        public const string NeedGreenText = "초록 담당이 필요합니다";
        public const string NeedYellowText = "노랑 담당이 필요합니다";

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        [Header("크기")]
        [Tooltip("탐색 영역 반지름 (m, 화면 판 기준) — 조작 원 · 목표 자리 = 값(−1 ~ 1) × 이 값")]
        [SerializeField] private float areaRadius = 0.16f;

        [Header("그림 (변환 도구가 채운다)")]
        [Tooltip("켜졌을 때만 보이는 그림 묶음 (탐색 영역 · 십자선 · 목표 · 조작 원 · 축 글자 · 막대)")]
        [SerializeField] private GameObject drawing;
        [SerializeField] private Transform cursor;
        [SerializeField] private Renderer cursorRing;
        [SerializeField] private Renderer cursorFill;
        [SerializeField] private Transform target;
        [SerializeField] private TMP_Text xAxisLabel;
        [SerializeField] private TMP_Text yAxisLabel;
        [SerializeField] private Renderer greenBar;
        [SerializeField] private Renderer yellowBar;
        [SerializeField] private TMP_Text greenBarLabel;
        [SerializeField] private TMP_Text yellowBarLabel;
        [Tooltip("꺼진 화면 가운데 안내 글자")]
        [SerializeField] private GameObject idleLabel;

        [Tooltip("완료하면 켜지는 「설정 완료」 글자 (화면 아래 가운데 — 원을 가리지 않게)")]
        [SerializeField] private GameObject doneLabel;

        [Tooltip("맞았는데 한 색 담당이 비어 있을 때 안내 (13b CO38 — 화면 아래 가운데)")]
        [SerializeField] private TMP_Text hintLabel;

        [Header("목표 표시 (13b CO7)")]
        [Tooltip("켬 = 기획서 \"정상화 위치가 표시된\" 대로 목표가 보인다. 기획자가 숨기기로 하면 끈다 (색 채움으로만 찾기)")]
        [SerializeField] private bool showTarget = true;

        [Header("색")]
        [SerializeField] private Color ringColor = new Color(0.45f, 0.85f, 1f, 1f);
        [SerializeField] private Color alignedColor = new Color(0.35f, 1f, 0.45f, 1f);
        [SerializeField] private Color greenColor = new Color(0.36f, 1f, 0.48f, 1f);
        [SerializeField] private Color yellowColor = new Color(1f, 0.86f, 0.3f, 1f);

        // 막대 진하기: 비어 있음 · 담당 있음 · 누르는 중 (CO8 — 흐림 / 켜짐 / 밝게)
        private const float BarEmptyAlpha = 0.18f;
        private const float BarTakenAlpha = 0.55f;
        private const float BarPressAlpha = 1f;

        // 축 글자에서 다음 방향이 아닌 쪽 화살표 진하기
        private const float ArrowDimAlpha = 0.25f;

        // 조작 원 안 채움: 채움 0 에서도 살짝 보이게
        private const float FillMinAlpha = 0.12f;
        private const float FillMaxAlpha = 0.85f;

        private CoordinateView shown;
        private bool hasShown;
        private MaterialPropertyBlock block;
        private float cursorZ;
        private float targetZ;
        private bool depthRead;

        public void Show(in CoordinateView view)
        {
            if (hasShown && shown.SameAs(view))
                return;

            hasShown = true;
            shown = view;
            Apply(view);
        }

        private void Apply(in CoordinateView view)
        {
            ReadDepth();

            if (drawing != null && drawing.activeSelf != view.On)
                drawing.SetActive(view.On);

            if (idleLabel != null && idleLabel.activeSelf == view.On)
                idleLabel.SetActive(!view.On);

            if (doneLabel != null && doneLabel.activeSelf != view.Completed)
                doneLabel.SetActive(view.Completed);

            if (hintLabel != null)
            {
                bool needSeat = view.On && (view.NeedGreen || view.NeedYellow);

                if (hintLabel.gameObject.activeSelf != needSeat)
                    hintLabel.gameObject.SetActive(needSeat);

                if (needSeat)
                    hintLabel.text = view.NeedGreen ? NeedGreenText : NeedYellowText;
            }

            if (!view.On)
                return;

            if (cursor != null)
                cursor.localPosition = new Vector3(view.X * areaRadius, view.Y * areaRadius, cursorZ);

            if (target != null)
            {
                target.localPosition = new Vector3(view.TargetX * areaRadius, view.TargetY * areaRadius, targetZ);

                if (target.gameObject.activeSelf != showTarget)
                    target.gameObject.SetActive(showTarget);
            }

            Color ring = view.Aligned ? alignedColor : ringColor;
            SetColor(cursorRing, ring);

            Color fill = view.Aligned ? alignedColor : ringColor;
            fill.a = Mathf.Lerp(FillMinAlpha, FillMaxAlpha, Mathf.Clamp01(view.Fill));
            SetColor(cursorFill, fill);

            if (xAxisLabel != null)
                xAxisLabel.text = $"{Arrow("◀", greenColor, view.DirX < 0)} X {Arrow("▶", greenColor, view.DirX >= 0)}";

            if (yAxisLabel != null)
                yAxisLabel.text = $"{Arrow("▲", yellowColor, view.DirY >= 0)}\nY\n{Arrow("▼", yellowColor, view.DirY < 0)}";

            ShowBar(greenBar, greenBarLabel, "X", greenColor, view.Green, view.PressX);
            ShowBar(yellowBar, yellowBarLabel, "Y", yellowColor, view.Yellow, view.PressY);
        }

        private void ShowBar(Renderer bar, TMP_Text label, string axis, Color color, CoordinateSeatState state, bool pressing)
        {
            Color barColor = color;
            barColor.a = state == CoordinateSeatState.Empty ? BarEmptyAlpha : pressing ? BarPressAlpha : BarTakenAlpha;
            SetColor(bar, barColor);

            if (label == null)
                return;

            label.text = state == CoordinateSeatState.Empty ? $"{axis} 비어 있음" : state == CoordinateSeatState.Mine ? $"{axis} 내 담당" : $"{axis} 담당 중";
            Color labelColor = color;
            labelColor.a = state == CoordinateSeatState.Empty ? 0.5f : 1f;
            label.color = labelColor;
        }

        /// <summary>다음 방향 쪽 화살표만 밝게 (CO14). TextMeshPro 서식 글자로 색을 넣는다.</summary>
        private static string Arrow(string symbol, Color color, bool bright)
        {
            Color c = color;
            c.a = bright ? 1f : ArrowDimAlpha;
            return $"<color=#{ColorUtility.ToHtmlStringRGBA(c)}>{symbol}</color>";
        }

        private void SetColor(Renderer renderer, Color color)
        {
            if (renderer == null)
                return;

            if (block == null)
                block = new MaterialPropertyBlock();

            renderer.GetPropertyBlock(block);
            block.SetColor(BaseColorId, color);
            block.SetColor(ColorId, color);
            renderer.SetPropertyBlock(block);
        }

        // 겹침 순서(앞뒤 z)는 변환 도구가 정한 값을 그대로 쓴다 — 자리만 바꾼다
        private void ReadDepth()
        {
            if (depthRead)
                return;

            depthRead = true;

            if (cursor != null)
                cursorZ = cursor.localPosition.z;

            if (target != null)
                targetZ = target.localPosition.z;
        }
    }
}
