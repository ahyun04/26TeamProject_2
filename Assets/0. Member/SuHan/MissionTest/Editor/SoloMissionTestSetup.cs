using System;
using System.Collections.Generic;
using Fusion;
using TrustNoOne.Missions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>
/// [에디터 전용, 1클릭 도구] SuhanTest 씬에 기획서(Trust No One 전체 기획서)의 미션을 전부 테스트할 수 있게 배치한다.
///
/// [배치하는 것]
///  ① 미션 오브젝트 (MissionInteractable, F 2초 홀드, 해당 미션이 없으면 상호작용 불가)
///     발전기×5, 전원, 보안 시스템, 서버 재부팅, 탈출구, 냉각, 안테나, 서버 점검, 문
///     - 발전기/안테나는 Assets/Prefabs/Mission 의 기존 프리팹에서 "모델만" 떼어 쓴다 (없거나 실패하면 큐브).
///  ② 행동 큐브 (TestActionCube, F 한 번, 미션과 무관하게 항상 가능)
///     의료 키트 사용, 아이템 제작, 아이템 전달, 거짓말 탐지기 사용, 시체 발견
///  ③ 바닥 구역 (MissionAreaTrigger, 밟으면 방문 처리): 의료실, 특정 지역
///  + 화면 디버그 패널(MissionDebugOverlay), 달리기 감지(TestRunReporter)
///
/// [왜 에디터 스크립트인가] NetworkObject 프리팹은 실행 중인 에디터 안에서 Unity API 로 만들어야
///  Fusion 의 컴포넌트 베이킹을 정상적으로 탄다. .prefab/.unity YAML 을 손으로 쓰면 깨질 위험이 크다.
///
/// [다시 실행해도 안전] 에셋은 값만 갱신하고, 씬의 MissionTestLayout 은 지우고 새로 만든다.
/// </summary>
public static class SoloMissionTestSetup
{
    private const string RootFolder = "Assets/0. Member/SuHan/MissionTest";
    private const string DataFolder = RootFolder + "/Data";
    private const string PrefabFolder = RootFolder + "/Prefabs";
    private const string ScenePath = "Assets/0. Member/SuHan/SuhanTest.unity";

    // Assets/Prefabs/Player/Player.prefab
    private const string PlayerPrefabGuid = "36eaf6db234c47649a9aad19fe7fb7cd";

    // 기획서 미션과 확실히 대응되는 기존 프리팹만 사용한다 (전선/차단기/밸브는 어떤 미션인지 애매해서 큐브로 둔다)
    private const string LegacyGeneratorPrefab = "Assets/Prefabs/Mission/Generator_Prefab.prefab";
    private const string LegacyAntennaPrefab = "Assets/Prefabs/Mission/Antenna_Prefab.prefab";

    // ProjectSettings/TagManager.asset 의 "Interactable" 레이어 = PlayerTargetDetector 가 감지하는 레이어
    private const int InteractableLayer = 6;

    private const string LayoutRootName = "MissionTestLayout";
    private const float HoldDuration = 2f;
    private const float InteractRange = 3.5f;

    private static readonly Color TeamColor = new Color(0.35f, 0.55f, 1f);
    private static readonly Color PersonalColor = new Color(0.35f, 0.85f, 0.45f);
    private static readonly Color ActionColor = new Color(1f, 0.65f, 0.25f);
    private static readonly Color AreaColor = new Color(0.7f, 0.45f, 1f);

    // ═════════════════════════════════════════════════════════════
    //  기획서 미션 정의
    //  - ID/이름은 기획서 그대로. 행동 목표는 기획서에 ID 가 없어 AG001~ 로 붙였다.
    //  - 필요 횟수는 기획서에 나온 것(발전기 5대, PM001 2회)만 반영하고 나머지는 1회 (에셋에서 수정 가능).
    //  - "특정 플레이어와 3분 이상 함께 이동"은 2인 이상이 필요한 Custom 목표라 제외.
    // ═════════════════════════════════════════════════════════════

    private sealed class DefSpec
    {
        public string Id;
        public string Name;
        public MissionCategory Category;
        public ObjectiveKind Kind;
        public MissionEventType Trigger;
        public int Required;

        public DefSpec(string id, string name, MissionCategory category, ObjectiveKind kind, MissionEventType trigger, int required = 1)
        {
            Id = id;
            Name = name;
            Category = category;
            Kind = kind;
            Trigger = trigger;
            Required = required;
        }
    }

