using System.Collections.Generic;
using System.Linq;
using Fusion;
using TrustNoOne.Missions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>
/// [에디터 전용, 1클릭 도구] 메인 게임 씬(GamePlay)의 옛 미션을 지우고 새 미션을 놓는다 (6b 명세 3장).
///
/// [하는 일] 실제 게임 데이터 준비(없을 때만) → 옛 미션 프리팹 제거 → 옛 MissionSystem 컴포넌트만 제거 →
///  `Missions (새 미션)` 아래에 MissionManager · PlayerHealthActorGate · 새 장치(없을 때만) → 옛 아이템 자리 후보 묶음 제거 →
///  팀원 스크립트의 미션 시스템 칸 연결 → 미션 목록 패널의 옛 MissionUI 를 MissionListUI 로 바꿔 끼우기(6c) → 1층 아이템 NavMesh 굽기(6b 보강 — 매번) → 새 5종 겹침 경고 → 저장.
/// [다시 실행해도 안전 — SW7] 이름으로 찾아 이미 있으면 건드리지 않는다 → 기획자가 옮긴 배치 · 바꾼 데이터 숫자가 남는다.
///  지운 것만 다시 만든다. 처음 배치로 되돌리려면 `Missions (새 미션)` 을 지우고 다시 실행한다.
/// [MissionSystem 오브젝트 — SW8] 옛 MissionSystem 과 같은 오브젝트에 RoleAssignment · GameTimer · GameEndSystem 이 붙어 있어
///  오브젝트째 지우지 않고 그 컴포넌트만 뗀다.
/// [팀원 타입을 이름으로 다루는 이유] 엔진 밖 컴파일 검사(VerifyAll)에 팀원 스크립트가 없다 → 팀원 타입(MissionSystem · RoleAssignment 등)을
///  코드에 직접 쓰지 않고 타입 이름 · 직렬화 칸 이름으로 찾는다.
/// [겹침 경고] 새 5종만 검사한다. 옛 미션 자리(벽에 붙은 밸브 · 차단기 등)는 원래 배치라 경고가 소음이 된다.
/// [근거] 6b 명세 SW1 · SW2 · SW5 · SW6 · SW7 · SW8 · SW11 · SW12 (사용자 결정 — 질문 1 ~ 6, 접근 1), 6b 보강 명세 NS2 · NS4 · NS12
/// </summary>
public static class GamePlayMissionInstaller
{
    private const string ScenePath = "Assets/Scenes/GamePlay.unity";
    private const string OldMissionPrefabFolder = "Assets/Prefabs/Mission/";
    private const string RootName = "Missions (새 미션)";
    private const string ManagerName = "MissionManager";
    private const string ActorGateName = "PlayerHealthActorGate";
    private const string TestOverlayName = "[테스트] MissionDebugOverlay";
    private const string DraftFolder = "Assets/0. Member/SuHan/MissionRedesign_Draft";
    private const string DataFolder = DraftFolder + "/Data/Game";
    private const string PoolPath = DataFolder + "/MissionPool_Game.asset";

    // 방화문 = 탈출 미션 (EscapeUnlocked — 팀원 탈출 코드와의 연결은 6d)
    private const string EscapeMissionId = "TG005";

    // 한 판: 단체 5 전부 · 시민마다 개인 2 · 살인마 0 · 행동 목표 0 (SW5 — 사용자 결정 A, 4단계 진행 안 함)
    private const int TeamCount = 5;
    private const int PersonalCount = 2;

    // 미션 목록 칸의 줄 간격 (6c 테스트 후 — 위에서부터 붙여 쌓을 때)
    private const float ListLineSpacing = 4f;

    // 새 장치는 이 지점(플레이어 생성기 근처 = 시작 지점)을 바라보게 놓는다
    private static readonly Vector3 SpawnFocus = Vector3.zero;

    private sealed class DefSpec
    {
        public string Id;
        public string Name;
        public MissionCategory Category;
        public MissionEventType Trigger;
        public int Required = 1;
        public float TimeLimit;
        public TimeLimitMode Mode = TimeLimitMode.SinceLastProgress;
    }

