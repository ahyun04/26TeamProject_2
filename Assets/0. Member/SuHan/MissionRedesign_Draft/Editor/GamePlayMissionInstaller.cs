using System.Collections.Generic;
using System.Linq;
using TrustNoOne.Missions;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>
/// [에디터 전용, 1클릭 도구] 메인 게임 씬(GamePlay)의 옛 미션을 지우고 새 미션을 놓는다 (6b 명세 3장).
///
/// [하는 일] 옛 개인 미션 6종을 단체로 옮기기(7a — 한 번) → 실제 게임 데이터 준비(없을 때만) · 풀 숫자 7a 값(옛 기본값일 때만) → 옛 미션 프리팹 제거 → 옛 MissionSystem 컴포넌트만 제거 →
///  `Missions (새 미션)` 아래에 MissionManager · PlayerHealthActorGate · 새 장치(없을 때만) → 옛 아이템 자리 후보 묶음 제거 →
///  팀원 스크립트의 미션 시스템 칸 연결 → 미션 목록 패널의 옛 MissionUI 를 MissionListUI 로 바꿔 끼우기(6c) → 1층 아이템 NavMesh 굽기(6b 보강 — 매번) → 새 5종 겹침 경고 →
///  미션 목록을 왼쪽 위 글자 칸으로 설정(10 — 공통 단계, 글자 칸이 없을 때만) → 저장.
/// [다시 실행해도 안전 — SW7] 이름으로 찾아 이미 있으면 건드리지 않는다 → 기획자가 옮긴 배치 · 바꾼 데이터 숫자가 남는다.
///  지운 것만 다시 만든다. 처음 배치로 되돌리려면 `Missions (새 미션)` 을 지우고 다시 실행한다.
/// [MissionSystem 오브젝트 — SW8] 옛 MissionSystem 과 같은 오브젝트에 RoleAssignment · GameTimer · GameEndSystem 이 붙어 있어
///  오브젝트째 지우지 않고 그 컴포넌트만 뗀다.
/// [팀원 타입을 이름으로 다루는 이유] 엔진 밖 컴파일 검사(VerifyAll)에 팀원 스크립트가 없다 → 팀원 타입(MissionSystem · RoleAssignment 등)을
///  코드에 직접 쓰지 않고 타입 이름 · 직렬화 칸 이름으로 찾는다.
/// [겹침 경고] 새 5종만 검사한다. 옛 미션 자리(벽에 붙은 밸브 · 차단기 등)는 원래 배치라 경고가 소음이 된다.
/// [9a 정리] 어느 맵에나 같은 단계(씬 열기 · 루트 · 매니저 · 게이트 · 배치 · NavMesh · 겹침 · 저장 · 테스트 표시)는 MissionMapInstaller 로 옮겼다.
///  여기는 GamePlay 설정 묶음(배치표 · NavMesh 범위)과 GamePlay 에서만 한 번 필요했던 단계(개인 → 단체 · 풀 숫자 · 옛 미션 · 미션 목록 UI)만 갖는다 — 동작 · 값은 그대로.
/// [근거] 6b 명세 SW1 · SW2 · SW5 · SW6 · SW7 · SW8 · SW11 · SW12 (사용자 결정 — 질문 1 ~ 6, 접근 1), 6b 보강 명세 NS2 · NS4 · NS12 · 7a 명세 TC6 · TC13 · 9a 명세 MP4 · 10 명세 ML12 · ML13
/// </summary>
public static class GamePlayMissionInstaller
{
    private const string ScenePath = "Assets/Scenes/GamePlay.unity";
    private const string OldMissionPrefabFolder = "Assets/Prefabs/Mission/";
    private const string DraftFolder = "Assets/0. Member/SuHan/MissionRedesign_Draft";
    private const string DataFolder = MissionMapInstaller.GameDataFolder;
    private const string PoolPath = MissionMapInstaller.GamePoolPath;

    // 한 판: 단체 7 (방화문 항상 + 나머지 10개 중 무작위 6) · 개인 0 · 살인마 0 · 행동 목표 0
    // (7a 명세 TC2 · TC3 — 사용자 결정, SW5 의 개인 2 · 단체 5 전부에서 바뀜)
    private const int TeamCount = 7;
    private const int PersonalCount = 0;

    // 6b 까지의 기본값 — 풀 숫자가 이 값 그대로일 때만 7a 값으로 바꾼다 (기획자가 바꾼 숫자는 보존 — 7a 명세 TC13)
    private const int OldTeamCount = 5;
    private const int OldPersonalCount = 2;

