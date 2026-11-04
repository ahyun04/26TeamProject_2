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
        private string missionId;
        private Action<string> hover;

        public void Setup(string displayName, Color color, string id, Action<string> onHover)
        {
            if (text == null) text = GetComponentInChildren<TMP_Text>();
            if (text != null) { text.text = displayName; text.color = color; }
            missionId = id;
            hover = onHover;
        }

        public void OnPointerEnter(PointerEventData e) { hover?.Invoke(missionId); }
        public void OnPointerExit(PointerEventData e) { hover?.Invoke(null); }
    }
}