    // 실제 게임 미션 정의 (SW11): 값은 테스트에서 검증한 값. 테스트 설정 메뉴와 따로 둔다 — 기획 조정으로 달라질 수 있다
    private static readonly DefSpec[] Definitions =
    {
        new DefSpec { Id = "TG001", Name = "발전기 작동시키기", Category = MissionCategory.Team, Trigger = MissionEventType.GeneratorRepaired, Required = 3, TimeLimit = 120f },
        new DefSpec { Id = "TG002", Name = "코드 순서 맞추기", Category = MissionCategory.Team, Trigger = MissionEventType.SecurityCodeEntered },
        new DefSpec { Id = "TG003", Name = "고장난 장비 조립", Category = MissionCategory.Team, Trigger = MissionEventType.EquipmentAssembled, Required = 4 },
        new DefSpec { Id = "TG004", Name = "생명 유지 장치 복구", Category = MissionCategory.Team, Trigger = MissionEventType.LifeSupportRestored, Required = 3, TimeLimit = 120f, Mode = TimeLimitMode.SinceFirstProgress },
        new DefSpec { Id = "TG005", Name = "대형 방화문 열기", Category = MissionCategory.Team, Trigger = MissionEventType.FireDoorOpened },
        new DefSpec { Id = "PS001", Name = "밸브 잠그기", Category = MissionCategory.Personal, Trigger = MissionEventType.ValveClosed },
        new DefSpec { Id = "PS002", Name = "차단기 올리기", Category = MissionCategory.Personal, Trigger = MissionEventType.BreakerRestored },
        new DefSpec { Id = "PS003", Name = "전선 연결하기", Category = MissionCategory.Personal, Trigger = MissionEventType.WiresConnected },
        new DefSpec { Id = "PS004", Name = "안테나 방향 맞추기", Category = MissionCategory.Personal, Trigger = MissionEventType.AntennaAligned },
        new DefSpec { Id = "PS005", Name = "압력 수치 맞추기", Category = MissionCategory.Personal, Trigger = MissionEventType.PressureStabilized },
        new DefSpec { Id = "PS006", Name = "필터 청소하기", Category = MissionCategory.Personal, Trigger = MissionEventType.FilterCleaned },
    };

    private sealed class Placement
    {
        public string Name;
        public string PrefabPath;
        public Vector3 Position;
        public float Yaw;
        public bool FaceSpawn;
        public bool CheckOverlap;
    }