    // 7a: 옛 개인 미션 → 단체 미션 (같은 순서, 명세 TC6). 파일 이름을 바꿔 GUID 를 유지한다 → 풀 연결 · 기획자가 바꾼 값이 남는다
    private static readonly string[,] PersonalToTeam =
    {
        { "PS001", "TG006" }, { "PS002", "TG007" }, { "PS003", "TG008" },
        { "PS004", "TG009" }, { "PS005", "TG010" }, { "PS006", "TG011" },
    };

    private sealed class DefSpec
    {
        public string Id;
        public string Name;
        public MissionCategory Category;
        public MissionEventType Trigger;
        public int Required = 1;
        public float TimeLimit;
        public TimeLimitMode Mode = TimeLimitMode.SinceLastProgress;
        public bool AlwaysInclude;
    }

    // 실제 게임 미션 정의 (SW11): 값은 테스트에서 검증한 값. 테스트 설정 메뉴와 따로 둔다 — 기획 조정으로 달라질 수 있다
    private static readonly DefSpec[] Definitions =
    {
        new DefSpec { Id = "TG001", Name = "발전기 작동시키기", Category = MissionCategory.Team, Trigger = MissionEventType.GeneratorRepaired, Required = 3, TimeLimit = 120f },
        new DefSpec { Id = "TG002", Name = "코드 순서 맞추기", Category = MissionCategory.Team, Trigger = MissionEventType.SecurityCodeEntered },
        new DefSpec { Id = "TG003", Name = "고장난 장비 조립", Category = MissionCategory.Team, Trigger = MissionEventType.EquipmentAssembled, Required = 4 },
        new DefSpec { Id = "TG004", Name = "생명 유지 장치 복구", Category = MissionCategory.Team, Trigger = MissionEventType.LifeSupportRestored, Required = 3, TimeLimit = 120f, Mode = TimeLimitMode.SinceFirstProgress },
        new DefSpec { Id = "TG005", Name = "대형 방화문 열기", Category = MissionCategory.Team, Trigger = MissionEventType.FireDoorOpened, AlwaysInclude = true },
        // 7a: 옛 개인 미니게임 6종 → 단체 (제한 시간 없음 — 기획서 6종 설명에 없다, 명세 TC7 · 안테나는 2대 중 1대, TC8)
        new DefSpec { Id = "TG006", Name = "밸브 잠그기", Category = MissionCategory.Team, Trigger = MissionEventType.ValveClosed },
        new DefSpec { Id = "TG007", Name = "차단기 올리기", Category = MissionCategory.Team, Trigger = MissionEventType.BreakerRestored },
        new DefSpec { Id = "TG008", Name = "전선 연결하기", Category = MissionCategory.Team, Trigger = MissionEventType.WiresConnected },
        new DefSpec { Id = "TG009", Name = "안테나 방향 맞추기", Category = MissionCategory.Team, Trigger = MissionEventType.AntennaAligned },
        new DefSpec { Id = "TG010", Name = "압력 수치 맞추기", Category = MissionCategory.Team, Trigger = MissionEventType.PressureStabilized },
        new DefSpec { Id = "TG011", Name = "필터 청소하기", Category = MissionCategory.Team, Trigger = MissionEventType.FilterCleaned },
    };

