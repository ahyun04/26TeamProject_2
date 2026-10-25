using System.Collections.Generic;
using System.Linq;
using Fusion;
using TrustNoOne.Missions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>
/// [역할] 맵 배치표 한 줄 — 장치 하나의 이름 · 프리팹 · 자리 (9a 명세 MP4).
/// [이유] GamePlay · 4~12 처럼 맵마다 표만 다르고 놓는 방법은 같다.
/// [근거] 9a 명세 MP4 (접근 1 — 사용자 결정)
/// </summary>
public sealed class MissionPlacement
{
    /// <summary>씬 오브젝트 이름. 미션 루트에 같은 이름이 있으면 다시 놓지 않는다 (6b SW7 · 9a MP3).</summary>
    public string Name;
    public string PrefabPath;
    public Vector3 Position;

    /// <summary>세로축 회전(도). 장치 앞(+Z)이 보는 쪽.</summary>
    public float Yaw;

    /// <summary>켜면 Yaw 대신 맵의 얼굴 기준점(MissionMapSpec.FaceFocus)을 바라보게 놓는다.</summary>
    public bool FaceFocus;

    /// <summary>켜면 놓은 뒤 벽 · 소품 콜라이더와 겹치는지 경고한다 (벽에 거는 장치는 끈다 — 벽과 닿는 것이 정상).</summary>
    public bool CheckOverlap;
}

/// <summary>
/// [역할] 맵 하나의 설정 묶음: 씬 · 배치표 · 1층 아이템 NavMesh (9a 명세 MP4).
/// [이유] 공통 단계(MissionMapInstaller)가 맵마다 다른 값만 이 묶음에서 읽는다 → 다음 맵은 묶음 하나만 더하면 된다.
/// [근거] 9a 명세 MP4 · MP8
/// </summary>
public sealed class MissionMapSpec
{
    /// <summary>로그 · 안내에 쓰는 맵 이름 (예: GamePlay, 4~12).</summary>
    public string Label;
    public string ScenePath;
    public MissionPlacement[] Placements;

    /// <summary>FaceFocus 가 켜진 장치가 바라볼 지점 (GamePlay = 시작 지점).</summary>
    public Vector3 FaceFocus;

    public Bounds NavMeshVolume;
    public string NavMeshAssetPath;

    /// <summary>예전 기본 NavMesh 범위 — 오브젝트의 범위가 이 값 그대로면 새 범위로 바꾼다 (ItemNavMeshBaker, 6b 보강 NS4).</summary>
    public Bounds[] OutdatedNavMeshVolumes = new Bounds[0];
}

/// <summary>
/// [역할 · 에디터 전용] 맵 씬에 미션 장치를 놓는 공통 단계 (9a 명세 MP4). GamePlay · 4~12 메뉴가 같이 쓴다.
///  씬 열기 → `Missions (새 미션)` 루트 → MissionManager · PlayerHealthActorGate (씬에 있으면 그대로, 없을 때만 만듦) →
///  배치표대로 놓기 (같은 이름이 있으면 건너뜀) → 1층 아이템 NavMesh 굽기 → 겹침 경고 → 저장.
/// [이유] 처음에는 메인 씬 도구(GamePlayMissionInstaller) 안에 상수로 있었다. 4~12 맵이 생겨 맵마다 다른 값(씬 · 표 · NavMesh)만
///  설정 묶음으로 빼고 단계는 한 벌로 둔다. 동작 · 로그 문구는 메인 씬 도구 때와 같다.
/// [다시 실행해도 안전 — SW7 · MP3] 이름으로 찾아 이미 있으면 건드리지 않는다 → 기획자가 옮긴 자리가 남는다.
///  처음 배치로 되돌리려면 `Missions (새 미션)` 을 지우고 다시 실행한다.
/// [저장] 씬을 저장할 때 Fusion 이 씬 오브젝트의 정렬 키를 굽는다 (호스트 · 손님이 같은 오브젝트로 짝을 맞추는 번호) — 반드시 저장한다.
/// [근거] 6b 명세 SW5 ~ SW8 · SW12, 6b 보강 NS2 ~ NS4, 9a 명세 MP3 ~ MP5 · MP8 · MP9
/// </summary>
public static class MissionMapInstaller
{
    public const string RootName = "Missions (새 미션)";
    public const string GameDataFolder = "Assets/0. Member/SuHan/MissionRedesign_Draft/Data/Game";
    public const string GamePoolPath = GameDataFolder + "/MissionPool_Game.asset";

    // 방화문 = 탈출 미션 (EscapeUnlocked)
    public const string EscapeMissionId = "TG005";

    private const string ManagerName = "MissionManager";
    private const string ActorGateName = "PlayerHealthActorGate";
    private const string TestOverlayName = "[테스트] MissionDebugOverlay";