    // 6b 명세 3장 배치표 — 옛 미션 자리(같은 위치 · 방향, 2026-10-11 씬에서 읽음)와 새 5종(시작 홀 빈 칸, 겹침 검사)
    private static readonly Placement[] Placements =
    {
        new Placement { Name = "Generator_A", PrefabPath = LegacyMissionConverter.GeneratorStationPath, Position = new Vector3(2.17f, 0f, 3.2f), Yaw = -90f },
        new Placement { Name = "Generator_B", PrefabPath = LegacyMissionConverter.GeneratorStationPath, Position = new Vector3(-7.6f, 0f, 3.2f), Yaw = 90f },
        new Placement { Name = "Generator_C", PrefabPath = LegacyMissionConverter.GeneratorStationPath, Position = new Vector3(-2.62f, 0f, 5.97f), Yaw = 180f },
        new Placement { Name = "Antenna_A", PrefabPath = LegacyMissionConverter.AntennaStationPath, Position = new Vector3(-2.23f, 0f, -2.16f) },
        new Placement { Name = "Antenna_B", PrefabPath = LegacyMissionConverter.AntennaStationPath, Position = new Vector3(-3.87f, 0f, -2.16f) },
        new Placement { Name = "Breaker", PrefabPath = LegacyMissionConverter.BreakerStationPath, Position = new Vector3(0f, 1.25f, -3.26f) },
        new Placement { Name = "Valve", PrefabPath = LegacyMissionConverter.ValveStationPath, Position = new Vector3(1.39f, 1.18f, -0.75f), Yaw = -90f },
        new Placement { Name = "Wiring", PrefabPath = LegacyMissionConverter.WiringStationPath, Position = new Vector3(-0.28f, 0f, 2.06f), Yaw = 90f },
        new Placement { Name = "Filter", PrefabPath = LegacyMissionConverter.FilterStationPath, Position = new Vector3(-0.24f, 0.03f, 0f) },
        new Placement { Name = "Vacuum", PrefabPath = LegacyMissionConverter.VacuumToolPath, Position = new Vector3(0f, 0.79f, 2.04f) },
        new Placement { Name = "Pressure", PrefabPath = LegacyMissionConverter.PressureStationPath, Position = new Vector3(-1f, 0f, -7f), FaceSpawn = true, CheckOverlap = true },
        new Placement { Name = "Code", PrefabPath = LegacyMissionConverter.CodeStationPath, Position = new Vector3(-7f, 0f, 17f), FaceSpawn = true, CheckOverlap = true },
        new Placement { Name = "LifeSupport", PrefabPath = LegacyMissionConverter.LifeSupportStationPath, Position = new Vector3(-1f, 0f, 18f), FaceSpawn = true, CheckOverlap = true },
        new Placement { Name = "Assembly", PrefabPath = LegacyMissionConverter.AssemblyStationPath, Position = new Vector3(5f, 0f, 18f), FaceSpawn = true, CheckOverlap = true },
        // 방화문: 북쪽 벽(z 26.5) 앞에서 남쪽(시작 홀)을 보게 — 벽과 나란히 180° 고정
        new Placement { Name = "FireDoor", PrefabPath = LegacyMissionConverter.FireDoorStationPath, Position = new Vector3(-1f, 0f, 24.5f), Yaw = 180f, CheckOverlap = true },
    };

    // 1층 아이템 NavMesh (6b 보강 NS2 · NS3): 높이 지면 −0.5 ~ +2.0m, 가로 · 세로 = 맵 바닥 전체 (x −20 ~ 129, z −34 ~ 27)
    private static readonly Bounds GamePlayNavMeshVolume = new Bounds(new Vector3(54.5f, 0.75f, -3.5f), new Vector3(149f, 2.5f, 61f));
    private const string GamePlayNavMeshPath = DataFolder + "/GamePlay_ItemNavMesh.asset";

    // ═════════════════════════════════════════════════════════════
    //  메뉴
    // ═════════════════════════════════════════════════════════════

    [MenuItem("SuHan/Apply Missions To GamePlay")]
    public static void Apply()
    {
        if (!TryOpenGamePlay(out Scene scene))
            return;

        MissionPool pool = EnsureGameData();

        if (pool == null)
            return;

        int removedOld = RemoveOldMissionInstances(scene);
        int removedSystems = RemoveOldMissionSystems(scene);

        Transform root = FindOrCreateRoot(scene);
        MissionManager manager = EnsureMissionManager(root, pool, out bool managerCreated);
        bool gateCreated = EnsureActorGate(root);

        List<GameObject> placedNow = new List<GameObject>();
        int skipped = 0;

        foreach (Placement placement in Placements)
        {
            if (root.Find(placement.Name) != null)
            {
                skipped++;
                continue;
            }

            GameObject placed = Place(placement, root);

            if (placed != null)
                placedNow.Add(placed);
        }

        int spotGroupsRemoved = RemoveOldItemSpots(root);
        int wired = WireTeamScripts(scene, manager);
        int listSwapped = SwapMissionListUI(scene, manager);
        int listLayoutFixed = FixMissionListLayout();
        // 장치를 다 놓은 뒤 굽는다 — 장치 몸체도 길을 막는 것으로 들어간다 (6b 보강 NS2 · NS4: 매번 다시 굽기)
        bool navMeshBaked = ItemNavMeshBaker.Bake(root, GamePlayNavMeshVolume, GamePlayNavMeshPath);
        int overlaps = CheckOverlaps(placedNow, root);

        EditorSceneManager.MarkSceneDirty(scene);
        // 저장할 때 Fusion 이 씬 오브젝트의 정렬 키를 굽는다 (호스트 · 손님이 같은 오브젝트로 짝을 맞추는 번호)
        EditorSceneManager.SaveScene(scene);

        Debug.Log($"[GamePlayMissionInstaller] 완료 — 옛 미션 {removedOld}개 · 옛 MissionSystem 컴포넌트 {removedSystems}개 제거, " +
                  $"새 장치 {placedNow.Count}개 놓음 · {skipped}개 이미 있어 건너뜀, MissionManager {(managerCreated ? "새로 만듦" : "있음")}, " +
                  $"PlayerHealthActorGate {(gateCreated ? "새로 만듦" : "있음")}, 옛 자리 후보 묶음 {spotGroupsRemoved}개 제거, NavMesh {(navMeshBaked ? "구움" : "실패")}, " +
                  $"미션 시스템 칸 {wired}개 연결, 미션 목록 UI {listSwapped}개 교체 · 칸 설정 {listLayoutFixed}개 고침, 겹침 경고 {overlaps}개. 멀티 테스트 전에는 SuHan/Build Full Game (Windows) 로 다시 빌드하세요.");
    }

