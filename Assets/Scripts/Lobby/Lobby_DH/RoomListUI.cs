using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace LockdownProtocol.Lobby
{
    /// <summary>
    /// [역할] [빠른 입장] 을 누르면 열리는 방 목록 창. 공개 방만 보이고, 줄의 [입장] 을 누르면 코드 입력 없이 바로 들어간다.
    /// [표시 조건] 공개(IsVisible) 방만. 비공개 방은 방을 만들 때 목록에서 빠지도록 등록된다.
    ///  방 컨디션: IsOpen 이면 "준비 중", 닫혀 있으면(게임 진행 중) "게임 중". 게임 중인 방은 [입장] 을 눌러도 안내 문구만 뜬다.
    /// [ESC] 목록이 열려 있으면 ESC 로 닫는다. 설정창(GameSettingsUIComponent)은 IsOpen 인 동안 ESC 를 무시한다.
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
        [SerializeField] private Button refreshButton;       // 우측 상단 새로고침
        [SerializeField, Min(0.5f)] private float refreshCooldown = 2f;

        [Header("Texts")]
        [SerializeField] private string waitingLabel = "준비 중";
        [SerializeField] private string playingLabel = "게임 중";
        [SerializeField] private string playingMessage = "게임 진행 중입니다. 잠시 후 다시 시도해 주세요.";

        /// <summary>방 목록 창이 열려 있는 동안 true. 설정창이 ESC 를 가로채지 않도록 확인하는 용도.</summary>
        public static bool IsOpen { get; private set; }

        private bool joining;
        private bool refreshing;
        private float nextRefreshTime;
        private readonly Dictionary<string, bool> playingBySession = new Dictionary<string, bool>(); // 세션 이름 -> 게임 중 여부
        private readonly Dictionary<string, bool> fullBySession = new Dictionary<string, bool>();    // 세션 이름 -> 가득 참 여부

        private void Awake()
        {
            if (panel != null) panel.SetActive(false);
        }

        private void OnEnable()
        {
            if (closeButton != null) closeButton.onClick.AddListener(Close);
            if (refreshButton != null) refreshButton.onClick.AddListener(Refresh);
            if (browser != null)
            {
                browser.ListUpdated += handleList;
                browser.Failed += handleFailed;
            }
        }

        private void OnDisable()
        {
            if (closeButton != null) closeButton.onClick.RemoveListener(Close);
            if (refreshButton != null) refreshButton.onClick.RemoveListener(Refresh);
            if (browser != null)
            {
                browser.ListUpdated -= handleList;
                browser.Failed -= handleFailed;
            }
            IsOpen = false;
        }

        private void Update()
        {
            // 열려 있는 동안 ESC 로 닫기. (입장 처리 중에는 무시)
            // GameSettingsUIComponent 는 실행 순서가 먼저라 같은 프레임에 IsOpen 이 아직 true 로 보여 ESC 를 쓰지 않는다.
            if (IsOpen && !joining && Input.GetKeyDown(KeyCode.Escape)) Close();
        }

        public async void Open()
        {
            if (panel == null || browser == null) { Debug.LogWarning("[RoomListUI] Panel 또는 Browser 가 연결되지 않았습니다."); return; }
            panel.SetActive(true);
            IsOpen = true;
            nextRefreshTime = Time.unscaledTime + refreshCooldown;
            showFeedback(string.Empty);
            rebuild(browser.Sessions);
            await browser.StartBrowsing();
        }

        public async void Close()
        {
            IsOpen = false;
            if (panel != null) panel.SetActive(false);
            if (browser != null) await browser.StopBrowsing();
        }

        /// <summary>목록 조회를 껐다 켜서 최신 방 목록을 다시 받는다. 연타 방지용 쿨다운이 있다.</summary>
        public async void Refresh()
        {
            if (!IsOpen || joining || refreshing || browser == null) return;
            if (Time.unscaledTime < nextRefreshTime) return;

            refreshing = true;
            nextRefreshTime = Time.unscaledTime + refreshCooldown;
            if (refreshButton != null) refreshButton.interactable = false;
            showFeedback(string.Empty);

            try
            {
                await browser.StopBrowsing();
                if (this == null || !IsOpen) return;
                await browser.StartBrowsing();
            }
            catch (System.Exception exception)
            {
                Debug.LogException(exception);
            }
            finally
            {
                refreshing = false;
                if (this != null && refreshButton != null) refreshButton.interactable = true;
            }
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

            playingBySession.Clear();
            fullBySession.Clear();

            int shown = 0;
            if (sessions != null)
            {
                foreach (Fusion.SessionInfo s in sessions)
                {
                    if (!s.IsVisible) continue; // 비공개 방은 제외 (게임 중인 방은 "게임 중" 으로 표시)

                    string roomName = readString(s, "n", s.Name);
                    int max = readInt(s, "m", s.MaxPlayers);
                    bool waiting = s.IsOpen; // 닫혀 있으면 게임 진행 중(또는 시작 중)
                    playingBySession[s.Name] = !waiting;
                    fullBySession[s.Name] = s.PlayerCount >= max;

                    RoomListRow row = Instantiate(rowPrefab, content);
                    row.Setup(roomName, $"{s.PlayerCount} / {max}", waiting ? waitingLabel : playingLabel, waiting, s.Name, onJoinClicked);
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

            // 목록 기준으로 게임 중이거나 가득 찬 방은 입장을 시도하지 않고 안내만 한다
            if (playingBySession.TryGetValue(sessionName, out bool playing) && playing) { showFeedback(playingMessage); return; }
            if (fullBySession.TryGetValue(sessionName, out bool full) && full) { showFeedback("방이 가득 찼습니다"); return; }

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
                    case RoomManager.JoinRoomResult.GameStarted: showFeedback(playingMessage); break;
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