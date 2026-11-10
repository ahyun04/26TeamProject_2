using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace LockdownProtocol.Lobby
{
    /// <summary>
    /// [역할] [빠른 입장] 을 누르면 열리는 방 목록 창. 공개 방만 보이고, 줄의 [입장] 을 누르면 코드 입력 없이 바로 들어간다.
    /// [표시 조건] 공개(IsVisible) + 입장 가능(IsOpen, 즉 게임 시작 전) 인 방만. 비공개 방은 방을 만들 때 목록에서 빠지도록 등록된다.
    /// [입장] 방 목록 전용 러너를 먼저 끄고 NetworkBootstrap.JoinRoom(세션 이름) 을 호출한다. 결과 안내는 초대 코드 입장과 같다.
    /// </summary>
    public class RoomListUI : MonoBehaviour
    {
        [SerializeField] private NetworkBootstrap bootstrap;
        [SerializeField] private SessionBrowser browser;

        [Header("UI")]
        [SerializeField] private GameObject panel;           // 방 목록 창 전체 (평소엔 꺼 둠)
        [SerializeField] private Transform content;          // 줄이 들어갈 칸 (ScrollView 의 Content)
        [SerializeField] private RoomListRow rowPrefab;
        [SerializeField] private TMP_Text emptyText;         // "현재 입장 가능한 방이 없습니다"
        [SerializeField] private TMP_Text feedbackText;
        [SerializeField] private Button closeButton;

        private bool joining;

        private void Awake()
        {
            if (panel != null) panel.SetActive(false);
        }

        private void OnEnable()
        {
            if (closeButton != null) closeButton.onClick.AddListener(Close);
            if (browser != null)
            {
                browser.ListUpdated += handleList;
                browser.Failed += handleFailed;
            }
        }

        private void OnDisable()
        {
            if (closeButton != null) closeButton.onClick.RemoveListener(Close);
            if (browser != null)
            {
                browser.ListUpdated -= handleList;
                browser.Failed -= handleFailed;
            }
        }

        public async void Open()
        {
            if (panel == null || browser == null) { Debug.LogWarning("[RoomListUI] Panel 또는 Browser 가 연결되지 않았습니다."); return; }
            panel.SetActive(true);
            showFeedback(string.Empty);
            rebuild(browser.Sessions);
            await browser.StartBrowsing();
        }

        public async void Close()
        {
            if (panel != null) panel.SetActive(false);
            if (browser != null) await browser.StopBrowsing();
        }

        private void handleList(IReadOnlyList<Fusion.SessionInfo> sessions) { rebuild(sessions); }

        private void handleFailed(string message)
        {
            showFeedback(message);
            rebuild(null);
        }

        private void rebuild(IReadOnlyList<Fusion.SessionInfo> sessions)
        {
            if (content == null || rowPrefab == null) return;

            for (int i = content.childCount - 1; i >= 0; i--)
                Destroy(content.GetChild(i).gameObject);

            int shown = 0;
            if (sessions != null)
            {
                foreach (Fusion.SessionInfo s in sessions)
                {
                    if (!s.IsVisible || !s.IsOpen) continue; // 비공개/게임 중인 방은 제외

                    string roomName = readString(s, "n", s.Name);
                    int max = readInt(s, "m", s.MaxPlayers);
                    bool joinable = s.PlayerCount < max;

                    RoomListRow row = Instantiate(rowPrefab, content);
                    row.Setup(roomName, $"{s.PlayerCount} / {max}", joinable, s.Name, onJoinClicked);
                    shown++;
                }
            }

            if (emptyText != null) emptyText.gameObject.SetActive(shown == 0);
        }

        private static string readString(Fusion.SessionInfo s, string key, string fallback)
        {
            if (s.Properties != null && s.Properties.TryGetValue(key, out Fusion.SessionProperty p) && p.IsString)
                return (string)p;
            return fallback;
        }

        private static int readInt(Fusion.SessionInfo s, string key, int fallback)
        {
            if (s.Properties != null && s.Properties.TryGetValue(key, out Fusion.SessionProperty p) && p.IsInt)
                return (int)p;
            return fallback;
        }

        private async void onJoinClicked(string sessionName)
        {
            if (joining || bootstrap == null) return;
            joining = true;
            showFeedback(string.Empty);
            LoadingOverlayUI.Show("입장 중");

            try
            {
                if (browser != null) await browser.StopBrowsing();
                var result = await bootstrap.JoinRoom(sessionName);
                if (this == null) return;

                var mapped = RoomManager.MapJoinResult(result);
                if (mapped == RoomManager.JoinRoomResult.Success) return; // 씬 전환은 자동

                LoadingOverlayUI.Hide();
                switch (mapped)
                {
                    case RoomManager.JoinRoomResult.RoomFull: showFeedback("방이 가득 찼습니다"); break;
                    case RoomManager.JoinRoomResult.NotFound: showFeedback("방이 사라졌거나 참가할 수 없는 방입니다"); break;
                    case RoomManager.JoinRoomResult.GameStarted: showFeedback("이미 게임이 시작되었습니다"); break;
                    default: showFeedback("접속 오류가 발생했습니다"); break;
                }

                if (browser != null && panel != null && panel.activeSelf) await browser.StartBrowsing(); // 목록 다시 받기
            }
            catch (System.Exception exception)
            {
                LoadingOverlayUI.Hide();
                if (this != null) showFeedback(exception.Message);
                Debug.LogException(exception);
            }
            finally
            {
                joining = false;
            }
        }

        private void showFeedback(string message)
        {
            if (feedbackText != null) feedbackText.text = message;
        }
    }
}