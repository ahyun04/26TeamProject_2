using System.Collections.Generic;
using Fusion;
using TrustNoOne.Missions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>
/// [에디터 전용, 1클릭 도구] SuhanTest 씬을 새 기획서(단체 미션 / 개인 미션 / 개인 행동 목표 기획서) 기준 테스트 구성으로 만든다.
///
/// [배치] (stage1 명세 5절)
///  - 단체 TG001 발전기 작동시키기: 진짜 GeneratorStation 3대 (LegacyMissionConverter 로 옛 발전기 프리팹을 변환)
///  - 개인 PS001 밸브 / PS002 차단기 / PS004 안테나: 진짜 스테이션 (2a, LegacyMissionConverter 로 옛 프리팹 변환, 실패 시 자리 표시)
///  - 단체 TG002~TG005, 개인 PS003 · PS005 · PS006: 아직 이식 전이라 "자리 표시" HoldStation 큐브 (F 2초)
///      단체는 Lock(완료 후 잠금), 개인은 ResetForNext(완료 후 원래대로) — 명세 S3
///  - 개인 행동 목표 AG101 뛰지 않는다: TestRunReporter (Shift 달리기 감지)
///  - 디버그 패널(F9 확정 / F10 공개, 단체 미션 남은 시간), 홀드 게이지 HUD
///
/// [이식이 진행되면] 자리 표시 큐브를 하나씩 진짜 스테이션으로 바꾼다 (PlaceholderStations 에서 빼고 변환 결과를 배치).
/// [다시 실행해도 안전] 에셋은 값만 갱신, 씬의 MissionTestLayout 은 지우고 새로 만든다.
///  전체 기획서 기준으로 만들었던 옛 테스트 에셋은 지운다 (새 기획서가 대체 — 명세 S8).
/// ⚠ 이 메뉴를 다시 실행하면 Pool 의 배정 개수와 발전기 제한 시간이 아래 값으로 되돌아간다.
/// </summary>
public static class SoloMissionTestSetup
{
    private const string RootFolder = "Assets/0. Member/SuHan/MissionTest";
    private const string DataFolder = RootFolder + "/Data";
    private const string PrefabFolder = RootFolder + "/Prefabs";
    private const string ScenePath = "Assets/0. Member/SuHan/SuhanTest.unity";

    // Assets/Prefabs/Player/Player.prefab
    private const string PlayerPrefabGuid = "36eaf6db234c47649a9aad19fe7fb7cd";

    // ProjectSettings/TagManager.asset 의 "Interactable" 레이어 = PlayerTargetDetector 가 감지하는 레이어
    private const int InteractableLayer = 6;

    private const string LayoutRootName = "MissionTestLayout";
    private const float HoldDuration = 2f;
    private const float InteractRange = 3.5f;

    // 발전기 제한 시간(초). 초기화를 빨리 보려면 TG001 에셋의 Time Limit Seconds 를 줄여서 테스트한다.
    private const float GeneratorTimeLimit = 120f;

    // 밸브 손잡이 중심 높이 (모델 피벗 = 손잡이 중심). 옛 게임 씬에서도 벽에 달려 있다.
    private const float ValveMountHeight = 1.2f;

    // 단체를 매 판 몇 개 뽑을지는 기획서에 없어 우선 전부 (명세 7절 열린 항목 1)
    private const int TeamCount = 5;
    private const int PersonalCount = 2;
    private const int ActionGoalCount = 1;

    private static readonly Color TeamColor = new Color(0.35f, 0.55f, 1f);
    private static readonly Color PersonalColor = new Color(0.35f, 0.85f, 0.45f);

    // ═════════════════════════════════════════════════════════════
    //  미션 정의 (새 기획서. ID 는 기획서에 없어 TG / PS / AG101~ 로 붙였다 — 명세 5절)
    // ═════════════════════════════════════════════════════════════

    private sealed class DefSpec
    {
        public string Id;
        public string Name;
        public MissionCategory Category;
        public ObjectiveKind Kind;
        public MissionEventType Trigger;
        public int Required = 1;
        public float TimeLimit;
        public TimeLimitMode TimeLimitMode = TimeLimitMode.SinceLastProgress;
    }

