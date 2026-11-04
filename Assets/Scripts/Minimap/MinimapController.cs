using System.Collections.Generic;
using TMPro;
using TrustNoOne.Missions;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace TrustNoOne.Minimap
{
    /// <summary>
    /// [역할] 기획서 8 — 화면 우측 상단 미니맵 + M 키 전체 지도.
    /// [미니맵] 원형 창. 맵 이미지가 내 위치를 중심으로 움직이고(북쪽 고정), 가운데 화살표가 내 시야 방향.
    /// [전체 지도] M 로 열고 M/ESC 로 닫는다. 게임은 계속 진행된다. 왼쪽 미션 목록에 마우스를 올리면 그 미션 위치 마커가 강조된다.
    /// [표시 대상] 미션 마커는 이 PC 에 공개된 미션(MissionManager.Client 의 Team + Mine)과 Id 가 같은 것만. 탈출구는 항상 표시(잠김=회색, 열림=초록).
    /// [숨김] 죽음/탈출/게임 종료, 맵 데이터 없음 → 미니맵 숨김 + 전체 지도 닫힘.
    /// </summary>
    public class MinimapController : MonoBehaviour
    {
        public static bool IsFullMapOpen { get; private set; }
        private static int escConsumedFrame = -1;
        /// <summary>이번 프레임에 ESC 로 지도를 닫았다 (설정창이 같은 ESC 로 열리지 않게 확인용).</summary>
        public static bool EscapeConsumedThisFrame => escConsumedFrame == Time.frameCount;

        [Header("맵 데이터 (씬 이름과 일치하는 것을 사용)")]
        [SerializeField] private MinimapMapData[] mapDatas;

        [Header("미니맵")]
        [SerializeField] private GameObject miniRoot;
        [SerializeField] private RectTransform miniMapRect;      // 마스크 안쪽에서 움직이는 맵 이미지
        [SerializeField] private Image miniMapImage;
        [SerializeField] private RectTransform miniPlayerArrow;   // 마스크 중앙 고정, 회전만
        [SerializeField] private float miniVisibleMeters = 30f;   // 미니맵 창 지름이 보여 줄 월드 거리(m)
        [SerializeField] private float miniWindowSize = 220f;     // 미니맵 창 지름(px). MiniRoot 크기와 같게

        [Header("전체 지도")]
        [SerializeField] private GameObject fullRoot;
        [SerializeField] private RectTransform fullMapRect;       // AspectRatioFitter 가 붙은 맵 이미지
        [SerializeField] private Image fullMapImage;
        [SerializeField] private AspectRatioFitter fullAspect;
        [SerializeField] private RectTransform fullPlayerArrow;
        [SerializeField] private RectTransform missionListRoot;   // 왼쪽 미션 목록이 들어갈 칸
        [SerializeField] private MinimapListItem listItemPrefab;
        [SerializeField] private TMP_Text noMapText;              // "맵 정보를 불러올 수 없습니다" (선택)

        [Header("마커")]
        [SerializeField] private Image markerPrefab;              // 작은 원/아이콘 Image 프리팹 (크기 18 정도)
        [SerializeField] private TMP_Text roomLabelPrefab;        // 방 이름용 (선택)
        [SerializeField] private Color missionColor = new Color32(0xFF, 0xD2, 0x4A, 0xFF);
        [SerializeField] private Color missionDoneColor = new Color32(0x7C, 0xFC, 0x7C, 0xFF);
        [SerializeField] private Color missionHoverColor = new Color32(0xFF, 0x5A, 0x3C, 0xFF);
        [SerializeField] private Color exitLockedColor = new Color32(0x99, 0x99, 0x99, 0xFF);
        [SerializeField] private Color exitOpenColor = new Color32(0x4A, 0xFF, 0x7A, 0xFF);
        [SerializeField] private float hoverScale = 1.6f;
        [SerializeField] private float miniMarkerSize = 10f;
        [SerializeField] private float fullMarkerSize = 14f;

        private MinimapMapData map;
        private PlayerHealth localPlayer;
        private MissionManager missions;
        private MissionClientState client;
        private GameEndSystem endSystem;
        private string loadedScene;
        private string hoveredMissionId;
        private bool lastKnownPosValid;
        private Vector3 lastKnownPos;
        private float lastKnownYaw;
        private bool listDirty = true;
        private float nextFind;
        private CursorLockMode prevLock;
        private bool prevCursorVisible;

        private readonly List<MarkerView> miniMarkers = new List<MarkerView>();
        private readonly List<MarkerView> fullMarkers = new List<MarkerView>();

        private class MarkerView
        {
            public Image icon;
            public TMP_Text label;
            public MinimapMarker source;
        }

        private void Awake()
        {
            setMiniActive(false);
            if (fullRoot != null) fullRoot.SetActive(false);
            IsFullMapOpen = false;
        }

        private void OnDestroy()
        {
            if (client != null) client.Changed -= markListDirty;
            if (IsFullMapOpen) closeFull();
            IsFullMapOpen = false;
        }

        private void Update()
        {
            ensureMap();
            findRefs();

            if (!IsFullMapOpen && fullRoot != null && fullRoot.activeSelf)
                fullRoot.SetActive(false);

            bool allowed = map != null && map.IsValid && localPlayer != null && isPlayerAlive() && !isGameEnded();

            if (!allowed)
            {
                if (IsFullMapOpen) closeFull();
                setMiniActive(false);
                if (noMapText != null) noMapText.gameObject.SetActive(map == null || !map.IsValid);
                return;
            }

            if (noMapText != null) noMapText.gameObject.SetActive(false);

            if (Input.GetKeyDown(KeyCode.M)) toggleFull();
            else if (IsFullMapOpen && Input.GetKeyDown(KeyCode.Escape)) { escConsumedFrame = Time.frameCount; closeFull(); }

            updatePosition();

            if (IsFullMapOpen)
            {
                setMiniActive(false);
                refreshFull();
            }
            else
            {
                setMiniActive(true);
                refreshMini();
            }
        }

        //----------------------------------------------------------------- 상태 확인
        private void ensureMap()
        {
            string scene = SceneManager.GetActiveScene().name;
            if (scene == loadedScene) return;

            loadedScene = scene;
            map = null;
            if (mapDatas != null)
                foreach (MinimapMapData d in mapDatas)
                    if (d != null && d.SceneName == scene) { map = d; break; }

            clearMarkers(miniMarkers);
            clearMarkers(fullMarkers);
            listDirty = true;

            if (map == null || !map.IsValid)
            {
                Debug.LogError($"[Minimap] 씬 '{scene}' 에 맞는 MinimapMapData 가 없거나 이미지가 비어 있습니다.");
                return;
            }

            if (miniMapImage != null) miniMapImage.sprite = map.MapImage;
            if (fullMapImage != null) fullMapImage.sprite = map.MapImage;
            if (fullAspect != null)
            {
                Vector2 s = map.WorldSize;
                fullAspect.aspectRatio = s.x / s.y;
            }
            if (miniMapRect != null)
            {
                Vector2 s = map.WorldSize;
                float pxPerMeter = miniWindowSize / Mathf.Max(1f, miniVisibleMeters);
                miniMapRect.sizeDelta = new Vector2(s.x * pxPerMeter, s.y * pxPerMeter);
            }
        }

        private void findRefs()
        {
            if (Time.unscaledTime < nextFind && localPlayer != null && missions != null) return;
            nextFind = Time.unscaledTime + 0.5f;

            if (localPlayer == null || localPlayer.Object == null || !localPlayer.Object.IsValid)
            {
                localPlayer = null;
                foreach (PlayerHealth p in FindObjectsByType<PlayerHealth>(FindObjectsSortMode.None))
                    if (p.Object != null && p.Object.IsValid && p.HasInputAuthority) { localPlayer = p; break; }
            }

            if (missions == null) missions = FindFirstObjectByType<MissionManager>();
            if (endSystem == null) endSystem = FindFirstObjectByType<GameEndSystem>();

            if (client == null && missions != null && missions.Client != null)
            {
                client = missions.Client;
                client.Changed += markListDirty;
                listDirty = true;
            }
        }

        private bool isPlayerAlive()
        {
            return localPlayer.Object != null && localPlayer.Object.IsValid && !localPlayer.IsDead && !localPlayer.IsEscaped;
        }

        private bool isGameEnded()
        {
            return endSystem != null && endSystem.Object != null && endSystem.Object.IsValid && endSystem.IsGameEnded;
        }

        private void markListDirty() { listDirty = true; }

        private void updatePosition()
        {
            lastKnownPos = localPlayer.transform.position;
            lastKnownYaw = localPlayer.transform.eulerAngles.y;
            lastKnownPosValid = true;
        }

        //----------------------------------------------------------------- 미니맵
        private void setMiniActive(bool on)
        {
            if (miniRoot != null && miniRoot.activeSelf != on) miniRoot.SetActive(on);
        }

        private void refreshMini()
        {
            if (!lastKnownPosValid || miniMapRect == null) return;

            Vector2 n = map.WorldToNormalized(lastKnownPos);
            Vector2 size = miniMapRect.rect.size;
            // 맵 이미지의 pivot 은 (0.5, 0.5). 내 위치가 창 중앙에 오도록 이미지를 반대로 민다.
            miniMapRect.anchoredPosition = new Vector2(-(n.x - 0.5f) * size.x, -(n.y - 0.5f) * size.y);

            if (miniPlayerArrow != null)
                miniPlayerArrow.localRotation = Quaternion.Euler(0f, 0f, -lastKnownYaw);

            syncMarkers(miniMarkers, miniMapRect, false);
        }

        //----------------------------------------------------------------- 전체 지도
        private void toggleFull()
        {
            if (IsFullMapOpen) closeFull();
            else openFull();
        }

        private void openFull()
        {
            if (fullRoot == null) return;
            prevLock = Cursor.lockState;
            prevCursorVisible = Cursor.visible;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            IsFullMapOpen = true;
            fullRoot.SetActive(true);
            listDirty = true;
            hoveredMissionId = null;
        }

        private void closeFull()
        {
            if (!IsFullMapOpen) return;
            IsFullMapOpen = false;
            if (fullRoot != null) fullRoot.SetActive(false);
            hoveredMissionId = null;
            Cursor.lockState = prevLock;
            Cursor.visible = prevCursorVisible;
        }

        private void refreshFull()
        {
            if (fullMapRect == null) return;

            if (lastKnownPosValid && fullPlayerArrow != null)
            {
                Vector2 n = map.WorldToNormalized(lastKnownPos);
                fullPlayerArrow.anchorMin = fullPlayerArrow.anchorMax = n;
                fullPlayerArrow.anchoredPosition = Vector2.zero;
                fullPlayerArrow.localRotation = Quaternion.Euler(0f, 0f, -lastKnownYaw);
            }

            if (listDirty) rebuildList();
            syncMarkers(fullMarkers, fullMapRect, true);
        }

        private void rebuildList()
        {
            listDirty = false;
            if (missionListRoot == null || listItemPrefab == null) return;

            for (int i = missionListRoot.childCount - 1; i >= 0; i--)
                Destroy(missionListRoot.GetChild(i).gameObject);

            if (client == null) return;
            addListItems(client.Team);
            addListItems(client.Mine);
        }

        private void addListItems(IReadOnlyList<ObjectiveView> views)
        {
            foreach (ObjectiveView v in views)
            {
                if (v.Definition == null) continue;
                string id = v.Definition.Id;
                // 지도에 위치(마커)가 없는 미션도 목록에는 보인다 (마우스를 올려도 강조될 점이 없을 뿐)

                string name = string.IsNullOrEmpty(v.Definition.DisplayName) ? id : v.Definition.DisplayName;
                bool done = v.Status == ObjectiveStatus.Completed;
                MinimapListItem item = Instantiate(listItemPrefab, missionListRoot);
                item.Setup(name, done ? missionDoneColor : Color.white, id, onHover);
            }
        }

        private void onHover(string id) { hoveredMissionId = id; }

        private static bool hasMarkerFor(string id)
        {
            foreach (MinimapMarker m in MinimapMarker.All)
                if (m.Kind == MinimapMarkerKind.Mission && m.MissionId == id) return true;
            return false;
        }

        //----------------------------------------------------------------- 마커
        private bool tryFindView(string id, out ObjectiveView found)
        {
            found = default;
            if (client == null) return false;
            foreach (ObjectiveView v in client.Team)
                if (v.Definition != null && v.Definition.Id == id) { found = v; return true; }
            foreach (ObjectiveView v in client.Mine)
                if (v.Definition != null && v.Definition.Id == id) { found = v; return true; }
            return false;
        }

        private void syncMarkers(List<MarkerView> views, RectTransform parent, bool isFull)
        {
            int used = 0;
            foreach (MinimapMarker m in MinimapMarker.All)
            {
                if (m == null) continue;
                if (m.Kind == MinimapMarkerKind.Room && !isFull) continue;

                Color color = Color.white;
                bool hovered = false;

                if (m.Kind == MinimapMarkerKind.Mission)
                {
                    if (!tryFindView(m.MissionId, out ObjectiveView v)) continue; // 이 PC 에 공개되지 않은 미션은 표시하지 않는다
                    bool done = v.Status == ObjectiveStatus.Completed;
                    hovered = isFull && hoveredMissionId == m.MissionId;
                    color = hovered ? missionHoverColor : done ? missionDoneColor : missionColor;
                }
                else if (m.Kind == MinimapMarkerKind.Exit)
                {
                    bool open = missions != null && missions.EscapeUnlocked;
                    color = open ? exitOpenColor : exitLockedColor;
                }

                MarkerView mv = getView(views, parent, used++);
                mv.source = m;
                mv.icon.gameObject.SetActive(m.Kind != MinimapMarkerKind.Room);
                mv.icon.color = color;
                mv.icon.rectTransform.sizeDelta = Vector2.one * (isFull ? fullMarkerSize : miniMarkerSize);
                mv.icon.rectTransform.localScale = Vector3.one * (hovered ? hoverScale : 1f);

                Vector2 n = map.WorldToNormalized(m.Position);
                RectTransform rt = mv.icon.rectTransform;
                rt.anchorMin = rt.anchorMax = n;
                rt.anchoredPosition = Vector2.zero;

                if (mv.label != null)
                {
                    bool showLabel = m.Kind == MinimapMarkerKind.Room && !string.IsNullOrEmpty(m.Label);
                    mv.label.gameObject.SetActive(showLabel);
                    if (showLabel)
                    {
                        mv.label.text = m.Label;
                        RectTransform lr = mv.label.rectTransform;
                        lr.anchorMin = lr.anchorMax = n;
                        lr.anchoredPosition = Vector2.zero;
                    }
                }
            }

            for (int i = used; i < views.Count; i++)
            {
                views[i].icon.gameObject.SetActive(false);
                if (views[i].label != null) views[i].label.gameObject.SetActive(false);
            }

            // 플레이어 화살표는 항상 마커 위에
            if (isFull && fullPlayerArrow != null) fullPlayerArrow.SetAsLastSibling();
        }

        private MarkerView getView(List<MarkerView> views, RectTransform parent, int index)
        {
            if (index < views.Count) return views[index];

            MarkerView mv = new MarkerView();
            if (markerPrefab != null)
                mv.icon = Instantiate(markerPrefab, parent);
            else
            {
                GameObject go = new GameObject("Marker", typeof(RectTransform), typeof(Image));
                go.transform.SetParent(parent, false);
                mv.icon = go.GetComponent<Image>();
                mv.icon.rectTransform.sizeDelta = new Vector2(18f, 18f);
            }
            mv.icon.raycastTarget = false;
            if (roomLabelPrefab != null)
            {
                mv.label = Instantiate(roomLabelPrefab, parent);
                mv.label.raycastTarget = false;
            }
            views.Add(mv);
            return mv;
        }

        private static void clearMarkers(List<MarkerView> views)
        {
            foreach (MarkerView v in views)
            {
                if (v.icon != null) Destroy(v.icon.gameObject);
                if (v.label != null) Destroy(v.label.gameObject);
            }
            views.Clear();
        }
    }
}