    /// <summary>테스트 동안만 메인 씬에 디버그 패널(단체 · 개인 미션 표시, 호스트 F8 ~ F10)을 넣는다 (SW12 — 미션 목록 UI 는 6c). 커밋 전에 끄기.</summary>
    [MenuItem("SuHan/GamePlay 테스트 표시 켜기")]
    public static void AddTestOverlay()
    {
        if (!TryOpenGamePlay(out Scene scene))
            return;

        if (FindRoot(scene, TestOverlayName) != null)
        {
            Debug.Log("[GamePlayMissionInstaller] 테스트 표시가 이미 있습니다.");
            return;
        }

        GameObject overlay = new GameObject(TestOverlayName);
        SceneManager.MoveGameObjectToScene(overlay, scene);
        overlay.AddComponent<MissionDebugOverlay>();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[GamePlayMissionInstaller] 메인 씬에 테스트 표시를 넣었습니다. 커밋 전에 SuHan/GamePlay 테스트 표시 끄기 를 실행하세요.");
    }

    [MenuItem("SuHan/GamePlay 테스트 표시 끄기")]
    public static void RemoveTestOverlay()
    {
        if (!TryOpenGamePlay(out Scene scene))
            return;

        GameObject overlay = FindRoot(scene, TestOverlayName);

        if (overlay == null)
        {
            Debug.Log("[GamePlayMissionInstaller] 테스트 표시가 없습니다.");
            return;
        }

        Object.DestroyImmediate(overlay);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[GamePlayMissionInstaller] 메인 씬에서 테스트 표시를 뺐습니다.");
    }

    // ═════════════════════════════════════════════════════════════
    //  씬 열기 · 실제 게임 데이터 (SW11 — 없을 때만 만든다)
    // ═════════════════════════════════════════════════════════════

    private static bool TryOpenGamePlay(out Scene scene)
    {
        scene = default;

        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            return false;

        scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        if (!scene.IsValid())
        {
            Debug.LogError($"[GamePlayMissionInstaller] 메인 씬을 열지 못했습니다: {ScenePath}");
            return false;
        }

        return true;
    }