    private static readonly DefSpec[] Definitions =
    {
        new DefSpec { Id = "TG001", Name = "발전기 작동시키기", Category = MissionCategory.Team, Kind = ObjectiveKind.Count, Trigger = MissionEventType.GeneratorRepaired, Required = 3, TimeLimit = GeneratorTimeLimit },
        new DefSpec { Id = "TG002", Name = "코드 순서 맞추기", Category = MissionCategory.Team, Kind = ObjectiveKind.Count, Trigger = MissionEventType.SecurityCodeEntered },
        new DefSpec { Id = "TG003", Name = "고장난 장비 조립", Category = MissionCategory.Team, Kind = ObjectiveKind.Count, Trigger = MissionEventType.EquipmentAssembled },
        new DefSpec { Id = "TG004", Name = "생명 유지 장치 복구", Category = MissionCategory.Team, Kind = ObjectiveKind.Count, Trigger = MissionEventType.LifeSupportRestored },
        new DefSpec { Id = "TG005", Name = "대형 방화문 열기", Category = MissionCategory.Team, Kind = ObjectiveKind.Count, Trigger = MissionEventType.FireDoorOpened },

        new DefSpec { Id = "PS001", Name = "밸브 잠그기", Category = MissionCategory.Personal, Kind = ObjectiveKind.Count, Trigger = MissionEventType.ValveClosed },
        new DefSpec { Id = "PS002", Name = "차단기 올리기", Category = MissionCategory.Personal, Kind = ObjectiveKind.Count, Trigger = MissionEventType.BreakerRestored },
        new DefSpec { Id = "PS003", Name = "전선 연결하기", Category = MissionCategory.Personal, Kind = ObjectiveKind.Count, Trigger = MissionEventType.WiresConnected },
        new DefSpec { Id = "PS004", Name = "안테나 방향 맞추기", Category = MissionCategory.Personal, Kind = ObjectiveKind.Count, Trigger = MissionEventType.AntennaAligned },
        new DefSpec { Id = "PS005", Name = "압력 수치 맞추기", Category = MissionCategory.Personal, Kind = ObjectiveKind.Count, Trigger = MissionEventType.PressureStabilized },
        new DefSpec { Id = "PS006", Name = "필터 청소하기", Category = MissionCategory.Personal, Kind = ObjectiveKind.Count, Trigger = MissionEventType.FilterCleaned },

        new DefSpec { Id = "AG101", Name = "뛰지 않는다", Category = MissionCategory.ActionGoal, Kind = ObjectiveKind.Avoid, Trigger = MissionEventType.PlayerRan },
    };

    // ═════════════════════════════════════════════════════════════
    //  배치 (플레이어는 (0,0,0)에서 +Z 방향을 보고 시작)
    // ═════════════════════════════════════════════════════════════

    private static readonly Vector3[] GeneratorPositions =
    {
        new Vector3(-8f, 0f, 8f), new Vector3(0f, 0f, 8f), new Vector3(8f, 0f, 8f),
    };

    private sealed class PlaceholderSpec
    {
        public string PrefabName;
        public string Label;
        public MissionEventType Event;
        public CompletionPolicy Policy;
        public Color Color;
        public Vector3 Position;
    }

    private static readonly PlaceholderSpec[] PlaceholderStations =
    {
        Team("Placeholder_TG002_Code", "코드 순서 맞추기", "TG002", MissionEventType.SecurityCodeEntered, new Vector3(-9f, 0f, 16f)),
        Team("Placeholder_TG003_Assembly", "고장난 장비 조립", "TG003", MissionEventType.EquipmentAssembled, new Vector3(-3f, 0f, 16f)),
        Team("Placeholder_TG004_LifeSupport", "생명 유지 장치 복구", "TG004", MissionEventType.LifeSupportRestored, new Vector3(3f, 0f, 16f)),
        Team("Placeholder_TG005_FireDoor", "대형 방화문 열기", "TG005", MissionEventType.FireDoorOpened, new Vector3(9f, 0f, 16f)),

        Personal("Placeholder_PS003_Wiring", "전선 연결하기", "PS003", MissionEventType.WiresConnected, new Vector3(-2.5f, 0f, -8f)),
        Personal("Placeholder_PS005_Pressure", "압력 수치 맞추기", "PS005", MissionEventType.PressureStabilized, new Vector3(7.5f, 0f, -8f)),
        Personal("Placeholder_PS006_Filter", "필터 청소하기", "PS006", MissionEventType.FilterCleaned, new Vector3(12.5f, 0f, -8f)),
    };

