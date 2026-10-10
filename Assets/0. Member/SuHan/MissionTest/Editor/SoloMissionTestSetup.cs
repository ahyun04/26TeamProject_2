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
///  - 개인 PS001 밸브 / PS002 차단기 / PS003 전선 / PS004 안테나 / PS005 압력 / PS006 필터(+ 청소기 아이템): 진짜 스테이션 (2a~2d, 옛 프리팹 변환 · 압력은 원본 패널 · 피스톤 모델에서 조립, 실패 시 자리 표시)
///  - 단체 TG002 코드 순서 맞추기: 진짜 CodeStation (3a, 원본 자판 모델에서 조립, 실패 시 자리 표시)
///  - 단체 TG004 생명 유지 장치 복구: 진짜 LifeSupportStation (3b, 기본 도형 자리 표시) + 산소통 자리 후보 10곳 — 실행 중 본체가 6곳에 산소통을 놓는다. 실패 시 자리 표시
///  - 단체 TG003 고장난 장비 조립: 진짜 AssemblyStation (3c, 원본 본체 · 부품 모델에서 조립) + 부품 자리 후보 2묶음(주변 4 · 맵 8) — 실행 중 장비가 부품 4개를 놓는다. 실패 시 자리 표시
///  - 단체 TG005 대형 방화문 열기: 진짜 FireDoorStation (3d, 원본 문 · 패널 · 레버 모델에서 조립) + 레버 자리 후보 6곳 — 실행 중 방화문이 레버 2개를 놓는다. 실패 시 자리 표시
///  - (자리 표시 큐브 목록은 이제 비어 있다 — 단체 미션이 모두 진짜 장치가 됨)
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

    // 생명 유지 장치 제한 시간(초): 첫 공급 완료부터 (3b 명세 LS6). 실패를 빨리 보려면 TG004 에셋의 Time Limit Seconds 를 줄여서 테스트한다.
    private const float LifeSupportTimeLimit = 120f;

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
        new DefSpec { Id = "TG003", Name = "고장난 장비 조립", Category = MissionCategory.Team, Kind = ObjectiveKind.Count, Trigger = MissionEventType.EquipmentAssembled, Required = 4 },
        new DefSpec { Id = "TG004", Name = "생명 유지 장치 복구", Category = MissionCategory.Team, Kind = ObjectiveKind.Count, Trigger = MissionEventType.LifeSupportRestored, Required = 3, TimeLimit = LifeSupportTimeLimit, TimeLimitMode = TimeLimitMode.SinceFirstProgress },
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

    };

    // 2a 에서 실제 미니게임으로 바뀐 개인 미션. 변환에 실패하면 이 자리 표시로 대체한다 (발전기와 같은 규칙).
    private static readonly PlaceholderSpec ValveFallback =
        Personal("Placeholder_PS001_Valve", "밸브 잠그기", "PS001", MissionEventType.ValveClosed, new Vector3(-12.5f, 0f, -8f));
    private static readonly PlaceholderSpec BreakerFallback =
        Personal("Placeholder_PS002_Breaker", "차단기 올리기", "PS002", MissionEventType.BreakerRestored, new Vector3(-7.5f, 0f, -8f));
    private static readonly PlaceholderSpec AntennaFallback =
        Personal("Placeholder_PS004_Antenna", "안테나 방향 맞추기", "PS004", MissionEventType.AntennaAligned, new Vector3(2.5f, 0f, -8f));
    private static readonly PlaceholderSpec WiringFallback =
        Personal("Placeholder_PS003_Wiring", "전선 연결하기", "PS003", MissionEventType.WiresConnected, new Vector3(-2.5f, 0f, -8f));
    private static readonly PlaceholderSpec FilterFallback =
        Personal("Placeholder_PS006_Filter", "필터 청소하기", "PS006", MissionEventType.FilterCleaned, new Vector3(12.5f, 0f, -8f));
    private static readonly PlaceholderSpec PressureFallback =
        Personal("Placeholder_PS005_Pressure", "압력 수치 맞추기", "PS005", MissionEventType.PressureStabilized, new Vector3(7.5f, 0f, -8f));

    // 3a 에서 실제 미니게임으로 바뀐 단체 미션. 변환에 실패하면 이 자리 표시로 대체한다.
    private static readonly PlaceholderSpec CodeFallback =
        Team("Placeholder_TG002_Code", "코드 순서 맞추기", "TG002", MissionEventType.SecurityCodeEntered, new Vector3(-9f, 0f, 16f));

    // 3b 생명 유지 장치 본체: 옛 TG004 자리 표시 자리 (먼 앞줄). 변환에 실패하면 이 자리 표시로 대체한다.
    private static readonly PlaceholderSpec LifeSupportFallback =
        Team("Placeholder_TG004_LifeSupport", "생명 유지 장치 복구", "TG004", MissionEventType.LifeSupportRestored, new Vector3(3f, 0f, 16f));

    // 3b 산소통 자리 후보 10곳 (명세 5장): 바닥 곳곳, 다른 스테이션과 겹치지 않는다. 판마다 본체가 이 중 6곳을 고른다 (LS7)
    private static readonly Vector3[] OxygenTankSpotPositions =
    {
        new Vector3(-20f, 0f, 24f), new Vector3(20f, 0f, 24f), new Vector3(-20f, 0f, -16f), new Vector3(20f, 0f, -16f),
        new Vector3(-24f, 0f, 4f), new Vector3(24f, 0f, 4f), new Vector3(0f, 0f, 28f),
        new Vector3(-12f, 0f, -20f), new Vector3(12f, 0f, -20f), new Vector3(0f, 0f, -24f),
    };

    // 3c 장비 조립: TG003 자리 표시 자리 (먼 앞줄). 변환에 실패하면 이 자리 표시로 대체한다.
    private static readonly PlaceholderSpec AssemblyFallback =
        Team("Placeholder_TG003_Assembly", "고장난 장비 조립", "TG003", MissionEventType.EquipmentAssembled, new Vector3(-3f, 0f, 16f));

    // 3c 부품 자리 후보 (명세 5장, EA4): 주변 묶음 — 조립 위치 원 밖, 장비에서 4 ~ 5m, 다른 장치와 3.5m 이상
    private static readonly Vector3[] EquipmentNearSpotPositions =
    {
        new Vector3(-7f, 0f, 13f), new Vector3(1.5f, 0f, 12.5f), new Vector3(-6f, 0f, 19.5f), new Vector3(0f, 0f, 19.5f),
    };

    // 맵 묶음 — 산소통 자리와 10m 이상
    private static readonly Vector3[] EquipmentFarSpotPositions =
    {
        new Vector3(-30f, 0f, 12f), new Vector3(30f, 0f, 12f), new Vector3(-28f, 0f, -24f), new Vector3(28f, 0f, -24f),
        new Vector3(-10f, 0f, 32f), new Vector3(10f, 0f, 32f), new Vector3(-32f, 0f, -4f), new Vector3(32f, 0f, -4f),
    };

    // 3d 방화문: 옛 TG005 자리 (9, 16) 은 패널 포함 폭 7.6m 가 생명 유지 장치 (3, 16) 과 너무 가까워 (14, 16) 으로 (명세 FD16)
    private static readonly PlaceholderSpec FireDoorFallback =
        Team("Placeholder_TG005_FireDoor", "대형 방화문 열기", "TG005", MissionEventType.FireDoorOpened, new Vector3(14f, 0f, 16f));

    // 3d 레버 자리 후보 6곳 (명세 5장, FD6): 방화문 주변 바닥, 다른 장치 · 아이템 자리와 3m 이상
    private static readonly Vector3[] FireDoorLeverSpotPositions =
    {
        new Vector3(10f, 0f, 11f), new Vector3(18f, 0f, 11f), new Vector3(14f, 0f, 9.5f),
        new Vector3(21f, 0f, 15f), new Vector3(8f, 0f, 18.5f), new Vector3(18.5f, 0f, 20f),
    };

    // 필터 옆 바닥에 청소기 (필터 청소는 청소기를 들어야 한다 — 2c 명세 F1)
    private static readonly Vector3 VacuumToolPosition = new Vector3(10.5f, 0f, -6f);

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
        "먼 앞줄: 단체 미션 — 코드 · 장비 조립 · 생명 유지 장치 · 방화문 (모두 실제)  /  뒤: 개인 미션 6종 — 모두 실제 미니게임\n" +
        "방화문(먼 앞줄 오른쪽): 다른 단체 미션 완료 → 레버 2개 패널에 끼우기(우클릭) → 30초 안에 내리기(클릭)   F8 = 방화문 빼고 단체 미션 즉시 완료(테스트)\n" +
        "장비 조립(먼 앞줄 왼쪽): 부품 4개(1개는 장비 주변)를 찾아 들고 장비 앞 바닥 원 안으로 → 자동으로 붙음\n" +
        "생명 유지 장치(먼 앞줄): 산소통 6개 중 정상 3개만 본체에 — 가까이서 압력계 확인 → 들기(좌클릭) → 본체에 우클릭, 첫 공급부터 120초\n" +
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

        AddConvertedOrPlaceholder(spawns, LegacyMissionConverter.CreateCodeStation(), CodeFallback, "코드");

        AddConvertedOrPlaceholder(spawns, LegacyMissionConverter.CreateLifeSupportStation(), LifeSupportFallback, "생명 유지 장치");
        AddConvertedOrPlaceholder(spawns, LegacyMissionConverter.CreateAssemblyStation(), AssemblyFallback, "장비 조립");
        AddConvertedOrPlaceholder(spawns, LegacyMissionConverter.CreateFireDoorStation(), FireDoorFallback, "방화문");

        // 밸브 모델은 피벗이 손잡이 중심이라 바닥(y=0)에 두면 절반이 묻힌다 → 벽에 달린 높이로 띄운다
        AddConvertedOrPlaceholder(spawns, LegacyMissionConverter.ConvertValve(), ValveFallback, "밸브", new Vector3(0f, ValveMountHeight, 0f));
        AddConvertedOrPlaceholder(spawns, LegacyMissionConverter.ConvertBreaker(), BreakerFallback, "차단기");
        AddConvertedOrPlaceholder(spawns, LegacyMissionConverter.ConvertAntenna(), AntennaFallback, "안테나");
        AddConvertedOrPlaceholder(spawns, LegacyMissionConverter.ConvertWiring(), WiringFallback, "전선");
        AddConvertedOrPlaceholder(spawns, LegacyMissionConverter.ConvertFilter(), FilterFallback, "필터");
        AddConvertedOrPlaceholder(spawns, LegacyMissionConverter.CreatePressureStation(), PressureFallback, "압력");

        NetworkObject vacuumTool = LegacyMissionConverter.ConvertVacuumTool();

        if (vacuumTool != null)
            spawns.Add((vacuumTool, VacuumToolPosition));
        else
            Debug.LogWarning("[SoloMissionTestSetup] 청소기 변환에 실패해 청소기를 두지 않습니다 (위의 LegacyMissionConverter 오류 확인).");

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
            "Placeholder_PS001_Valve", "Placeholder_PS002_Breaker", "Placeholder_PS003_Wiring", "Placeholder_PS004_Antenna", "Placeholder_PS006_Filter", "Placeholder_PS005_Pressure",
            "Placeholder_TG002_Code", "Placeholder_TG004_OxygenValve",
        };

        foreach (string prefab in oldPrefabs)
            yield return $"{PrefabFolder}/{prefab}.prefab";

        // 3b 밸브 버전(기획 변경으로 폐기)의 프리팹 — 변환기 출력 폴더에 있다
        yield return "Assets/0. Member/SuHan/MissionRedesign_Draft/Prefabs/Missions/OxygenValve_Station.prefab";
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
        CreateInfoLabel(layoutRoot.transform, "Info (전선)", WiringFallback.Position + new Vector3(0f, 2.4f, 0f),
            "전선 연결하기 · PS003\n같은 색끼리 끌어서 연결 → 레버");
        CreateInfoLabel(layoutRoot.transform, "Info (필터)", FilterFallback.Position + new Vector3(0f, 2.4f, 0f),
            "필터 청소하기 · PS006\n청소기 줍기(좌클릭) → 먼지 조준 → 우클릭 · G 내려놓기");
        CreateInfoLabel(layoutRoot.transform, "Info (압력)", PressureFallback.Position + new Vector3(0f, 2.4f, 0f),
            "압력 수치 맞추기 · PS005\n버튼 클릭 → 초록 구간에서 멈추기 (연결은 무작위)");
        CreateInfoLabel(layoutRoot.transform, "Info (코드)", CodeFallback.Position + new Vector3(0f, 2.4f, 0f),
            "코드 순서 맞추기 · TG002\n빨강 → 파란불 순서 기억 → 숫자 입력 → 파랑");
        CreateInfoLabel(layoutRoot.transform, "Info (생명 유지 장치)", LifeSupportFallback.Position + new Vector3(0f, 2.4f, 0f),
            "생명 유지 장치 · TG004\n산소통 들기(좌클릭) → 가까이서 압력계 확인 → 본체에 우클릭");
        CreateInfoLabel(layoutRoot.transform, "Info (장비 조립)", AssemblyFallback.Position + new Vector3(0f, 3f, 0f),
            "고장난 장비 조립 · TG003\n부품 4개를 들고 바닥 원 안으로");
        CreateInfoLabel(layoutRoot.transform, "Info (방화문)", FireDoorFallback.Position + new Vector3(0f, 3.2f, 0f),
            "대형 방화문 열기 · TG005\n다른 단체 미션 완료 → 레버 2개 끼우기(우클릭) → 30초 안에 내리기(클릭)");

        // 아이템 자리 후보 (네트워크 오브젝트 아님 — 장치가 실행 중에 묶음 이름으로 찾는다)
        CreateItemSpots(layoutRoot.transform, LifeSupportStation.TankSpotGroup, "산소통 자리 후보", OxygenTankSpotPositions);
        CreateItemSpots(layoutRoot.transform, AssemblyStation.NearGroup, "장비 부품 — 장비 주변", EquipmentNearSpotPositions);
        CreateItemSpots(layoutRoot.transform, AssemblyStation.FarGroup, "장비 부품 — 맵 곳곳", EquipmentFarSpotPositions);
        CreateItemSpots(layoutRoot.transform, FireDoorStation.LeverSpotGroup, "방화문 레버", FireDoorLeverSpotPositions);

        GameObject bootstrapObject = FindOrCreate("SoloTestBootstrap", Vector3.zero);
        // 지운 테스트 스크립트(예: 임시 진단 컴포넌트)가 씬에 "스크립트 없음" 컴포넌트로 남지 않게 정리한다
        GameObjectUtility.RemoveMonoBehavioursWithMissingScript(bootstrapObject);
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

    /// <summary>아이템 자리 후보 묶음 (ItemSpots) — 네트워크 오브젝트가 아니다. 장치가 실행 중에 묶음 이름으로 찾는다 (3b LS7 · 3c EA4).</summary>
    private static void CreateItemSpots(Transform parent, string group, string label, Vector3[] positions)
    {
        GameObject spotsObject = new GameObject($"ItemSpots {group} ({label})");
        spotsObject.transform.SetParent(parent, false);

        ItemSpots spots = spotsObject.AddComponent<ItemSpots>();
        SerializedObject so = new SerializedObject(spots);
        so.FindProperty("group").stringValue = group;
        so.ApplyModifiedPropertiesWithoutUndo();

        for (int i = 0; i < positions.Length; i++)
        {
            Transform spot = new GameObject($"Spot_{i:00}").transform;
            spot.SetParent(spotsObject.transform, false);
            spot.position = positions[i];
        }
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