    private static MissionPool EnsureGameData()
    {
        EnsureFolder(DraftFolder, "Data");
        EnsureFolder(DraftFolder + "/Data", "Game");

        int created = 0;
        List<MissionDefinition> definitions = new List<MissionDefinition>();

        foreach (DefSpec spec in Definitions)
            definitions.Add(LoadOrCreateDefinition(spec, ref created));

        MissionPool pool = AssetDatabase.LoadAssetAtPath<MissionPool>(PoolPath);

        if (pool == null)
        {
            pool = ScriptableObject.CreateInstance<MissionPool>();
            SerializedObject so = new SerializedObject(pool);
            SerializedProperty list = so.FindProperty("definitions");
            list.ClearArray();

            for (int i = 0; i < definitions.Count; i++)
            {
                list.InsertArrayElementAtIndex(i);
                list.GetArrayElementAtIndex(i).objectReferenceValue = definitions[i];
            }

            so.FindProperty("teamMissionCount").intValue = TeamCount;
            so.FindProperty("personalMissionsPerCitizen").intValue = PersonalCount;
            so.FindProperty("killerMissionsPerKiller").intValue = 0;   // 기획서에 살인마 미션 본문이 없음 (SW3)
            so.FindProperty("actionGoalsPerPlayer").intValue = 0;      // 4단계(개인 행동 목표) 진행 안 함 (SW5)
            so.ApplyModifiedPropertiesWithoutUndo();

            AssetDatabase.CreateAsset(pool, PoolPath);
            created++;
        }

        if (created > 0)
        {
            AssetDatabase.SaveAssets();
            Debug.Log($"[GamePlayMissionInstaller] 실제 게임 데이터 {created}개를 새로 만들었습니다 ({DataFolder}). 이미 있던 데이터는 그대로 둡니다.");
        }

        return pool;
    }

    private static MissionDefinition LoadOrCreateDefinition(DefSpec spec, ref int created)
    {
        string path = $"{DataFolder}/{spec.Id}.asset";
        MissionDefinition existing = AssetDatabase.LoadAssetAtPath<MissionDefinition>(path);

        if (existing != null)
            return existing;

        MissionDefinition asset = ScriptableObject.CreateInstance<MissionDefinition>();
        SerializedObject so = new SerializedObject(asset);
        so.FindProperty("id").stringValue = spec.Id;
        so.FindProperty("displayName").stringValue = spec.Name;
        // MissionEventType 은 값이 연속적이지 않아 enumValueIndex(선언 순서)를 쓰면 엉뚱한 값이 들어간다 → intValue
        so.FindProperty("category").intValue = (int)spec.Category;
        so.FindProperty("kind").intValue = (int)ObjectiveKind.Count;
        so.FindProperty("trigger").intValue = (int)spec.Trigger;
        so.FindProperty("requiredCount").intValue = spec.Required;
        so.FindProperty("timeLimitSeconds").floatValue = spec.TimeLimit;
        so.FindProperty("timeLimitMode").intValue = (int)spec.Mode;
        so.ApplyModifiedPropertiesWithoutUndo();

        AssetDatabase.CreateAsset(asset, path);
        created++;
        return asset;
    }

    // ═════════════════════════════════════════════════════════════
    //  지우기 (옛 미션 · 옛 MissionSystem 컴포넌트)
    // ═════════════════════════════════════════════════════════════

    private static int RemoveOldMissionInstances(Scene scene)
    {
        List<string> removed = new List<string>();

        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (!PrefabUtility.IsAnyPrefabInstanceRoot(root))
                continue;

            string path = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(root);

            if (string.IsNullOrEmpty(path) || !path.StartsWith(OldMissionPrefabFolder))
                continue;

            removed.Add(root.name);
            Object.DestroyImmediate(root);
        }

        if (removed.Count > 0)
            Debug.Log($"[GamePlayMissionInstaller] 옛 미션 {removed.Count}개 제거: {string.Join(", ", removed)}");