    // 6b 명세 3장 배치표 — 옛 미션 자리(같은 위치 · 방향, 2026-10-11 씬에서 읽음)와 새 5종(시작 홀 빈 칸, 겹침 검사)
    private static readonly MissionPlacement[] Placements =
    {
        new MissionPlacement { Name = "Generator_A", PrefabPath = LegacyMissionConverter.GeneratorStationPath, Position = new Vector3(2.17f, 0f, 3.2f), Yaw = -90f },
        new MissionPlacement { Name = "Generator_B", PrefabPath = LegacyMissionConverter.GeneratorStationPath, Position = new Vector3(-7.6f, 0f, 3.2f), Yaw = 90f },
        new MissionPlacement { Name = "Generator_C", PrefabPath = LegacyMissionConverter.GeneratorStationPath, Position = new Vector3(-2.62f, 0f, 5.97f), Yaw = 180f },
        new MissionPlacement { Name = "Antenna_A", PrefabPath = LegacyMissionConverter.AntennaStationPath, Position = new Vector3(-2.23f, 0f, -2.16f) },
        new MissionPlacement { Name = "Antenna_B", PrefabPath = LegacyMissionConverter.AntennaStationPath, Position = new Vector3(-3.87f, 0f, -2.16f) },
        new MissionPlacement { Name = "Breaker", PrefabPath = LegacyMissionConverter.BreakerStationPath, Position = new Vector3(0f, 1.25f, -3.26f) },
        new MissionPlacement { Name = "Valve", PrefabPath = LegacyMissionConverter.ValveStationPath, Position = new Vector3(1.39f, 1.18f, -0.75f), Yaw = -90f },
        new MissionPlacement { Name = "Wiring", PrefabPath = LegacyMissionConverter.WiringStationPath, Position = new Vector3(-0.28f, 0f, 2.06f), Yaw = 90f },
        new MissionPlacement { Name = "Filter", PrefabPath = LegacyMissionConverter.FilterStationPath, Position = new Vector3(-0.24f, 0.03f, 0f) },
        new MissionPlacement { Name = "Vacuum", PrefabPath = LegacyMissionConverter.VacuumToolPath, Position = new Vector3(0f, 0.79f, 2.04f) },
        new MissionPlacement { Name = "Pressure", PrefabPath = LegacyMissionConverter.PressureStationPath, Position = new Vector3(-1f, 0f, -7f), FaceFocus = true, CheckOverlap = true },
        new MissionPlacement { Name = "Code", PrefabPath = LegacyMissionConverter.CodeStationPath, Position = new Vector3(-7f, 0f, 17f), FaceFocus = true, CheckOverlap = true },
        new MissionPlacement { Name = "LifeSupport", PrefabPath = LegacyMissionConverter.LifeSupportStationPath, Position = new Vector3(-1f, 0f, 18f), FaceFocus = true, CheckOverlap = true },
        // 7d: 생명 유지 장치 본체 3대 (명세 L3B1 · L3B12) — 지금 본체 근처 빈 바닥 (다른 장치에서 4m · 방화문 앞 피함, 씬 분석). 정확한 자리는 기획자
        new MissionPlacement { Name = "LifeSupport_B", PrefabPath = LegacyMissionConverter.LifeSupportStationPath, Position = new Vector3(-2f, 0f, 11f), FaceFocus = true, CheckOverlap = true },
        new MissionPlacement { Name = "LifeSupport_C", PrefabPath = LegacyMissionConverter.LifeSupportStationPath, Position = new Vector3(4f, 0f, 12f), FaceFocus = true, CheckOverlap = true },
        new MissionPlacement { Name = "Assembly", PrefabPath = LegacyMissionConverter.AssemblyStationPath, Position = new Vector3(5f, 0f, 18f), FaceFocus = true, CheckOverlap = true },
        // 방화문: 북쪽 벽(z 26.5) 앞에서 남쪽(시작 홀)을 보게 — 벽과 나란히 180° 고정
        new MissionPlacement { Name = "FireDoor", PrefabPath = LegacyMissionConverter.FireDoorStationPath, Position = new Vector3(-1f, 0f, 24.5f), Yaw = 180f, CheckOverlap = true },
    };

    // 1층 아이템 NavMesh (6b 보강 NS2 · NS3): 높이 지면 −0.5 ~ +2.0m, 가로 · 세로 = 본게임 건물만 (x −20 ~ 30, z −34 ~ 27).
    // 본게임 건물 = 가장 큰 건물(바닥 타일 189개 — 플레이어 시작 지점 · 새 장치가 있는 곳), 바닥 x −19.6 ~ 28.4 · z −33.5 ~ 26.5 (4m 타일 가장자리) + 여유.
    // 나머지 두 건물(x 57 ~ 77, x 111 ~ 127)은 게임 구역이 아니다 (테스트 후 추가 — 사용자 확인)
    private static readonly Bounds GamePlayNavMeshVolume = new Bounds(new Vector3(5f, 0.75f, -3.5f), new Vector3(50f, 2.5f, 61f));

    // 처음 범위(맵 바닥 전체 x −20 ~ 129 — 게임 구역이 아닌 두 건물까지 덮었다). 오브젝트 범위가 이 값 그대로면 새 범위로 바꾼다
    private static readonly Bounds FirstGamePlayNavMeshVolume = new Bounds(new Vector3(54.5f, 0.75f, -3.5f), new Vector3(149f, 2.5f, 61f));
    private const string GamePlayNavMeshPath = DataFolder + "/GamePlay_ItemNavMesh.asset";