    // 2a 에서 실제 미니게임으로 바뀐 개인 미션. 변환에 실패하면 이 자리 표시로 대체한다 (발전기와 같은 규칙).
    private static readonly PlaceholderSpec ValveFallback =
        Personal("Placeholder_PS001_Valve", "밸브 잠그기", "PS001", MissionEventType.ValveClosed, new Vector3(-12.5f, 0f, -8f));
    private static readonly PlaceholderSpec BreakerFallback =
        Personal("Placeholder_PS002_Breaker", "차단기 올리기", "PS002", MissionEventType.BreakerRestored, new Vector3(-7.5f, 0f, -8f));
    private static readonly PlaceholderSpec AntennaFallback =
        Personal("Placeholder_PS004_Antenna", "안테나 방향 맞추기", "PS004", MissionEventType.AntennaAligned, new Vector3(2.5f, 0f, -8f));

    private static PlaceholderSpec Team(string prefab, string title, string id, MissionEventType e, Vector3 position)
    {
        return new PlaceholderSpec
        {
            PrefabName = prefab, Label = $"{title} (자리 표시)\n{id}\n[F 2초]", Event = e,
            Policy = CompletionPolicy.Lock, Color = TeamColor, Position = position,
        };
    }

    private static PlaceholderSpec Personal(string prefab, string title, string id, MissionEventType e, Vector3 position)
    {
        return new PlaceholderSpec
        {
            PrefabName = prefab, Label = $"{title} (자리 표시)\n{id} · 완료 후 초기화\n[F 2초]", Event = e,
            Policy = CompletionPolicy.ResetForNext, Color = PersonalColor, Position = position,
        };
    }

    private const string InfoText =
        "가까운 앞줄: 발전기 3대 (버튼 클릭 → 3초, 1대 고치면 다음 1대까지 2분)\n" +
        "먼 앞줄: 단체 미션 자리 표시 (F 2초)  /  뒤: 개인 미션 — 밸브·차단기·안테나는 실제 미니게임, 나머지는 자리 표시\n" +
        "Shift 달리기 = AG101 '뛰지 않는다' 위반   F9 내 행동 확정 / F10 전체 공개";

    // ═════════════════════════════════════════════════════════════
    //  진입점
    // ═════════════════════════════════════════════════════════════

    [MenuItem("SuHan/Setup Solo Mission Test")]
    public static void Run()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            return;

        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        EnsureFolder(RootFolder, "Data");
        EnsureFolder(RootFolder, "Prefabs");

        foreach (string path in ObsoleteAssetPaths())
            AssetDatabase.DeleteAsset(path);

        MissionPool pool = CreateOrUpdatePool();
        NetworkObject missionManagerPrefab = CreateOrUpdateMissionManagerPrefab(pool);
        NetworkObject playerPrefab = LoadPlayerPrefab();

        List<(NetworkObject prefab, Vector3 position)> spawns = new List<(NetworkObject, Vector3)>();

        NetworkObject generatorPrefab = LegacyMissionConverter.ConvertGenerator();

        if (generatorPrefab == null)
        {
            Debug.LogWarning("[SoloMissionTestSetup] 발전기 변환에 실패해 자리 표시 큐브로 대체합니다 (위의 LegacyMissionConverter 오류 확인).");
            generatorPrefab = BuildPlaceholderPrefab(Team("Placeholder_TG001_Generator", "발전기", "TG001", MissionEventType.GeneratorRepaired, Vector3.zero));
        }

        foreach (Vector3 position in GeneratorPositions)
            spawns.Add((generatorPrefab, position));

        foreach (PlaceholderSpec spec in PlaceholderStations)
            spawns.Add((BuildPlaceholderPrefab(spec), spec.Position));