    private static readonly DefSpec[] Definitions =
    {
        new DefSpec("TM001", "발전기 수리", MissionCategory.Team, ObjectiveKind.Count, MissionEventType.GeneratorRepaired, 5),
        new DefSpec("TM002", "전원 연결", MissionCategory.Team, ObjectiveKind.Count, MissionEventType.PowerConnected),
        new DefSpec("TM003", "보안 시스템 복구", MissionCategory.Team, ObjectiveKind.Count, MissionEventType.SecuritySystemRestored),
        new DefSpec("TM004", "서버 재부팅", MissionCategory.Team, ObjectiveKind.Count, MissionEventType.ServerRebooted),
        new DefSpec("TM005", "탈출구 개방", MissionCategory.Team, ObjectiveKind.Count, MissionEventType.ExitOpened),
        new DefSpec("TM006", "냉각 시스템 복구", MissionCategory.Team, ObjectiveKind.Count, MissionEventType.CoolingRestored),
        new DefSpec("TM007", "통신 안테나 복구", MissionCategory.Team, ObjectiveKind.Count, MissionEventType.AntennaRestored),

        new DefSpec("PM001", "발전기 수리 2회", MissionCategory.Personal, ObjectiveKind.Count, MissionEventType.GeneratorRepaired, 2),
        new DefSpec("PM002", "의료 키트 사용", MissionCategory.Personal, ObjectiveKind.Count, MissionEventType.MedKitUsed),
        new DefSpec("PM003", "아이템 제작", MissionCategory.Personal, ObjectiveKind.Count, MissionEventType.ItemCrafted),
        new DefSpec("PM004", "의료실 방문", MissionCategory.Personal, ObjectiveKind.Count, MissionEventType.MedicalRoomVisited),
        new DefSpec("PM005", "아이템 전달", MissionCategory.Personal, ObjectiveKind.Count, MissionEventType.ItemGiven),
        new DefSpec("PM006", "전원 연결", MissionCategory.Personal, ObjectiveKind.Count, MissionEventType.PowerConnected),
        new DefSpec("PM007", "서버 점검", MissionCategory.Personal, ObjectiveKind.Count, MissionEventType.ServerChecked),
        new DefSpec("PM008", "문 개방", MissionCategory.Personal, ObjectiveKind.Count, MissionEventType.DoorOpened),

        new DefSpec("AG001", "특정 지역 방문", MissionCategory.ActionGoal, ObjectiveKind.Count, MissionEventType.AreaVisited),
        new DefSpec("AG002", "시체 최초 발견", MissionCategory.ActionGoal, ObjectiveKind.Count, MissionEventType.CorpseFirstFound),
        new DefSpec("AG003", "한 번도 달리지 않기", MissionCategory.ActionGoal, ObjectiveKind.Avoid, MissionEventType.PlayerRan),
        new DefSpec("AG004", "회복 아이템 끝까지 보관", MissionCategory.ActionGoal, ObjectiveKind.Avoid, MissionEventType.MedKitUsed),
        new DefSpec("AG005", "거짓말 탐지기 사용하지 않기", MissionCategory.ActionGoal, ObjectiveKind.Avoid, MissionEventType.LieDetectorUsed),
        new DefSpec("AG006", "누구에게도 아이템을 주지 않기", MissionCategory.ActionGoal, ObjectiveKind.Avoid, MissionEventType.ItemGiven),
    };

    // 기본값: 기획서 규칙대로 랜덤 배정 (단체 4 / 개인 2 / 행동 목표 1).
    //  - 단체 4: 기획서 예시 "Pool 7개 중 이번 게임에서 4개 사용"
    //  - 개인 2 / 행동 목표 1: 기존 MissionSystem 의 기본값과 같은 수
    // 모든 미션을 한 판에 다 테스트하려면 MissionPool_SoloTest 에셋에서 7 / 8 / 4 로 바꾸면 된다
    //  (행동 목표가 4인 이유: 개인 미션을 전부 받으면 AG004·AG006 은 충돌 검사에서 빠지고 4개가 남는다).
    // ⚠ 이 메뉴를 다시 실행하면 에셋의 개수가 아래 값으로 되돌아간다.
    private const int TeamCount = 4;
    private const int PersonalCount = 2;
    private const int ActionGoalCount = 1;