    // GamePlay 설정 묶음 (9a MP4). 새 장치는 시작 지점(플레이어 생성기 근처 (0, 0, 0))을 바라보게 놓는다.
    //  배치표 · NavMesh 값보다 뒤에 둔다 — 정적 필드는 적힌 순서로 초기화된다
    private static readonly MissionMapSpec Spec = new MissionMapSpec
    {
        Label = "GamePlay",
        ScenePath = ScenePath,
        Placements = Placements,
        FaceFocus = Vector3.zero,
        NavMeshVolume = GamePlayNavMeshVolume,
        NavMeshAssetPath = GamePlayNavMeshPath,
        OutdatedNavMeshVolumes = new[] { FirstGamePlayNavMeshVolume },
    };

    // ═════════════════════════════════════════════════════════════
    //  메뉴
    // ═════════════════════════════════════════════════════════════

    [MenuItem("SuHan/Apply Missions To GamePlay")]
    public static void Apply()
    {
        if (!MissionMapInstaller.TryOpenScene(Spec, out Scene scene))
            return;

        // 7a: 옛 개인 데이터를 단체로 먼저 옮긴다 (EnsureGameData 가 새 TG 파일을 따로 만들지 않게)
        int movedToTeam = MigratePersonalToTeam();
        MissionPool pool = EnsureGameData();

        if (pool == null)
            return;

        bool poolUpdated = UpdatePoolForTeamOnly(pool);

        int removedOld = RemoveOldMissionInstances(scene);
        int removedSystems = RemoveOldMissionSystems(scene);

        Transform root = MissionMapInstaller.FindOrCreateRoot(scene);
        MissionManager manager = MissionMapInstaller.EnsureMissionManager(root, pool, out bool managerCreated);
        bool gateCreated = MissionMapInstaller.EnsureActorGate(root);
        List<GameObject> placedNow = MissionMapInstaller.PlaceMissing(Spec, root, out int skipped);

        int spotGroupsRemoved = RemoveOldItemSpots(root);
        int wired = WireTeamScripts(scene, manager);
        int listSwapped = SwapMissionListUI(scene, manager);
        // 장치를 다 놓은 뒤 굽는다 — 장치 몸체도 길을 막는 것으로 들어간다 (6b 보강 NS2 · NS4: 매번 다시 굽기)
        bool navMeshBaked = MissionMapInstaller.BakeNavMesh(Spec, root);
        int overlaps = MissionMapInstaller.CheckOverlaps(Spec, placedNow, root);
        // 미션 목록: 왼쪽 위 글자 칸 하나 (10 명세 ML12 — 바꿔 끼운 뒤, 글자 칸이 없는 목록만)
        int listsSetUp = MissionMapInstaller.SetupMissionList(scene);

        // 저장할 때 Fusion 이 씬 오브젝트의 정렬 키를 굽는다 (호스트 · 손님이 같은 오브젝트로 짝을 맞추는 번호)
        MissionMapInstaller.SaveScene(scene);

        Debug.Log($"[GamePlayMissionInstaller] 완료 — 개인 → 단체 {movedToTeam}개 옮김, 풀 숫자 {(poolUpdated ? $"단체 {TeamCount} · 개인 {PersonalCount} 으로 바꿈" : "그대로")}, 옛 미션 {removedOld}개 · 옛 MissionSystem 컴포넌트 {removedSystems}개 제거, " +
                  $"새 장치 {placedNow.Count}개 놓음 · {skipped}개 이미 있어 건너뜀, MissionManager {(managerCreated ? "새로 만듦" : "있음")}, " +
                  $"PlayerHealthActorGate {(gateCreated ? "새로 만듦" : "있음")}, 옛 자리 후보 묶음 {spotGroupsRemoved}개 제거, NavMesh {(navMeshBaked ? "구움" : "실패")}, " +
                  $"미션 시스템 칸 {wired}개 연결, 미션 목록 UI {listSwapped}개 교체 · {listsSetUp}개 설정, 겹침 경고 {overlaps}개. 멀티 테스트 전에는 SuHan/Build Full Game (Windows) 로 다시 빌드하세요.");
    }

    /// <summary>테스트 동안만 메인 씬에 디버그 패널(단체 · 개인 미션 표시, 호스트 F8 ~ F10)을 넣는다 (SW12 — 미션 목록 UI 는 6c). 커밋 전에 끄기.</summary>
    [MenuItem("SuHan/GamePlay 테스트 표시 켜기")]
    public static void AddTestOverlay()
    {
        MissionMapInstaller.AddTestOverlay(Spec);
    }

    [MenuItem("SuHan/GamePlay 테스트 표시 끄기")]
    public static void RemoveTestOverlay()
    {
        MissionMapInstaller.RemoveTestOverlay(Spec);
    }

    // ═════════════════════════════════════════════════════════════
    //  실제 게임 데이터 (SW11 — 없을 때만 만든다)
    // ═════════════════════════════════════════════════════════════

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