        // 밸브 모델은 피벗이 손잡이 중심이라 바닥(y=0)에 두면 절반이 묻힌다 → 벽에 달린 높이로 띄운다
        AddConvertedOrPlaceholder(spawns, LegacyMissionConverter.ConvertValve(), ValveFallback, "밸브", new Vector3(0f, ValveMountHeight, 0f));
        AddConvertedOrPlaceholder(spawns, LegacyMissionConverter.ConvertBreaker(), BreakerFallback, "차단기");
        AddConvertedOrPlaceholder(spawns, LegacyMissionConverter.ConvertAntenna(), AntennaFallback, "안테나");

        WireScene(scene, playerPrefab, missionManagerPrefab, spawns);

        AssetDatabase.SaveAssets();

        // 새 NetworkObject 프리팹을 Fusion 네트워크 프리팹 목록에 즉시 반영 (안 하면 Runner.Spawn 이 실패한다)
        Fusion.Editor.NetworkProjectConfigUtilities.RebuildPrefabTable();

        Debug.Log($"[SoloMissionTestSetup] 완료. 네트워크 오브젝트 {spawns.Count}개 배치. Play 를 눌러 테스트하세요.");
    }

    /// <summary>전체 기획서 기준으로 만들었던 옛 테스트 에셋 (새 기획서가 대체).</summary>
    private static IEnumerable<string> ObsoleteAssetPaths()
    {
        for (int i = 1; i <= 7; i++) yield return $"{DataFolder}/TM00{i}.asset";
        for (int i = 1; i <= 8; i++) yield return $"{DataFolder}/PM00{i}.asset";
        for (int i = 1; i <= 6; i++) yield return $"{DataFolder}/AG00{i}.asset";

        yield return $"{DataFolder}/TM_Door.asset";
        yield return $"{DataFolder}/PM_Door.asset";

        string[] oldPrefabs =
        {
            "Mission_Generator", "Mission_Power", "Mission_Security", "Mission_ServerReboot", "Mission_Cooling",
            "Mission_Antenna", "Mission_Exit", "Mission_ServerCheck", "Mission_Door",
            "Action_MedKit", "Action_ItemCraft", "Action_ItemGive", "Action_LieDetector", "Action_Corpse",
            "TestDoor_MissionInteractable",
            "Placeholder_PS001_Valve", "Placeholder_PS002_Breaker", "Placeholder_PS004_Antenna",
        };

        foreach (string prefab in oldPrefabs)
            yield return $"{PrefabFolder}/{prefab}.prefab";
    }

    // ═════════════════════════════════════════════════════════════
    //  데이터 에셋
    // ═════════════════════════════════════════════════════════════

    private static MissionPool CreateOrUpdatePool()
    {
        List<MissionDefinition> definitions = new List<MissionDefinition>();

        foreach (DefSpec spec in Definitions)
            definitions.Add(CreateOrUpdateDefinition(spec));

        string path = $"{DataFolder}/MissionPool_SoloTest.asset";
        MissionPool pool = AssetDatabase.LoadAssetAtPath<MissionPool>(path);
        bool isNew = pool == null;

        if (isNew)
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
        so.FindProperty("killerMissionsPerKiller").intValue = 0;   // 기획서에 살인마 미션 본문이 없음
        so.FindProperty("actionGoalsPerPlayer").intValue = ActionGoalCount;
        so.ApplyModifiedPropertiesWithoutUndo();

        if (isNew)
            AssetDatabase.CreateAsset(pool, path);
        else
            EditorUtility.SetDirty(pool);

        return pool;
    }

    private static MissionDefinition CreateOrUpdateDefinition(DefSpec spec)
    {
        string path = $"{DataFolder}/{spec.Id}.asset";
        MissionDefinition asset = AssetDatabase.LoadAssetAtPath<MissionDefinition>(path);
        bool isNew = asset == null;

        if (isNew)
            asset = ScriptableObject.CreateInstance<MissionDefinition>();

        SerializedObject so = new SerializedObject(asset);
        so.FindProperty("id").stringValue = spec.Id;
        so.FindProperty("displayName").stringValue = spec.Name;
        // 주의: MissionEventType 은 값이 연속적이지 않아서 enumValueIndex(선언 순서)를 쓰면 엉뚱한 값이 들어간다.
        // intValue(실제 저장되는 정수값)로 직접 써야 한다.
        so.FindProperty("category").intValue = (int)spec.Category;
        so.FindProperty("kind").intValue = (int)spec.Kind;
        so.FindProperty("trigger").intValue = (int)spec.Trigger;
        so.FindProperty("requiredCount").intValue = spec.Required;
        so.FindProperty("timeLimitSeconds").floatValue = spec.TimeLimit;
        so.FindProperty("timeLimitMode").intValue = (int)spec.TimeLimitMode;
        so.ApplyModifiedPropertiesWithoutUndo();

        if (isNew)
            AssetDatabase.CreateAsset(asset, path);
        else
            EditorUtility.SetDirty(asset);

        return asset;
    }

    // ═════════════════════════════════════════════════════════════
    //  프리팹
    // ═════════════════════════════════════════════════════════════

    private static NetworkObject CreateOrUpdateMissionManagerPrefab(MissionPool pool)
    {
        GameObject template = new GameObject("MissionManager");

        try
        {
            template.AddComponent<NetworkObject>();
            MissionManager manager = template.AddComponent<MissionManager>();

            SerializedObject so = new SerializedObject(manager);
            so.FindProperty("pool").objectReferenceValue = pool;
            // 방화문 = 탈출. 새 기획서의 "다른 단체 미션 모두 완료 후 시도" 규칙은 3단계에서 다시 정한다 (명세 7절 열린 항목 2)
            so.FindProperty("escapeMissionId").stringValue = "TG005";
            so.ApplyModifiedPropertiesWithoutUndo();

            return SaveAsPrefab(template, $"{PrefabFolder}/MissionManager.prefab");
        }
        finally
        {
            Object.DestroyImmediate(template);
        }
    }

    /// <summary>자리 표시 스테이션: 루트(NetworkObject + HoldStation + 이름표) 아래에 큐브. 루트 위치 = 바닥 중앙.</summary>
    /// <summary>
    /// 변환에 성공하면 그 프리팹을 fallback 위치 + convertedOffset 에, 실패하면 자리 표시 큐브를 fallback 위치에 배치한다.
    /// (offset 은 모델 피벗 보정용이라 바닥에 서는 자리 표시 큐브에는 쓰지 않는다)
    /// </summary>
    private static void AddConvertedOrPlaceholder(
        List<(NetworkObject prefab, Vector3 position)> spawns, NetworkObject converted, PlaceholderSpec fallback, string what,
        Vector3 convertedOffset = default)
    {
        if (converted == null)
        {
            Debug.LogWarning($"[SoloMissionTestSetup] {what} 변환에 실패해 자리 표시 큐브로 대체합니다 (위의 LegacyMissionConverter 오류 확인).");
            spawns.Add((BuildPlaceholderPrefab(fallback), fallback.Position));
            return;
        }

        spawns.Add((converted, fallback.Position + convertedOffset));
    }

    private static NetworkObject BuildPlaceholderPrefab(PlaceholderSpec spec)
    {
        GameObject root = new GameObject(spec.PrefabName);

        try
        {
            CreateBody(root, new Vector3(1.2f, 1.8f, 1.2f));
            SetLayerRecursively(root, InteractableLayer);

            root.AddComponent<NetworkObject>();
            HoldStation station = root.AddComponent<HoldStation>();

            SerializedObject so = new SerializedObject(station);
            so.FindProperty("completionEvent").intValue = (int)spec.Event;
            so.FindProperty("completionPolicy").intValue = (int)spec.Policy;
            so.FindProperty("holdDuration").floatValue = HoldDuration;
            so.FindProperty("interactRange").floatValue = InteractRange;

            Collider[] colliders = root.GetComponentsInChildren<Collider>(true);
            SerializedProperty colliderList = so.FindProperty("interactionColliders");
            colliderList.ClearArray();

            for (int i = 0; i < colliders.Length; i++)
            {
                colliderList.InsertArrayElementAtIndex(i);
                colliderList.GetArrayElementAtIndex(i).objectReferenceValue = colliders[i];
            }

            so.ApplyModifiedPropertiesWithoutUndo();

            // 조준 시 외곽선 (큐브 본체 기준). Player 의 PlayerMissionPromptController 가 켜고 끈다.
            MissionOutlineBuilder.Attach(root, root.transform.Find("Body").gameObject);

            AddLabel(root, spec.Label, spec.Color, tint: true);

            return SaveAsPrefab(root, $"{PrefabFolder}/{spec.PrefabName}.prefab");
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    /// <summary>바닥이 루트 위치에 오도록 자식 큐브를 만든다 (루트를 늘리면 이름표까지 늘어나므로 자식만 늘린다).</summary>
    private static void CreateBody(GameObject root, Vector3 size)
    {
        GameObject body = GameObject.CreatePrimitive(PrimitiveType.Cube);
        body.name = "Body";
        body.transform.SetParent(root.transform, false);
        body.transform.localPosition = new Vector3(0f, size.y * 0.5f, 0f);
        body.transform.localScale = size;
    }

    private static void AddLabel(GameObject target, string text, Color color, bool tint)
    {
        TestCubeLabel label = target.AddComponent<TestCubeLabel>();

        SerializedObject so = new SerializedObject(label);
        so.FindProperty("text").stringValue = text;
        so.FindProperty("color").colorValue = color;
        so.FindProperty("tintRenderers").boolValue = tint;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    private static NetworkObject SaveAsPrefab(GameObject template, string path)
    {
        GameObject saved = PrefabUtility.SaveAsPrefabAsset(template, path);
        return saved.GetComponent<NetworkObject>();
    }

    private static NetworkObject LoadPlayerPrefab()
    {
        string path = AssetDatabase.GUIDToAssetPath(PlayerPrefabGuid);
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);

        if (prefab == null)
        {
            Debug.LogError($"[SoloMissionTestSetup] Player 프리팹을 찾을 수 없습니다 (guid: {PlayerPrefabGuid}).");
            return null;
        }

        return prefab.GetComponent<NetworkObject>();
    }

    private static void SetLayerRecursively(GameObject root, int layer)
    {
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            t.gameObject.layer = layer;
    }

    // ═════════════════════════════════════════════════════════════
    //  씬 구성
    // ═════════════════════════════════════════════════════════════

    private static void WireScene(
        Scene scene, NetworkObject playerPrefab, NetworkObject missionManagerPrefab,
        List<(NetworkObject prefab, Vector3 position)> spawns)
    {
        RemovePlacedPlayerInstances(scene, playerPrefab);
        DestroyIfExists("TestDoorSpawnPoint");
        DestroyIfExists(LayoutRootName);

        Transform playerSpawnPoint = FindOrCreate("PlayerSpawnPoint", new Vector3(0f, 1f, 0f)).transform;

        // NetworkBehaviour 가 아닌 일반 MonoBehaviour 라 씬에 바로 둔다. MissionStation 이 자동으로 찾아 쓴다.
        if (Object.FindFirstObjectByType<PlayerHealthActorGate>() == null)
            new GameObject("PlayerHealthActorGate").AddComponent<PlayerHealthActorGate>();

        GameObject layoutRoot = new GameObject(LayoutRootName);
        Transform spawnRoot = new GameObject("SpawnPoints (Runner.Spawn 위치)").transform;
        spawnRoot.SetParent(layoutRoot.transform, false);

        List<Transform> points = new List<Transform>();

        for (int i = 0; i < spawns.Count; i++)
        {
            Transform point = new GameObject($"{spawns[i].prefab.name}_{i:00}").transform;
            point.SetParent(spawnRoot, false);
            point.position = spawns[i].position;
            // 모두 플레이어 시작 위치 쪽을 바라보게 (이름표/모델 정면)
            Vector3 toCenter = new Vector3(-point.position.x, 0f, -point.position.z);
            point.rotation = toCenter.sqrMagnitude > 0.01f ? Quaternion.LookRotation(toCenter) : Quaternion.identity;
            points.Add(point);
        }

        CreateInfoLabel(layoutRoot.transform, "Info (조작 안내)", new Vector3(0f, 0.8f, 4f), InfoText);
        CreateInfoLabel(layoutRoot.transform, "Info (발전기)", new Vector3(0f, 2.2f, 11f), $"발전기 ×3 · TG001 · 제한 {GeneratorTimeLimit:0}초 체인");
        CreateInfoLabel(layoutRoot.transform, "Info (밸브)", ValveFallback.Position + new Vector3(0f, 2.4f, 0f),
            "밸브 잠그기 · PS001\nF 유지 4~8초 (떼면 처음부터)");
        CreateInfoLabel(layoutRoot.transform, "Info (차단기)", BreakerFallback.Position + new Vector3(0f, 2.4f, 0f),
            "차단기 올리기 · PS002\n레버 클릭 → 전부 올리기");
        CreateInfoLabel(layoutRoot.transform, "Info (안테나)", AntennaFallback.Position + new Vector3(0f, 2.4f, 0f),
            "안테나 방향 맞추기 · PS004\nF 유지 → 고정 버튼 클릭");

        GameObject bootstrapObject = FindOrCreate("SoloTestBootstrap", Vector3.zero);
        SoloTestBootstrap bootstrap = GetOrAdd<SoloTestBootstrap>(bootstrapObject);
        GetOrAdd<MissionDebugOverlay>(bootstrapObject);
        GetOrAdd<TestRunReporter>(bootstrapObject);
        GetOrAdd<MissionHoldGaugeHUD>(bootstrapObject);

        SerializedObject so = new SerializedObject(bootstrap);
        so.FindProperty("playerPrefab").objectReferenceValue = playerPrefab;
        so.FindProperty("playerSpawnPoint").objectReferenceValue = playerSpawnPoint;
        so.FindProperty("missionManagerPrefab").objectReferenceValue = missionManagerPrefab;

        SerializedProperty entries = so.FindProperty("networkedSpawns");
        entries.ClearArray();

        for (int i = 0; i < spawns.Count; i++)
        {
            entries.InsertArrayElementAtIndex(i);
            SerializedProperty entry = entries.GetArrayElementAtIndex(i);
            entry.FindPropertyRelative("prefab").objectReferenceValue = spawns[i].prefab;
            entry.FindPropertyRelative("point").objectReferenceValue = points[i];
        }

        so.ApplyModifiedPropertiesWithoutUndo();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
    }

    private static void CreateInfoLabel(Transform parent, string name, Vector3 position, string text)
    {
        GameObject info = new GameObject(name);
        info.transform.SetParent(parent, false);
        info.transform.position = position;
        AddLabel(info, text, Color.white, tint: false);
    }

    /// <summary>
    /// 씬에 직접 놓인 Player(러너 없이 놓여서 Spawned 가 불리지 않던 것)를 지운다. 이름 또는 Player 프리팹 인스턴스로 찾는다.
    /// Player 는 SoloTestBootstrap 이 Runner.Spawn 으로 만든다.
    /// </summary>
    private static void RemovePlacedPlayerInstances(Scene scene, NetworkObject playerPrefab)
    {
        string playerPath = playerPrefab != null ? AssetDatabase.GetAssetPath(playerPrefab.gameObject) : null;

        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (IsPlacedPlayer(root, playerPath))
            {
                Debug.Log($"[SoloMissionTestSetup] 씬에 직접 놓인 Player 인스턴스 제거: {root.name}");
                Object.DestroyImmediate(root);
            }
        }

        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (IsPlacedPlayer(root, playerPath))
            {
                Debug.LogWarning($"[SoloMissionTestSetup] '{root.name}' 를 자동으로 지우지 못했습니다. 하이어라키에서 직접 삭제해 주세요.", root);
            }
        }
    }

    private static bool IsPlacedPlayer(GameObject root, string playerPath)
    {
        if (root.name == "Player")
            return true;

        return !string.IsNullOrEmpty(playerPath) &&
               PrefabUtility.IsAnyPrefabInstanceRoot(root) &&
               PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(root) == playerPath;
    }

    private static void EnsureFolder(string parent, string child)
    {
        if (!AssetDatabase.IsValidFolder($"{parent}/{child}"))
            AssetDatabase.CreateFolder(parent, child);
    }

    private static void DestroyIfExists(string name)
    {
        GameObject go = GameObject.Find(name);

        if (go != null)
            Object.DestroyImmediate(go);
    }

    private static GameObject FindOrCreate(string name, Vector3 position)
    {
        GameObject go = GameObject.Find(name);

        if (go == null)
        {
            go = new GameObject(name);
            go.transform.position = position;
        }

        return go;
    }

    private static T GetOrAdd<T>(GameObject go) where T : Component
    {
        T component = go.GetComponent<T>();
        return component != null ? component : go.AddComponent<T>();
    }
}