    // ═════════════════════════════════════════════════════════════
    //  배치 정의 (플레이어는 (0,0,0)에서 +Z 방향을 보고 시작)
    // ═════════════════════════════════════════════════════════════

    private sealed class ObjectSpec
    {
        public string PrefabName;
        public string Label;
        public MissionEventType Event;
        public Color Color;
        public string LegacyPrefab;
        public Vector3[] Positions;
    }

    private static readonly ObjectSpec[] MissionObjects =
    {
        new ObjectSpec
        {
            PrefabName = "Mission_Generator", Label = "발전기\nTM001 · PM001\n[F 2초]", Event = MissionEventType.GeneratorRepaired,
            Color = TeamColor, LegacyPrefab = LegacyGeneratorPrefab,
            Positions = new[] { new Vector3(-10f, 0f, 8f), new Vector3(-5f, 0f, 8f), new Vector3(0f, 0f, 8f), new Vector3(5f, 0f, 8f), new Vector3(10f, 0f, 8f) },
        },
        new ObjectSpec
        {
            PrefabName = "Mission_Power", Label = "전원 연결\nTM002 · PM006\n[F 2초]", Event = MissionEventType.PowerConnected,
            Color = TeamColor, Positions = new[] { new Vector3(-15f, 0f, 16f) },
        },
        new ObjectSpec
        {
            PrefabName = "Mission_Security", Label = "보안 시스템 복구\nTM003\n[F 2초]", Event = MissionEventType.SecuritySystemRestored,
            Color = TeamColor, Positions = new[] { new Vector3(-9f, 0f, 16f) },
        },
        new ObjectSpec
        {
            PrefabName = "Mission_ServerReboot", Label = "서버 재부팅\nTM004\n[F 2초]", Event = MissionEventType.ServerRebooted,
            Color = TeamColor, Positions = new[] { new Vector3(-3f, 0f, 16f) },
        },
        new ObjectSpec
        {
            PrefabName = "Mission_Cooling", Label = "냉각 시스템 복구\nTM006\n[F 2초]", Event = MissionEventType.CoolingRestored,
            Color = TeamColor, Positions = new[] { new Vector3(3f, 0f, 16f) },
        },
        new ObjectSpec
        {
            PrefabName = "Mission_Antenna", Label = "통신 안테나 복구\nTM007\n[F 2초]", Event = MissionEventType.AntennaRestored,
            Color = TeamColor, LegacyPrefab = LegacyAntennaPrefab, Positions = new[] { new Vector3(9f, 0f, 16f) },
        },
        new ObjectSpec
        {
            PrefabName = "Mission_Exit", Label = "탈출구 개방\nTM005 (완료 시 탈출 해금)\n[F 2초]", Event = MissionEventType.ExitOpened,
            Color = TeamColor, Positions = new[] { new Vector3(15f, 0f, 16f) },
        },
        new ObjectSpec
        {
            PrefabName = "Mission_ServerCheck", Label = "서버 점검\nPM007\n[F 2초]", Event = MissionEventType.ServerChecked,
            Color = PersonalColor, Positions = new[] { new Vector3(-4f, 0f, 24f) },
        },
        new ObjectSpec
        {
            PrefabName = "Mission_Door", Label = "문 개방\nPM008\n[F 2초]", Event = MissionEventType.DoorOpened,
            Color = PersonalColor, Positions = new[] { new Vector3(4f, 0f, 24f) },
        },
    };

    private static readonly ObjectSpec[] ActionCubes =
    {
        new ObjectSpec
        {
            PrefabName = "Action_MedKit", Label = "의료 키트 사용\nPM002 달성 / AG004 위반\n[F]", Event = MissionEventType.MedKitUsed,
            Color = ActionColor, Positions = new[] { new Vector3(-10f, 0f, -8f) },
        },
        new ObjectSpec
        {
            PrefabName = "Action_ItemCraft", Label = "아이템 제작\nPM003\n[F]", Event = MissionEventType.ItemCrafted,
            Color = ActionColor, Positions = new[] { new Vector3(-5f, 0f, -8f) },
        },
        new ObjectSpec
        {
            PrefabName = "Action_ItemGive", Label = "아이템 전달\nPM005 달성 / AG006 위반\n[F]", Event = MissionEventType.ItemGiven,
            Color = ActionColor, Positions = new[] { new Vector3(0f, 0f, -8f) },
        },
        new ObjectSpec
        {
            PrefabName = "Action_LieDetector", Label = "거짓말 탐지기 사용\nAG005 위반\n[F]", Event = MissionEventType.LieDetectorUsed,
            Color = ActionColor, Positions = new[] { new Vector3(5f, 0f, -8f) },
        },
        new ObjectSpec
        {
            PrefabName = "Action_Corpse", Label = "시체 발견\nAG002\n[F]", Event = MissionEventType.CorpseFirstFound,
            Color = ActionColor, Positions = new[] { new Vector3(10f, 0f, -8f) },
        },
    };