    /// <summary>
    /// 7a: 옛 개인 미션 데이터(PS001 ~ PS006)를 단체(TG006 ~ TG011)로 옮긴다 — PS 파일이 있고 TG 파일이 없을 때만 (명세 TC6 · TC13).
    /// 파일 이름을 바꿔 GUID 를 유지하므로 풀 연결과 기획자가 바꾼 값(필요 횟수 등)이 그대로 남는다. ID · 분류만 바꾼다.
    /// </summary>
    private static int MigratePersonalToTeam()
    {
        int moved = 0;

        for (int i = 0; i < PersonalToTeam.GetLength(0); i++)
        {
            string oldId = PersonalToTeam[i, 0];
            string newId = PersonalToTeam[i, 1];
            string oldPath = $"{DataFolder}/{oldId}.asset";
            string newPath = $"{DataFolder}/{newId}.asset";
            MissionDefinition old = AssetDatabase.LoadAssetAtPath<MissionDefinition>(oldPath);

            if (old == null || AssetDatabase.LoadAssetAtPath<MissionDefinition>(newPath) != null)
                continue;

            string error = AssetDatabase.RenameAsset(oldPath, newId);

            if (!string.IsNullOrEmpty(error))
            {
                Debug.LogError($"[GamePlayMissionInstaller] {oldId} → {newId} 이름 바꾸기 실패: {error}");
                continue;
            }

            SerializedObject so = new SerializedObject(old);
            so.FindProperty("id").stringValue = newId;
            so.FindProperty("category").intValue = (int)MissionCategory.Team;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(old);
            moved++;
        }

        if (moved > 0)
        {
            AssetDatabase.SaveAssets();
            Debug.Log($"[GamePlayMissionInstaller] 옛 개인 미션 {moved}개를 단체 미션(TG006 ~ TG011)으로 옮겼습니다 (7a).");
        }

        return moved;
    }

    /// <summary>
    /// 7a: 풀 숫자가 옛 기본값(단체 5 · 개인 2) 그대로면 단체 7 · 개인 0 으로 바꾸고 방화문(TG005)을 "매 판 항상 포함"으로 켠다 (명세 TC13).
    /// 기획자가 숫자를 바꿨다면 손대지 않는다.
    /// </summary>
    private static bool UpdatePoolForTeamOnly(MissionPool pool)
    {
        SerializedObject so = new SerializedObject(pool);
        SerializedProperty team = so.FindProperty("teamMissionCount");
        SerializedProperty personal = so.FindProperty("personalMissionsPerCitizen");

        if (team.intValue != OldTeamCount || personal.intValue != OldPersonalCount)
            return false;

        team.intValue = TeamCount;
        personal.intValue = PersonalCount;
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(pool);

        MissionDefinition escape = AssetDatabase.LoadAssetAtPath<MissionDefinition>($"{DataFolder}/{MissionMapInstaller.EscapeMissionId}.asset");

        if (escape != null)
        {
            SerializedObject escapeSO = new SerializedObject(escape);
            escapeSO.FindProperty("alwaysInclude").boolValue = true;
            escapeSO.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(escape);
        }

        AssetDatabase.SaveAssets();
        return true;
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
        so.FindProperty("alwaysInclude").boolValue = spec.AlwaysInclude;
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
    //  옛 아이템 자리 후보
    // ═════════════════════════════════════════════════════════════

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
    //  연결 (팀원 스크립트 · 미션 목록 UI)
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
    /// 미션 목록 패널의 옛 MissionUI 를 새 MissionListUI 로 바꿔 끼운다 (6c — 사용자 결정 A).
    /// 옛 줄 프리팹 · 단체 칸 · 개인 칸은 옮기지 않는다 — 새 목록은 글자 칸 하나에 쓰고, 그 칸은 공통 단계 SetupMissionList 가 만든다 (10 명세 ML13).
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

            MissionListUI list = panel.AddComponent<MissionListUI>();
            SerializedObject so = new SerializedObject(list);
            so.FindProperty("manager").objectReferenceValue = manager;
            so.ApplyModifiedPropertiesWithoutUndo();

            Object.DestroyImmediate(oldUI);
            swapped++;
            Debug.Log($"[GamePlayMissionInstaller] '{panel.name}' 의 옛 MissionUI 를 MissionListUI 로 바꿔 끼웠습니다 (글자 칸은 미션 목록 설정 단계가 만든다).");
        }

        return swapped;
    }

    // ═════════════════════════════════════════════════════════════
    //  도우미
    // ═════════════════════════════════════════════════════════════

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