    // ═════════════════════════════════════════════════════════════
    //  씬
    // ═════════════════════════════════════════════════════════════

    public static bool TryOpenScene(MissionMapSpec spec, out Scene scene)
    {
        scene = default;

        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            return false;

        scene = EditorSceneManager.OpenScene(spec.ScenePath, OpenSceneMode.Single);

        if (!scene.IsValid())
        {
            Debug.LogError($"[MissionMapInstaller] {spec.Label} 씬을 열지 못했습니다: {spec.ScenePath}");
            return false;
        }

        return true;
    }

    public static void SaveScene(Scene scene)
    {
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
    }

    public static GameObject FindRoot(Scene scene, string name)
    {
        return scene.GetRootGameObjects().FirstOrDefault(root => root.name == name);
    }

    public static Transform FindOrCreateRoot(Scene scene)
    {
        GameObject root = FindRoot(scene, RootName);

        if (root == null)
        {
            root = new GameObject(RootName);
            SceneManager.MoveGameObjectToScene(root, scene);
        }

        return root.transform;
    }

    // ═════════════════════════════════════════════════════════════
    //  미션 시스템 (씬에 있으면 그대로)
    // ═════════════════════════════════════════════════════════════

    /// <summary>씬의 MissionManager 를 찾고, 없으면 루트 아래에 만들어 풀 · 방화문 미션을 넣는다.</summary>
    public static MissionManager EnsureMissionManager(Transform root, MissionPool pool, out bool created)
    {
        created = false;
        MissionManager existing = Object.FindFirstObjectByType<MissionManager>(FindObjectsInactive.Include);

        if (existing != null)
            return existing;

        GameObject managerObject = new GameObject(ManagerName);
        managerObject.transform.SetParent(root, false);
        managerObject.AddComponent<NetworkObject>();
        MissionManager manager = managerObject.AddComponent<MissionManager>();

        SerializedObject so = new SerializedObject(manager);
        so.FindProperty("pool").objectReferenceValue = pool;
        so.FindProperty("escapeMissionId").stringValue = EscapeMissionId;
        so.ApplyModifiedPropertiesWithoutUndo();

        created = true;
        return manager;
    }

    /// <summary>씬에 있던 MissionManager 가 실제 게임 풀을 쓰는지 확인만 한다 — 다르면 경고, 바꾸지 않는다 (9a MP5: 공유 설정은 맵 담당이 정한다).</summary>
    public static void WarnIfPoolDiffers(MissionManager manager, MissionPool expected, string label)
    {
        if (manager == null)
            return;

        Object current = new SerializedObject(manager).FindProperty("pool").objectReferenceValue;

        if (current != expected)
            Debug.LogWarning($"[MissionMapInstaller] {label}: 씬의 MissionManager 풀이 실제 게임 풀({GamePoolPath})이 아닙니다 " +
                             $"(지금: {(current != null ? AssetDatabase.GetAssetPath(current) : "비어 있음")}). 바꾸지 않았습니다 — 의도한 설정인지 확인하세요.", manager);
    }

    /// <summary>미션 장치가 "행동 가능한가(살아 있음 등)"를 묻는 곳 — 씬에 하나 있어야 한다. 없을 때만 만든다.</summary>
    public static bool EnsureActorGate(Transform root)
    {
        if (Object.FindFirstObjectByType<PlayerHealthActorGate>(FindObjectsInactive.Include) != null)
            return false;

        GameObject gate = new GameObject(ActorGateName);
        gate.transform.SetParent(root, false);
        gate.AddComponent<PlayerHealthActorGate>();
        return true;
    }

    // ═════════════════════════════════════════════════════════════
    //  놓기 (없을 때만)
    // ═════════════════════════════════════════════════════════════

    /// <summary>배치표에서 루트에 아직 없는 장치만 놓는다. 이미 있는 이름은 건너뛴다 (SW7 · MP3).</summary>
    public static List<GameObject> PlaceMissing(MissionMapSpec spec, Transform root, out int skipped)
    {
        List<GameObject> placed = new List<GameObject>();
        skipped = 0;

        foreach (MissionPlacement placement in spec.Placements)
        {
            if (root.Find(placement.Name) != null)
            {
                skipped++;
                continue;
            }

            GameObject instance = Place(spec, placement, root);

            if (instance != null)
                placed.Add(instance);
        }

        return placed;
    }

    private static GameObject Place(MissionMapSpec spec, MissionPlacement placement, Transform root)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(placement.PrefabPath);

        if (prefab == null)
        {
            Debug.LogError($"[MissionMapInstaller] {spec.Label} {placement.Name}: 프리팹이 없습니다 ({placement.PrefabPath}). 메뉴 SuHan/Setup Solo Mission Test 로 변환을 먼저 실행하세요.");
            return null;
        }

        GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, root);
        instance.name = placement.Name;

        Quaternion rotation = Quaternion.Euler(0f, placement.Yaw, 0f);

        if (placement.FaceFocus)
        {
            Vector3 toFocus = spec.FaceFocus - placement.Position;
            toFocus.y = 0f;

            if (toFocus.sqrMagnitude > 0.01f)
                rotation = Quaternion.LookRotation(toFocus);
        }

        instance.transform.SetPositionAndRotation(placement.Position, rotation);
        return instance;
    }

    // ═════════════════════════════════════════════════════════════
    //  NavMesh · 겹침 검사
    // ═════════════════════════════════════════════════════════════

    /// <summary>장치를 다 놓은 뒤 굽는다 — 장치 몸체도 길을 막는 것으로 들어간다 (6b 보강 NS2 · NS4: 매번 다시 굽기).</summary>
    public static bool BakeNavMesh(MissionMapSpec spec, Transform root)
    {
        return ItemNavMeshBaker.Bake(root, spec.NavMeshVolume, spec.NavMeshAssetPath, spec.OutdatedNavMeshVolumes);
    }

    /// <summary>이번에 놓은 장치 중 CheckOverlap 인 것이 벽 · 소품 콜라이더와 겹치면 경고 (렌더러 범위 상자, 바닥에서 10cm 위부터 — 바닥은 빼려고).</summary>
    public static int CheckOverlaps(MissionMapSpec spec, List<GameObject> placed, Transform root)
    {
        Physics.SyncTransforms();
        int warnings = 0;

        foreach (GameObject instance in placed)
        {
            MissionPlacement placement = spec.Placements.FirstOrDefault(p => p.Name == instance.name);

            if (placement == null || !placement.CheckOverlap)
                continue;

            Renderer[] renderers = instance.GetComponentsInChildren<Renderer>(false).Where(r => r.enabled).ToArray();

            if (renderers.Length == 0)
                continue;

            Bounds bounds = renderers[0].bounds;

            foreach (Renderer renderer in renderers)
                bounds.Encapsulate(renderer.bounds);

            Vector3 min = bounds.min;
            min.y += 0.1f;

            if (bounds.max.y <= min.y)
                continue;

            Bounds box = new Bounds();
            box.SetMinMax(min, bounds.max);

            HashSet<string> hits = new HashSet<string>();

            foreach (Collider collider in Physics.OverlapBox(box.center, box.extents, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore))
            {
                if (!collider.transform.IsChildOf(root))
                    hits.Add(collider.gameObject.name);
            }

            if (hits.Count == 0)
                continue;

            warnings++;
            Debug.LogWarning($"[MissionMapInstaller] {spec.Label} 겹침: {instance.name} ↔ {string.Join(", ", hits)} — 씬 뷰에서 확인 후 옮겨 주세요.", instance);
        }

        return warnings;
    }

    // ═════════════════════════════════════════════════════════════
    //  테스트 표시 (SW12 · MP9 — 커밋 전에 끈다)
    // ═════════════════════════════════════════════════════════════

    /// <summary>테스트 동안만 씬에 디버그 패널(단체 · 개인 미션 표시, 호스트 F8 ~ F10)을 넣는다.</summary>
    public static void AddTestOverlay(MissionMapSpec spec)
    {
        if (!TryOpenScene(spec, out Scene scene))
            return;

        if (FindRoot(scene, TestOverlayName) != null)
        {
            Debug.Log($"[MissionMapInstaller] {spec.Label}: 테스트 표시가 이미 있습니다.");
            return;
        }

        GameObject overlay = new GameObject(TestOverlayName);
        SceneManager.MoveGameObjectToScene(overlay, scene);
        overlay.AddComponent<MissionDebugOverlay>();

        SaveScene(scene);
        Debug.Log($"[MissionMapInstaller] {spec.Label} 씬에 테스트 표시를 넣었습니다. 커밋 전에 SuHan/{spec.Label} 테스트 표시 끄기 를 실행하세요.");
    }

    public static void RemoveTestOverlay(MissionMapSpec spec)
    {
        if (!TryOpenScene(spec, out Scene scene))
            return;

        GameObject overlay = FindRoot(scene, TestOverlayName);

        if (overlay == null)
        {
            Debug.Log($"[MissionMapInstaller] {spec.Label}: 테스트 표시가 없습니다.");
            return;
        }

        Object.DestroyImmediate(overlay);
        SaveScene(scene);
        Debug.Log($"[MissionMapInstaller] {spec.Label} 씬에서 테스트 표시를 뺐습니다.");
    }
}
