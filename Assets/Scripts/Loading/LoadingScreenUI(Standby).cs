using LockdownProtocol.Lobby;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 게임 시작 후 게임 씬으로 넘어가는 동안 화면 전체를 덮는 로딩 화면 (기획서: 하단에 "Tab 키를 눌러 키를 확인하세요").
/// - 방 상태가 Playing이 되는 순간(방장이 게임 씬 전환을 시작하는 시점) 모든 클라이언트에서 켜진다.
/// - 게임 씬이 로드되고 내 캐릭터가 생성되면(최소 표시 시간 이후) 꺼진다. 오래 걸리면 안전하게 강제로 꺼진다.
/// 씬을 넘나들며 유지되도록 StandBy 씬의 루트 오브젝트(Canvas)에 붙인다. 이 오브젝트는 항상 켜 두고,
/// 실제로 보이는 UI(검은 배경 + 하단 안내 문구)는 panel 아래에 둔다.
/// </summary>
public class LoadingScreenUI : MonoBehaviour
{
    public static LoadingScreenUI Instance { get; private set; }

    /// <summary>로딩 화면이 지금 화면을 덮고 있는지 (역할 공개 연출이 로딩이 끝나길 기다리는 데 사용)</summary>
    public static bool IsVisible => Instance != null && Instance.panel != null && Instance.panel.activeSelf;

    [SerializeField] private GameObject panel;          //로딩 화면 전체 (검은 배경 + 하단 안내 문구)
    [SerializeField] private string standbySceneName = "StandBy"; //이 씬에 있는 동안은 아직 게임 씬이 아님
    [SerializeField, Min(0f)] private float minDisplaySeconds = 1.5f; //너무 번쩍 사라지지 않게 하는 최소 표시 시간
    [SerializeField, Min(1f)] private float maxDisplaySeconds = 30f;  //준비가 안 와도 이 시간이 지나면 강제로 닫음
    [SerializeField, Min(0.05f)] private float checkInterval = 0.25f;

    private bool sessionActive; //이번 게임에서 이미 로딩을 띄웠는지 (다시 띄우지 않기 위함)
    private float shownTime;
    private float nextCheckTime;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject); //StandBy 씬에 다시 들어와도 하나만 유지
            return;
        }

        Instance = this;
        if (panel == null) Debug.LogError("[LoadingScreenUI] Panel 슬롯이 비어 있어 로딩 화면을 표시할 수 없습니다.", this);
        if (transform.parent != null) transform.SetParent(null); //DontDestroyOnLoad는 루트 오브젝트만 가능
        DontDestroyOnLoad(gameObject);
        NetworkBootstrap.OnSceneLoadStartEvent += handleSceneLoadStart;

        if (panel != null) panel.SetActive(false);
    }

    private void OnDestroy()
    {
        NetworkBootstrap.OnSceneLoadStartEvent -= handleSceneLoadStart;
        if (Instance == this) Instance = null;
    }

    // 대기실에서 다른 씬으로 넘어가는 로드가 시작되면(= 게임 시작) 모든 접속자에게 로딩 화면 표시
    private void handleSceneLoadStart(Fusion.NetworkRunner runner)
    {
        if (sessionActive || IsVisible) return;
        if (SceneManager.GetActiveScene().name != standbySceneName) return; //게임 → 대기실 복귀는 제외
        sessionActive = true;
        Debug.Log("[LoadingScreenUI] 씬 로드 시작 감지 → 로딩 화면 표시");
        Show();
    }

    private void Update()
    {
        RoomManager room = RoomManager.Instance;
        if (room != null && room.Object != null && room.Object.IsValid)
        {
            bool playing = room.CurrentRoomState == RoomManager.RoomState.Playing;
            if (playing && !sessionActive)
            {
                sessionActive = true;
                Debug.Log("[LoadingScreenUI] 게임 시작 감지 → 로딩 화면 표시");
                Show();
            }
            else if (!playing && room.CurrentRoomState != RoomManager.RoomState.Starting)
            {
                sessionActive = false; //게임이 끝났거나 대기실로 돌아오면 다음 게임을 위해 초기화
                if (IsVisible) Hide();
            }
        }

        if (!IsVisible) return;

        shownTime += Time.unscaledDeltaTime;

        if (Time.unscaledTime < nextCheckTime) return;
        nextCheckTime = Time.unscaledTime + checkInterval;

        if (shownTime >= maxDisplaySeconds)
        {
            Debug.LogWarning($"[LoadingScreenUI] 준비 확인이 {shownTime:F0}초 걸려 강제로 닫습니다. (현재 씬 '{SceneManager.GetActiveScene().name}', 내 플레이어 {(FindLocalPlayer() ? "있음" : "없음")})");
            Hide();
            return;
        }

        if (shownTime >= minDisplaySeconds && isGameSceneReady())
        {
            Debug.Log($"[LoadingScreenUI] 게임 씬 준비 완료 → 로딩 화면 종료 ({shownTime:F1}초)");
            Hide();
        }
    }

    public static void Show()
    {
        LoadingScreenUI loading = Instance;
        if (loading == null || loading.panel == null)
        {
            Debug.LogError("[LoadingScreenUI] Show 실패: " + (loading == null ? "씬에 LoadingScreenUI가 없습니다 (StandBy 씬 루트에 배치 필요)" : "Panel 슬롯이 비어 있습니다"));
            return;
        }

        loading.shownTime = 0f;
        loading.panel.SetActive(true);
    }

    public static void Hide()
    {
        LoadingScreenUI loading = Instance;
        if (loading == null || loading.panel == null) return;

        loading.panel.SetActive(false);
    }

    // 게임 씬이 실제로 로드됐고(대기실이 아니고) 내 캐릭터가 생성됐으면 준비 완료.
    // 게임 씬에서는 RoomManager가 사라지므로 방 정보에 의존하지 않고 현재 씬과 내 캐릭터만 본다.
    private bool isGameSceneReady()
    {
        if (SceneManager.GetActiveScene().name == standbySceneName) return false;
        return FindLocalPlayer();
    }

    private static bool FindLocalPlayer() //내 캐릭터(입력 권한)가 생성됐는지
    {
        foreach (PlayerHealth player in FindObjectsByType<PlayerHealth>(FindObjectsSortMode.None))
        {
            if (player.Object != null && player.Object.IsValid && player.HasInputAuthority)
                return true;
        }

        return false;
    }
}