    private sealed class AreaSpec
    {
        public string Name;
        public string Label;
        public MissionEventType Event;
        public Vector3 Position;
        public Vector3 Size;
    }

    private static readonly AreaSpec[] Areas =
    {
        new AreaSpec
        {
            Name = "Area_MedicalRoom", Label = "의료실 (밟기)\nPM004", Event = MissionEventType.MedicalRoomVisited,
            Position = new Vector3(-12f, 0f, -18f), Size = new Vector3(6f, 4f, 6f),
        },
        new AreaSpec
        {
            Name = "Area_Special", Label = "특정 지역 (밟기)\nAG001", Event = MissionEventType.AreaVisited,
            Position = new Vector3(12f, 0f, -18f), Size = new Vector3(6f, 4f, 6f),
        },
    };

    private const string InfoText =
        "Shift 달리기 = AG003 '한 번도 달리지 않기' 위반\n" +
        "앞: 미션 오브젝트(F 2초)  /  뒤: 행동 큐브(F)  /  뒤쪽 바닥: 방문 구역\n" +
        "F9 내 행동 확정(탈출·사망 흉내)  /  F10 전체 공개";

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
        DeleteObsoleteAssets();

        MissionPool pool = CreateOrUpdatePool();
        NetworkObject missionManagerPrefab = CreateOrUpdateMissionManagerPrefab(pool);
        NetworkObject playerPrefab = LoadPlayerPrefab();

        List<(NetworkObject prefab, Vector3 position)> spawns = new List<(NetworkObject, Vector3)>();
        List<string> legacyUsed = new List<string>();

        foreach (ObjectSpec spec in MissionObjects)
        {
            NetworkObject prefab = BuildMissionObjectPrefab(spec, out bool usedLegacy);

            if (usedLegacy)
                legacyUsed.Add(spec.PrefabName);

            foreach (Vector3 position in spec.Positions)
                spawns.Add((prefab, position));
        }

        foreach (ObjectSpec spec in ActionCubes)
        {
            NetworkObject prefab = BuildActionCubePrefab(spec);

            foreach (Vector3 position in spec.Positions)
                spawns.Add((prefab, position));
        }

        WireScene(scene, playerPrefab, missionManagerPrefab, spawns);

        AssetDatabase.SaveAssets();

        // 새로 만든 NetworkObject 프리팹을 Fusion 네트워크 프리팹 목록에 즉시 반영한다.
        // 안 하면 목록이 늦게 갱신되어 Runner.Spawn 이 "guid failed to be translated into a prefab id" 로 실패한다.
        // (메뉴 Tools/Fusion/Rebuild Prefab Table 과 같은 동작)
        Fusion.Editor.NetworkProjectConfigUtilities.RebuildPrefabTable();