        return removed.Count;
    }

    private static int RemoveOldMissionSystems(Scene scene)
    {
        int removed = 0;

        foreach (MonoBehaviour system in FindComponentsByTypeName(scene, "MissionSystem"))
        {
            if (PrefabUtility.IsPartOfPrefabInstance(system))
            {
                Debug.LogError($"[GamePlayMissionInstaller] '{system.gameObject.name}' 의 옛 MissionSystem 은 프리팹 인스턴스의 일부라 떼지 못했습니다 — 직접 확인해 주세요.", system);
                continue;
            }

            string owner = system.gameObject.name;
            Object.DestroyImmediate(system);
            removed++;
            Debug.Log($"[GamePlayMissionInstaller] '{owner}' 에서 옛 MissionSystem 컴포넌트만 뗐습니다 (같은 오브젝트의 역할 · 시간 · 승패 컴포넌트는 그대로).");
        }

        return removed;
    }

    // ═════════════════════════════════════════════════════════════
    //  놓기 (없을 때만)
    // ═════════════════════════════════════════════════════════════

    private static Transform FindOrCreateRoot(Scene scene)
    {
        GameObject root = FindRoot(scene, RootName);

        if (root == null)
        {
            root = new GameObject(RootName);
            SceneManager.MoveGameObjectToScene(root, scene);
        }

        return root.transform;
    }

    private static MissionManager EnsureMissionManager(Transform root, MissionPool pool, out bool created)
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

    /// <summary>미션 장치가 "행동 가능한가(살아 있음 등)"를 묻는 곳 — 씬에 하나 있어야 한다 (테스트 씬과 같음).</summary>
    private static bool EnsureActorGate(Transform root)
    {
        if (Object.FindFirstObjectByType<PlayerHealthActorGate>(FindObjectsInactive.Include) != null)
            return false;

        GameObject gate = new GameObject(ActorGateName);
        gate.transform.SetParent(root, false);
        gate.AddComponent<PlayerHealthActorGate>();
        return true;
    }

    private static GameObject Place(Placement placement, Transform root)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(placement.PrefabPath);

        if (prefab == null)
        {
            Debug.LogError($"[GamePlayMissionInstaller] {placement.Name}: 프리팹이 없습니다 ({placement.PrefabPath}). 메뉴 SuHan/Setup Solo Mission Test 로 변환을 먼저 실행하세요.");
            return null;
        }

        GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, root);
        instance.name = placement.Name;

        Quaternion rotation = Quaternion.Euler(0f, placement.Yaw, 0f);

        if (placement.FaceSpawn)
        {
            Vector3 toSpawn = SpawnFocus - placement.Position;
            toSpawn.y = 0f;

            if (toSpawn.sqrMagnitude > 0.01f)
                rotation = Quaternion.LookRotation(toSpawn);
        }

        instance.transform.SetPositionAndRotation(placement.Position, rotation);
        return instance;
    }

    /// <summary>
    /// 6b 의 아이템 자리 후보 묶음(`ItemSpots …`)을 지운다 (6b 보강 NS12 — 아이템은 이제 1층 NavMesh 위 무작위).
    /// 우리 도구가 붙인 이름으로 찾는다 → ItemSpots 스크립트를 지운 뒤에도 정리된다.
    /// </summary>
    private static int RemoveOldItemSpots(Transform root)
    {
        List<GameObject> old = new List<GameObject>();

        foreach (Transform child in root)
        {
            if (child.name.StartsWith("ItemSpots "))
                old.Add(child.gameObject);
        }

        foreach (GameObject spots in old)
            Object.DestroyImmediate(spots);

        if (old.Count > 0)
            Debug.Log($"[GamePlayMissionInstaller] 옛 아이템 자리 후보 묶음 {old.Count}개를 지웠습니다 (아이템은 1층 NavMesh 위 무작위).");

        return old.Count;
    }

    // ═════════════════════════════════════════════════════════════
    //  연결 · 겹침 검사
    // ═════════════════════════════════════════════════════════════

    /// <summary>팀원 스크립트의 미션 시스템 칸(missionSystem)에 새 MissionManager 를 넣는다 (칸 타입은 6b 에서 MissionManager 로 바뀐다).</summary>
    private static int WireTeamScripts(Scene scene, MissionManager manager)
    {
        int wired = 0;

        foreach (string typeName in new[] { "RoleAssignment", "GameEndSystem", "MissionProgressUI" })
        {
            foreach (MonoBehaviour behaviour in FindComponentsByTypeName(scene, typeName))
            {
                SerializedObject so = new SerializedObject(behaviour);
                SerializedProperty field = so.FindProperty("missionSystem");

                if (field == null || field.propertyType != SerializedPropertyType.ObjectReference)
                {
                    Debug.LogWarning($"[GamePlayMissionInstaller] {typeName} 에 missionSystem 칸이 없습니다.", behaviour);
                    continue;
                }

                field.objectReferenceValue = manager;

                if (field.objectReferenceValue != manager)
                {
                    Debug.LogWarning($"[GamePlayMissionInstaller] {typeName} 의 미션 시스템 칸에 새 MissionManager 를 넣지 못했습니다 — " +
                                     "칸 타입이 아직 옛 MissionSystem 인지 확인하세요 (팀원 스크립트 변경 · Unity 컴파일 뒤 다시 실행).", behaviour);
                    continue;
                }

                so.ApplyModifiedPropertiesWithoutUndo();
                wired++;
            }
        }

        return wired;
    }

    /// <summary>
    /// 미션 목록 패널의 옛 MissionUI 를 새 MissionListUI 로 바꿔 끼운다 (6c — 사용자 결정 A). 줄 프리팹 · 단체 칸 · 개인 칸은 그대로 옮긴다.
    /// 옛 컴포넌트가 없으면(이미 바꿨으면) 아무것도 하지 않는다 → 다시 실행해도 안전. 팀원 파일 MissionUI.cs 는 고치지 않는다 (6e 정리).
    /// </summary>
    private static int SwapMissionListUI(Scene scene, MissionManager manager)
    {
        int swapped = 0;

        foreach (MonoBehaviour oldUI in FindComponentsByTypeName(scene, "MissionUI"))
        {
            GameObject panel = oldUI.gameObject;

            if (PrefabUtility.IsPartOfPrefabInstance(oldUI))
            {
                Debug.LogError($"[GamePlayMissionInstaller] '{panel.name}' 의 옛 MissionUI 는 프리팹 인스턴스의 일부라 바꾸지 못했습니다 — 직접 확인해 주세요.", oldUI);
                continue;
            }

            if (panel.GetComponent<MissionListUI>() != null)
            {
                Debug.LogWarning($"[GamePlayMissionInstaller] '{panel.name}' 에 이미 MissionListUI 가 있어 옛 MissionUI 를 그대로 둡니다.", panel);
                continue;
            }

            SerializedObject oldSO = new SerializedObject(oldUI);
            Object itemPrefab = oldSO.FindProperty("itemPrefab")?.objectReferenceValue;
            Object sharedRoot = oldSO.FindProperty("sharedRoot")?.objectReferenceValue;
            Object personalRoot = oldSO.FindProperty("personalRoot")?.objectReferenceValue;

            if (itemPrefab == null || sharedRoot == null || personalRoot == null)
                Debug.LogWarning($"[GamePlayMissionInstaller] '{panel.name}' 의 옛 MissionUI 에 비어 있는 칸이 있습니다 (줄 프리팹 · 단체 칸 · 개인 칸) — 새 목록에서 연결을 확인해 주세요.", panel);

            MissionListUI list = panel.AddComponent<MissionListUI>();
            SerializedObject so = new SerializedObject(list);
            so.FindProperty("manager").objectReferenceValue = manager;
            so.FindProperty("itemPrefab").objectReferenceValue = itemPrefab;
            so.FindProperty("sharedRoot").objectReferenceValue = sharedRoot;
            so.FindProperty("personalRoot").objectReferenceValue = personalRoot;
            so.ApplyModifiedPropertiesWithoutUndo();

            Object.DestroyImmediate(oldUI);
            swapped++;
            Debug.Log($"[GamePlayMissionInstaller] '{panel.name}' 의 옛 MissionUI 를 MissionListUI 로 바꿔 끼웠습니다 (줄 프리팹 · 단체 칸 · 개인 칸 그대로).");
        }

        return swapped;
    }

    /// <summary>
    /// 미션 목록 칸(단체 · 개인)의 줄 쌓기 설정을 여러 줄에 맞게 바꾼다 (6c 테스트 후 수정).
    /// [원인] 칸이 300 × 200 이고 줄 프리팹도 300 × 200 인데, 칸의 세로 쌓기(VerticalLayoutGroup)가 줄 높이를 정하지 않고(Control Child Size 꺼짐)
    ///  남는 공간을 벌려 채우게(Child Force Expand 켜짐) 되어 있다 → 옛 게임처럼 칸마다 한 줄일 때만 맞고, 여러 줄이면 줄마다 200 씩 내려가
    ///  칸 · 패널 밖으로 넘치고 단체 · 개인 줄이 겹친다.
    /// [수정] 줄 높이를 글자 높이에 맞추고(Control Child Size 켬) 위에서부터 붙여 쌓는다(Force Expand 끔, 간격 4).
    ///  옛 설정일 때만 바꾼다 → 기획자가 다르게 바꾼 값은 다시 실행해도 그대로 둔다. 줄 프리팹(팀원)은 고치지 않는다.
    /// </summary>
    private static int FixMissionListLayout()
    {
        int fixedCount = 0;

        foreach (MissionListUI list in Object.FindObjectsByType<MissionListUI>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            SerializedObject so = new SerializedObject(list);

            foreach (string field in new[] { "sharedRoot", "personalRoot" })
            {
                Transform content = so.FindProperty(field).objectReferenceValue as Transform;
                VerticalLayoutGroup layout = content != null ? content.GetComponent<VerticalLayoutGroup>() : null;

                if (layout == null || (layout.childControlHeight && !layout.childForceExpandHeight))
                    continue;

                layout.childControlHeight = true;
                layout.childForceExpandHeight = false;

                if (layout.spacing <= 0f)
                    layout.spacing = ListLineSpacing;

                EditorUtility.SetDirty(layout);
                fixedCount++;
                Debug.Log($"[GamePlayMissionInstaller] '{content.name}' 줄 쌓기 설정을 여러 줄에 맞게 바꿨습니다 (줄 높이 = 글자 높이, 위에서부터 간격 {layout.spacing}).", content);
            }
        }

        return fixedCount;
    }

    /// <summary>새 5종이 벽 · 소품 콜라이더와 겹치면 경고 (렌더러 범위 상자, 바닥에서 10cm 위부터 — 바닥은 빼려고).</summary>
    private static int CheckOverlaps(List<GameObject> placed, Transform root)
    {
        Physics.SyncTransforms();
        int warnings = 0;

        foreach (GameObject instance in placed)
        {
            Placement placement = Placements.FirstOrDefault(p => p.Name == instance.name);

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
            Debug.LogWarning($"[GamePlayMissionInstaller] 겹침: {instance.name} ↔ {string.Join(", ", hits)} — 씬 뷰에서 확인 후 옮겨 주세요.", instance);
        }

        return warnings;
    }

    // ═════════════════════════════════════════════════════════════
    //  도우미
    // ═════════════════════════════════════════════════════════════

    private static GameObject FindRoot(Scene scene, string name)
    {
        return scene.GetRootGameObjects().FirstOrDefault(root => root.name == name);
    }

    /// <summary>타입 이름으로 씬의 컴포넌트를 찾는다 (팀원 타입을 코드에 직접 쓰지 않으려고 — 클래스 머리 주석).</summary>
    private static List<MonoBehaviour> FindComponentsByTypeName(Scene scene, string typeName)
    {
        List<MonoBehaviour> found = new List<MonoBehaviour>();

        foreach (GameObject root in scene.GetRootGameObjects())
        {
            foreach (MonoBehaviour behaviour in root.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (behaviour != null && behaviour.GetType().Name == typeName)
                    found.Add(behaviour);
            }
        }

        return found;
    }

    private static void EnsureFolder(string parent, string child)
    {
        if (!AssetDatabase.IsValidFolder($"{parent}/{child}"))
            AssetDatabase.CreateFolder(parent, child);
    }
}
