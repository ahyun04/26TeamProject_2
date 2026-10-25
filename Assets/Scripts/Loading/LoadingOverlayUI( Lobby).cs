using TMPro;
using UnityEngine;

namespace LockdownProtocol.Lobby
{
    /// <summary>
    /// 방 생성/참가 중 화면 전체를 덮는 로딩 표시. 로비 씬 Canvas 맨 아래(가장 위에 그려지는 위치)에 둔다.
    /// 이 스크립트는 항상 켜져 있는 오브젝트(LoadingOverlay)에 붙이고, 실제로 보이는 건 panel이다.
    /// 성공하면 씬이 바뀌면서 자연스럽게 사라지므로, 실패했을 때만 Hide()를 호출하면 된다.
    /// </summary>
    public class LoadingOverlayUI : MonoBehaviour
    {
        public static LoadingOverlayUI Instance { get; private set; }

        [SerializeField] private GameObject panel;      // 배경(반투명 검정) + 텍스트가 들어 있는 오브젝트
        [SerializeField] private TMP_Text messageText;  // "입장 중" (점은 자동으로 붙음)
        [SerializeField] private string baseMessage = "입장 중";
        [SerializeField] private float dotInterval = 0.4f;

        private float _timer;
        private int _dots;

        private void Awake()
        {
            Instance = this;
            if (panel != null) panel.SetActive(false);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void Update()
        {
            if (panel == null || !panel.activeSelf || messageText == null) return;

            _timer += Time.unscaledDeltaTime;
            if (_timer < dotInterval) return;
            _timer = 0f;
            _dots = (_dots + 1) % 4;
            messageText.text = baseMessage + new string('.', _dots);
        }

        public static void Show(string message = null)
        {
            var overlay = Instance;
            if (overlay == null || overlay.panel == null) return;

            if (!string.IsNullOrEmpty(message)) overlay.baseMessage = message;
            overlay._timer = 0f;
            overlay._dots = 0;
            if (overlay.messageText != null) overlay.messageText.text = overlay.baseMessage;
            overlay.panel.SetActive(true);
        }

        public static void Hide()
        {
            var overlay = Instance;
            if (overlay == null || overlay.panel == null) return;
            overlay.panel.SetActive(false);
        }
    }
}