        Debug.Log($"[SoloMissionTestSetup] 완료. 네트워크 오브젝트 {spawns.Count}개, 방문 구역 {Areas.Length}개 배치. " +
                  $"기존 프리팹 모델 사용: {(legacyUsed.Count > 0 ? string.Join(", ", legacyUsed) : "없음")}. Play 를 눌러 테스트하세요.");
    }

    // ═════════════════════════════════════════════════════════════
    //  데이터 에셋
    // ═════════════════════════════════════════════════════════════

    private static void DeleteObsoleteAssets()
    {
        // 이전 버전(문 1개 테스트)에서 만든 것들 — 새 구성으로 대체
        AssetDatabase.DeleteAsset($"{DataFolder}/TM_Door.asset");
        AssetDatabase.DeleteAsset($"{DataFolder}/PM_Door.asset");
        AssetDatabase.DeleteAsset($"{PrefabFolder}/TestDoor_MissionInteractable.prefab");
    }

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
            so.FindProperty("escapeMissionId").stringValue = "TM005";
            so.ApplyModifiedPropertiesWithoutUndo();

            return SaveAsPrefab(template, $"{PrefabFolder}/MissionManager.prefab");
        }
        finally
        {
            Object.DestroyImmediate(template);
        }
    }

    /// <summary>
    /// 미션 오브젝트 프리팹: 루트(NetworkObject + MissionInteractable + 이름표) 아래에 외형을 자식으로 둔다.
    /// 루트의 위치 = 바닥 중앙이라서, 배치 좌표의 y 를 0 으로 통일할 수 있다.
    /// </summary>
    private static NetworkObject BuildMissionObjectPrefab(ObjectSpec spec, out bool usedLegacy)
    {
        GameObject root = new GameObject(spec.PrefabName);

        try
        {
            GameObject visual = string.IsNullOrEmpty(spec.LegacyPrefab) ? null : TryExtractLegacyVisual(spec.LegacyPrefab);
            usedLegacy = visual != null;

            if (usedLegacy)
            {
                visual.transform.SetParent(root.transform, false);
                visual.transform.localPosition = Vector3.zero;
                EnsureCollider(root, visual);
            }
            else
            {
                CreateBody(root, new Vector3(1.2f, 1.8f, 1.2f), keepCollider: true);
            }

            SetLayerRecursively(root, InteractableLayer);

            root.AddComponent<NetworkObject>();
            MissionInteractable interactable = root.AddComponent<MissionInteractable>();

            SerializedObject so = new SerializedObject(interactable);
            so.FindProperty("completionEvent").intValue = (int)spec.Event;
            so.FindProperty("mode").intValue = (int)InteractionCompletionMode.HoldTimer;
            so.FindProperty("holdDuration").floatValue = HoldDuration;
            so.FindProperty("interactRange").floatValue = InteractRange;
            so.FindProperty("singleUse").boolValue = true;

            Collider[] colliders = root.GetComponentsInChildren<Collider>(true);
            SerializedProperty colliderList = so.FindProperty("interactionColliders");
            colliderList.ClearArray();

            for (int i = 0; i < colliders.Length; i++)
            {
                colliderList.InsertArrayElementAtIndex(i);
                colliderList.GetArrayElementAtIndex(i).objectReferenceValue = colliders[i];
            }

            so.ApplyModifiedPropertiesWithoutUndo();

            // 기존 프리팹 모델은 원래 색을 살리고, 큐브만 칠한다.
            AddLabel(root, spec.Label, spec.Color, tint: !usedLegacy);

            return SaveAsPrefab(root, $"{PrefabFolder}/{spec.PrefabName}.prefab");
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    private static NetworkObject BuildActionCubePrefab(ObjectSpec spec)
    {
        GameObject root = new GameObject(spec.PrefabName);

        try
        {
            CreateBody(root, new Vector3(0.9f, 0.9f, 0.9f), keepCollider: true);
            SetLayerRecursively(root, InteractableLayer);

            root.AddComponent<NetworkObject>();
            TestActionCube action = root.AddComponent<TestActionCube>();

            SerializedObject so = new SerializedObject(action);
            so.FindProperty("actionEvent").intValue = (int)spec.Event;
            so.ApplyModifiedPropertiesWithoutUndo();

            AddLabel(root, spec.Label, spec.Color, tint: true);

            return SaveAsPrefab(root, $"{PrefabFolder}/{spec.PrefabName}.prefab");
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    /// <summary>바닥이 루트 위치에 오도록 자식 큐브를 만든다 (루트를 늘리면 이름표까지 늘어나므로 자식만 늘린다).</summary>
    private static GameObject CreateBody(GameObject root, Vector3 size, bool keepCollider)
    {
        GameObject body = GameObject.CreatePrimitive(PrimitiveType.Cube);
        body.name = "Body";
        body.transform.SetParent(root.transform, false);
        body.transform.localPosition = new Vector3(0f, size.y * 0.5f, 0f);
        body.transform.localScale = size;

        if (!keepCollider)
            Object.DestroyImmediate(body.GetComponent<BoxCollider>());

        return body;
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

    // ═════════════════════════════════════════════════════════════
    //  기존 프리팹에서 "모델만" 떼어내기
    //  기존 프리팹은 옛 미션 시스템 스크립트(GeneratorMission, AntennaMission, MissionButton …),
    //  NetworkObject, 월드 UI 캔버스를 달고 있다. 옛 MissionSystem 에 연결된 로직이라 새 MissionManager 와
    //  동작하지 않으므로, 메시·콜라이더 같은 외형 요소만 남기고 전부 지운다. 원본 프리팹은 건드리지 않는다(복사본만 수정).
    // ═════════════════════════════════════════════════════════════

    private static readonly Type[] VisualComponentTypes =
    {
        typeof(Transform), typeof(MeshFilter), typeof(Renderer), typeof(Collider),
        typeof(LODGroup), typeof(ParticleSystem), typeof(Light),
    };

    private static GameObject TryExtractLegacyVisual(string path)
    {
        GameObject legacy = AssetDatabase.LoadAssetAtPath<GameObject>(path);

        if (legacy == null)
        {
            Debug.LogWarning($"[SoloMissionTestSetup] 기존 프리팹 없음, 큐브로 대체: {path}");
            return null;
        }

        GameObject copy = Object.Instantiate(legacy);
        copy.name = $"Visual ({legacy.name})";

        try
        {
            // 월드 UI(프롬프트 텍스트 등)는 옛 스크립트가 켜고 끄던 것이라 통째로 제거
            foreach (Canvas canvas in copy.GetComponentsInChildren<Canvas>(true))
            {
                if (canvas != null && canvas.gameObject != copy)
                    Object.DestroyImmediate(canvas.gameObject);
            }

            StripToVisual(copy);

            // 옛 스크립트나 NetworkObject 가 하나라도 남으면 새 프리팹 안에 NetworkObject 가 중첩되어 Fusion 이 오작동한다.
            if (copy.GetComponentsInChildren<MonoBehaviour>(true).Length > 0 ||
                copy.GetComponentsInChildren<NetworkObject>(true).Length > 0)
            {
                Debug.LogWarning($"[SoloMissionTestSetup] 기존 스크립트를 완전히 제거하지 못해 큐브로 대체: {path}");
                Object.DestroyImmediate(copy);
                return null;
            }

            if (copy.GetComponentsInChildren<Renderer>(true).Length == 0)
            {
                Debug.LogWarning($"[SoloMissionTestSetup] 모델(Renderer)이 없어 큐브로 대체: {path}");
                Object.DestroyImmediate(copy);
                return null;
            }

            copy.transform.position = Vector3.zero;
            return copy;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[SoloMissionTestSetup] 기존 프리팹 모델 추출 실패, 큐브로 대체: {path}\n{e}");
            Object.DestroyImmediate(copy);
            return null;
        }
    }

    /// <summary>
    /// 외형 컴포넌트만 남기고 나머지를 지운다.
    /// RequireComponent 로 묶인 것은 의존하는 쪽을 먼저 지워야 하므로 여러 번에 나눠 지운다.
    /// </summary>
    private static void StripToVisual(GameObject root)
    {
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            GameObjectUtility.RemoveMonoBehavioursWithMissingScript(t.gameObject);

        for (int pass = 0; pass < 10; pass++)
        {
            bool removedAny = false;

            foreach (Component component in root.GetComponentsInChildren<Component>(true))
            {
                if (component == null || IsVisualComponent(component) || IsRequiredByOthers(component))
                    continue;

                Object.DestroyImmediate(component);

                // 엔진이 삭제를 거부하면 살아 있다 — 그 경우는 "진행"으로 치지 않아야 무한 반복하지 않는다.
                if (component == null)
                    removedAny = true;
            }

            if (!removedAny)
                return;
        }
    }

    private static bool IsVisualComponent(Component component)
    {
        foreach (Type type in VisualComponentTypes)
        {
            if (type.IsInstanceOfType(component))
                return true;
        }

        return false;
    }

    private static bool IsRequiredByOthers(Component target)
    {
        foreach (Component other in target.GetComponents<Component>())
        {
            if (other == null || other == target)
                continue;

            foreach (object attribute in other.GetType().GetCustomAttributes(typeof(RequireComponent), true))
            {
                RequireComponent require = (RequireComponent)attribute;

                if (IsOfType(require.m_Type0, target) || IsOfType(require.m_Type1, target) || IsOfType(require.m_Type2, target))
                    return true;
            }
        }

        return false;
    }

    private static bool IsOfType(Type type, Component component)
    {
        return type != null && type.IsInstanceOfType(component);
    }

    /// <summary>
    /// 모델 전체를 덮는 콜라이더가 없으면 BoxCollider 를 루트에 붙인다.
    /// [이유] 기존 발전기 프리팹은 본체에 콜라이더가 없고 옛 스크립트용 작은 버튼(14cm) 콜라이더만 있어서,
    ///  그 버튼을 정확히 조준해야만 감지됐다(사실상 상호작용 불가). 콜라이더가 "있는지"가 아니라
    ///  "모델을 충분히 덮는지"를 봐야 한다. 기존 콜라이더는 그대로 둔다(겹쳐도 문제 없음).
    /// </summary>
    private static void EnsureCollider(GameObject root, GameObject visual)
    {
        Physics.SyncTransforms();

        Bounds modelBounds = Encapsulate(visual.GetComponentsInChildren<Renderer>(true), r => r.bounds);
        Collider[] colliders = visual.GetComponentsInChildren<Collider>(true);

        if (colliders.Length > 0)
        {
            Bounds colliderBounds = Encapsulate(colliders, c => c.bounds);

            if (Volume(colliderBounds) >= Volume(modelBounds) * 0.5f)
                return;
        }

        // 빌드 중 루트는 원점·회전 없음·크기 1 이라 월드 좌표 = 루트 로컬 좌표
        BoxCollider box = root.AddComponent<BoxCollider>();
        box.center = modelBounds.center;
        box.size = modelBounds.size;
    }

    private static Bounds Encapsulate<T>(T[] items, Func<T, Bounds> getBounds)
    {
        Bounds bounds = getBounds(items[0]);

        for (int i = 1; i < items.Length; i++)
            bounds.Encapsulate(getBounds(items[i]));

        return bounds;
    }

    private static float Volume(Bounds bounds)
    {
        return bounds.size.x * bounds.size.y * bounds.size.z;
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
        // 씬에 직접 놓여 있던 Player(러너 없이 놓여서 Spawned 가 불리지 않던 것)와 이전 버전 배치를 정리
        RemovePlacedPlayerInstances(scene, playerPrefab);
        DestroyIfExists("TestDoorSpawnPoint");
        DestroyIfExists(LayoutRootName);

        Transform playerSpawnPoint = FindOrCreate("PlayerSpawnPoint", new Vector3(0f, 1f, 0f)).transform;

        // NetworkBehaviour 가 아닌 일반 MonoBehaviour 라 씬에 바로 둔다. MissionInteractable 이 자동으로 찾아 쓴다.
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

        foreach (AreaSpec spec in Areas)
            CreateArea(layoutRoot.transform, spec);

        GameObject info = new GameObject("Info (조작 안내)");
        info.transform.SetParent(layoutRoot.transform, false);
        info.transform.position = new Vector3(0f, 0.8f, 4f);
        AddLabel(info, InfoText, Color.white, tint: false);

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

    /// <summary>
    /// 이름("Player")으로 찾는 방식은 지난 실행에서 인스턴스를 지우지 못했다.
    /// 그래서 "Player 프리팹에서 나온 씬 루트 오브젝트"를 직접 찾아 지우고, 남아 있으면 경고한다.
    /// 씬에 놓인 Player 는 Runner 가 스폰한 것이 아니라 움직이지 않고, 카메라·오디오 리스너가 겹쳐서 화면을 가릴 수 있다.
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
                Debug.LogWarning($"[SoloMissionTestSetup] '{root.name}' 를 자동으로 지우지 못했습니다. 하이어라키에서 직접 삭제해 주세요 " +
                                 "(Player 는 SoloTestBootstrap 이 Runner.Spawn 으로 만듭니다).", root);
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

    private static void CreateArea(Transform parent, AreaSpec spec)
    {
        GameObject area = new GameObject(spec.Name);
        area.transform.SetParent(parent, false);
        area.transform.position = spec.Position;

        // 바닥 표시용 얇은 판. 밟고 지나가야 하므로 콜라이더는 없앤다.
        CreateBody(area, new Vector3(spec.Size.x, 0.05f, spec.Size.z), keepCollider: false);

        MissionAreaTrigger trigger = area.AddComponent<MissionAreaTrigger>();
        SerializedObject so = new SerializedObject(trigger);
        so.FindProperty("visitEvent").intValue = (int)spec.Event;
        so.FindProperty("size").vector3Value = spec.Size;
        so.ApplyModifiedPropertiesWithoutUndo();

        AddLabel(area, spec.Label, AreaColor, tint: true);
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
