using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

namespace TrustNoOne.Minimap
{
    /// <summary>전체 지도 왼쪽 미션 목록 한 줄. 마우스를 올리면 해당 미션 마커를 강조한다.</summary>
    public class MinimapListItem : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        [SerializeField] private TMP_Text text;
        [SerializeField] private float fontSize = 24f;
        [SerializeField] private float rowHeight = 36f;
        private string missionId;
        public string MissionId => missionId;

        private Color baseColor = Color.white;
        private float baseSize;
        private bool highlighted;

        /// <summary>마우스가 올라간 줄 강조: 글자 확대 + 굵게 + 색 변경. 벗어나면 원래대로.</summary>
        public void SetHighlighted(bool on, Color highlightColor, float sizeScale)
        {
            if (text == null || on == highlighted) return;
            highlighted = on;
            text.fontSize = on ? baseSize * sizeScale : baseSize;
            text.fontStyle = on ? FontStyles.Bold : FontStyles.Normal;
            text.color = on ? highlightColor : baseColor;
        }
        private Action<string> hover;

        public void Setup(string displayName, Color color, string id, Action<string> onHover)
        {
            if (text == null) text = GetComponentInChildren<TMP_Text>();
            if (text != null)
            {
                text.text = displayName;
                text.color = color;
                text.enableAutoSizing = false; // 줄 높이가 눌려 글자가 점처럼 작아지는 것 방지
                text.fontSize = fontSize;
                baseSize = fontSize;
                baseColor = color;
                highlighted = false;
                text.fontStyle = FontStyles.Normal;
                text.raycastTarget = true;     // 마우스 호버를 받으려면 필요
            }

            // 줄이 가로로 부모 폭만큼 펴지고, 높이는 rowHeight 로 고정되게 한다 (폭이 0 이면 글자가 세로로 한 글자씩 꺾인다)
            RectTransform rt = transform as RectTransform;
            if (rt != null)
            {
                rt.localScale = Vector3.one;
                rt.anchorMin = new Vector2(0f, 1f);
                rt.anchorMax = new Vector2(1f, 1f);
                rt.pivot = new Vector2(0.5f, 1f);
                rt.sizeDelta = new Vector2(0f, rowHeight);
            }
            if (text != null && text.transform != transform)
            {
                RectTransform tr = text.rectTransform;
                tr.localScale = Vector3.one;
                tr.anchorMin = Vector2.zero;
                tr.anchorMax = Vector2.one;
                tr.offsetMin = Vector2.zero;
                tr.offsetMax = Vector2.zero;
            }
            if (text != null)
            {
                text.overflowMode = TextOverflowModes.Overflow; // 호버로 글자가 커져도 사라지지 않게 (Ellipsis 는 줄 높이를 넘으면 글자를 숨긴다)
#pragma warning disable CS0618
                text.enableWordWrapping = false;
#pragma warning restore CS0618
            }

            // 프리팹에 레이아웃 설정이 없어도 줄이 일정한 높이를 갖도록 한다
            UnityEngine.UI.LayoutElement le = GetComponent<UnityEngine.UI.LayoutElement>();
            if (le == null) le = gameObject.AddComponent<UnityEngine.UI.LayoutElement>();
            le.minHeight = rowHeight;
            le.preferredHeight = rowHeight;
            missionId = id;
            hover = onHover;
        }

        public void OnPointerEnter(PointerEventData e) { hover?.Invoke(missionId); }
        public void OnPointerExit(PointerEventData e) { hover?.Invoke(null); }
    }
}