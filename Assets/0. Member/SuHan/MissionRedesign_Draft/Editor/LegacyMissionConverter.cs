using System.Collections.Generic;
using Fusion;
using TMPro;
using TrustNoOne.Missions;
using UnityEditor;
using UnityEngine;

/// <summary>
/// [에디터 전용] 옛 미션 프리팹(Assets/Prefabs/Mission)을 새 구조(MissionStation)로 바꾼 "복사본"을 만든다.
///  원본 프리팹은 수정하지 않는다 (stage1 명세 S1: 옛 코드·프리팹은 교체 단계까지 그대로).
///
/// [방식] 모델·월드 UI·NetworkObject 는 그대로 두고 스크립트만 교체한다. 옛 값은 SerializedObject 로 읽어 옮긴다.
///  옛 타입은 "이름(문자열)"으로 찾는다 → 교체 단계에서 옛 코드를 지워도 이 파일은 그대로 컴파일된다.
/// [공통 틀 — Convert] 원본 로드 → 복사본 생성 → 미니게임별 Build → 검증 2가지 → 저장 → 복사본 파괴.
///  검증: ① 옛 스크립트 · Missing Script 가 남지 않았는가 ② 모든 콜라이더를 조준하면 입력을 받는 대상이 잡히는가.
///  하나라도 실패하면 저장하지 않는다.
///  공통 후처리: 조준 문구(MissionPrompt.promptObject)가 있으면 PromptBillboard 를 붙여 항상 보는 사람 쪽을 향하게 한다
///  (옛 밸브 문구가 방향 고정이라 배치에 따라 뒤집혀 보였음 — 2b 테스트에서 발견).
/// [조준 외곽선] MissionOutlineBuilder 로 붙인다 (1단계 사용자 요청). 부품마다 MissionPrompt 를 두면 조준한 부품만 켜진다 (2a 명세 P7).
/// [변환 목록] 1단계 발전기 / 2a 밸브 · 안테나 · 차단기 / 2b 전선 / 2c 필터 · 청소기(아이템) / 2d 압력(신규 — 옛 프리팹이 없어 원본 패널 · 피스톤 모델에서 조립) / 3a 코드 자판(신규 — 원본 자판 모델에서 조립) / 3b 생명 유지 장치 · 산소통(신규 — 모델이 없어 기본 도형으로 조립) / 3c 장비 조립(신규 — 원본 본체 · 부품 모델에서 조립). 미니게임을 이식할 때마다 Build… 와 Convert… 를 하나씩 추가한다.
/// </summary>
public static class LegacyMissionConverter
{
    private const string OutputFolder = "Assets/0. Member/SuHan/MissionRedesign_Draft/Prefabs/Missions";
    private const string LegacyFolder = "Assets/Prefabs/Mission";

    public const string GeneratorStationPath = OutputFolder + "/Generator_Station.prefab";
    public const string ValveStationPath = OutputFolder + "/Valve_Station.prefab";
    public const string AntennaStationPath = OutputFolder + "/Antenna_Station.prefab";
    public const string BreakerStationPath = OutputFolder + "/Breaker_Station.prefab";
    public const string WiringStationPath = OutputFolder + "/Wiring_Station.prefab";
    public const string FilterStationPath = OutputFolder + "/Filter_Station.prefab";

    private const string ItemPrefabFolder = "Assets/0. Member/SuHan/MissionRedesign_Draft/Prefabs/Items";
    private const string ItemDataFolder = "Assets/0. Member/SuHan/MissionRedesign_Draft/Items";
    private const string LegacyVacuumDataPath = "Assets/ScriptableObjects/Data/Item/VacuumData.asset";

    public const string VacuumToolPath = ItemPrefabFolder + "/Vacuum_Tool.prefab";
    public const string VacuumToolFirstPersonPath = ItemPrefabFolder + "/Vacuum_Tool_FP.prefab";
    public const string VacuumToolDataPath = ItemDataFolder + "/VacuumToolData.asset";

    // 2d 압력: 옛 프리팹이 없어 원본 모델(패널 프리팹 · 피스톤 FBX)에서 조립한다 (원본은 복사해서 쓰고 수정하지 않음)
    public const string PressureStationPath = OutputFolder + "/Pressure_Station.prefab";
    // 원본 모델 · 재질 폴더 (읽기 전용 — 복사해서 쓰고 원본은 수정하지 않는다)
    private const string SourceModelFolder = "Assets/0. Member/Taewoo/Model";
    private const string PressurePanelPath = SourceModelFolder + "/Pressure_device_panel.prefab";
    private const string SourceMaterialFolder = "Assets/0. Member/Taewoo/Material";

    // 3a 코드: 옛 프리팹이 없어 원본 자판 모델(Match_code.fbx)에서 조립한다 (원본은 복사해서 쓰고 수정하지 않음)
    public const string CodeStationPath = OutputFolder + "/Code_Station.prefab";
    private const string CodeModelPath = SourceModelFolder + "/Match_code.fbx";
    private const float CodeInteractRange = 3f;

    // 3a C13 화면: 패널 메시에서 화면 면을 찾는 아틀라스 칸 (TryFindCodeScreen 참고)과 글꼴
    private static readonly Vector2 CodeScreenAtlasUv = new Vector2(0.3865f, 0.5745f);
    private const float CodeScreenUvTolerance = 0.004f;
    private const string TmpDefaultFontPath = "Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset";

    // 3b 생명 유지 장치 · 산소통: 모델이 없어 기본 도형으로 조립한다 (명세 LS9 — 모델이 생기면 Build 함수만 바꾼다)
    public const string LifeSupportStationPath = OutputFolder + "/LifeSupport_Station.prefab";
    public const string OxygenTankPath = ItemPrefabFolder + "/OxygenTank_Item.prefab";
    private const string OxygenTankFirstPersonPath = ItemPrefabFolder + "/OxygenTank_FP.prefab";
    private const string OxygenTankDataPath = ItemDataFolder + "/OxygenTankData.asset";
    private const string DraftMaterialFolder = "Assets/0. Member/SuHan/MissionRedesign_Draft/Materials";
    private const float LifeSupportInteractRange = 3f;

    // 산소통 아이템 번호: 기존 아이템 데이터(10 청소기 · 11 · 12 · 13)와 겹치지 않게
    private const int OxygenTankItemId = 20;

    // 크기 (m): 산소통 지름 0.25 · 높이 0.6, 본체 1 × 1.6 × 0.6, 화면 0.8 × 0.35 (명세 4장)
    private const float OxygenTankDiameter = 0.25f;
    private const float OxygenTankHeight = 0.6f;
    private static readonly Vector3 LifeSupportBodySize = new Vector3(1f, 1.6f, 0.6f);
    private static readonly Vector2 LifeSupportScreenSize = new Vector2(0.8f, 0.35f);
    private const float LifeSupportScreenHeight = 1.25f;
    private const float LifeSupportShelfHeight = 0.35f;

    // 자리 표시 색
    private static readonly Color OxygenTankColor = new Color(0.75f, 0.12f, 0.1f);
    private static readonly Color LampOffColor = new Color(0.35f, 0.35f, 0.35f);
    private static readonly Color LifeSupportBodyColor = new Color(0.22f, 0.24f, 0.27f);
    private static readonly Color SocketColor = new Color(0.08f, 0.08f, 0.09f);

    // 3c 장비 조립: 원본 본체 · 부품 모델에서 조립한다 (원본은 복사해서 쓰고 수정하지 않음)
    public const string AssemblyStationPath = OutputFolder + "/Assembly_Station.prefab";
    private const string AssemblyMachineModelPath = SourceModelFolder + "/Equipment_repair_machine_main.fbx";
    private const float AssemblyZoneRadius = 2.5f;      // 조립 위치 원 반경 (3c EA1)
    private const float AssemblyZoneGap = 2.5f;         // 장비 앞면에서 원 중심까지 (m)
    private const float AssemblyFlyStartHeight = 1.2f;  // 날아오기 시작 높이 (EA12)

    // 부품 아이템 번호: 21 파랑 · 22 회색 · 23 빨강 · 24 노랑 (기존 10 ~ 13 · 20 과 겹치지 않게, EA10)
    private const int EquipmentPartFirstItemId = 21;

    // 색 순서 = AssemblyRules 색 번호. 모델 · 자리 노드 이름 / 프리팹 이름 / 로그 · 아이템 이름
    private static readonly string[] EquipmentPartColors = { "blue", "gray", "red", "yellow" };
    private static readonly string[] EquipmentPartNames = { "Blue", "Gray", "Red", "Yellow" };
    private static readonly string[] EquipmentPartKoreanNames = { "파랑", "회색", "빨강", "노랑" };
    private static readonly Color AssemblyZoneColor = new Color(0.1f, 0.55f, 0.55f);

    // 조준 감지 레이어 (ProjectSettings 의 Interactable). 원본 모델은 기본 레이어(0)로 들어와 있어 버튼을 이 레이어로 옮겨야 조준된다
    private const int InteractableLayer = 6;

    private static readonly string[] PressureLetters = { "A", "B", "C" };

    // 모델 폴더의 테스트 씬에 놓인 배치를 패널 기준 상대 위치로 옮긴 값 (패널을 마주 보면 왼쪽부터 A · B · C)
    private static readonly Vector3[] PistonOffsets =
    {
        new Vector3(3.486f, 0f, -6.761f),
        new Vector3(-0.034f, 0f, -6.731f),
        new Vector3(-3.324f, 0f, -6.621f),
    };

    // 개인 미니게임 공통 거리 (2a 명세 3장). 발전기는 1단계 값 3.5 유지.
    private const float PersonalInteractRange = 3f;

    private static readonly string[] LegacyTypeNames =
    {
        "MissionMiniGameBase", "GeneratorMission", "MissionButton",
        "ValveMission", "AntennaMission", "AntennaLockButton", "BreakerMission", "BreakerLever",
        "WiringMission", "WireStartPoint", "WireEndPoint", "WireConnectionVisual", "WiringLever",
        "FilterCleaningMission", "VacuumDust", "VacuumItem", "FirstPersonVacuumView",
    };

    [MenuItem("SuHan/Convert Legacy Mission Prefabs")]
    public static void ConvertAllFromMenu()
    {
        Report("발전기", ConvertGenerator());
        Report("밸브", ConvertValve());
        Report("안테나", ConvertAntenna());
        Report("차단기", ConvertBreaker());
        Report("전선", ConvertWiring());
        Report("필터", ConvertFilter());
        Report("청소기", ConvertVacuumTool());
        Report("압력", CreatePressureStation());
        Report("코드", CreateCodeStation());
        Report("생명 유지 장치", CreateLifeSupportStation());
        Report("장비 조립", CreateAssemblyStation());
        AssetDatabase.SaveAssets();

        // 새 NetworkObject 프리팹을 Fusion 네트워크 프리팹 목록에 즉시 반영 (안 하면 Runner.Spawn 이 실패한다)
        Fusion.Editor.NetworkProjectConfigUtilities.RebuildPrefabTable();
    }

    private static void Report(string what, NetworkObject result)
    {
        if (result == null)
            Debug.LogError($"[LegacyMissionConverter] {what} 변환 실패 (위 로그 참고)");
    }

    public static NetworkObject ConvertGenerator() =>
        Convert("발전기", LegacyFolder + "/Generator_Prefab.prefab", "Generator_Station", GeneratorStationPath, BuildGenerator);

    public static NetworkObject ConvertValve() =>
        Convert("밸브", LegacyFolder + "/Valve_Prefab.prefab", "Valve_Station", ValveStationPath, BuildValve);

    public static NetworkObject ConvertAntenna() =>
        Convert("안테나", LegacyFolder + "/Antenna_Prefab.prefab", "Antenna_Station", AntennaStationPath, BuildAntenna);

    public static NetworkObject ConvertBreaker() =>
        Convert("차단기", LegacyFolder + "/CircuitBreaker_Prefab.prefab", "Breaker_Station", BreakerStationPath, BuildBreaker);

    public static NetworkObject ConvertWiring() =>
        Convert("전선", LegacyFolder + "/Wiring/Wiring_Prefab.prefab", "Wiring_Station", WiringStationPath, BuildWiring);

    public static NetworkObject ConvertFilter() =>
        Convert("필터", LegacyFolder + "/FilterCleaning_Prefab.prefab", "Filter_Station", FilterStationPath, BuildFilter);

    /// <summary>
    /// 청소기: 1인칭 모델 → 아이템 데이터(복사본) → 아이템 프리팹 순서로 만든다 (앞의 결과를 뒤에서 참조).
    /// 아이템은 스테이션이 아니라 조준 검증을 하지 않는다.
    /// </summary>
    public static NetworkObject ConvertVacuumTool()
    {
        GameObject firstPerson = ConvertPrefab("청소기 1인칭", LegacyFolder + "/Vacuum_FP_Prefab.prefab", "Vacuum_Tool_FP",
            VacuumToolFirstPersonPath, BuildVacuumFirstPerson, false);

        if (firstPerson == null)
            return null;

        Object itemData = CreateVacuumToolData(firstPerson);

        if (itemData == null)
            return null;

        return Convert("청소기", LegacyFolder + "/Vacuum_Prefab.prefab", "Vacuum_Tool", VacuumToolPath,
            copy => BuildVacuumTool(copy, itemData), false);
    }

    /// <summary>압력: 원본 패널 프리팹(Pressure_device_panel)을 복사해 조립한다 (명세 PR9). 공통 틀의 저장 · 검증 · 빌보드를 그대로 쓴다.</summary>
    public static NetworkObject CreatePressureStation() =>
        Convert("압력", PressurePanelPath, "Pressure_Station", PressureStationPath, BuildPressure);

    /// <summary>코드: 원본 자판 모델(Match_code.fbx)을 복사해 조립한다 (3a 명세 4장). 공통 틀의 저장 · 검증 · 빌보드를 그대로 쓴다.</summary>
    public static NetworkObject CreateCodeStation() =>
        Convert("코드", CodeModelPath, "Code_Station", CodeStationPath, BuildCode);

    /// <summary>
    /// 생명 유지 장치 본체 (3b 명세 4장): 산소통 아이템을 먼저 만들고, 빈 루트에서 본체를 조립한다.
    /// 모델이 없어 기본 도형 자리 표시다 (LS9).
    /// </summary>
    public static NetworkObject CreateLifeSupportStation()
    {
        NetworkObject tankPrefab = CreateOxygenTank();

        if (tankPrefab == null)
        {
            Debug.LogError("[LegacyMissionConverter] 생명 유지 장치: 산소통을 만들지 못해 본체를 조립하지 않습니다.");
            return null;
        }

        return Assemble("생명 유지 장치", "LifeSupport_Station", LifeSupportStationPath, copy => BuildLifeSupport(copy, tankPrefab));
    }

    /// <summary>산소통 아이템 (3b 명세 4장): 1인칭 모델 → 아이템 데이터 → 월드 아이템 순서로 만든다 (청소기와 같은 순서).</summary>
    public static NetworkObject CreateOxygenTank()
    {
        GameObject firstPerson = AssemblePrefab("산소통 1인칭", "OxygenTank_FP", OxygenTankFirstPersonPath, BuildOxygenTankFirstPerson, false);

        if (firstPerson == null)
            return null;

        ItemData data = CreateOxygenTankData(firstPerson);

        if (data == null)
            return null;

        return Assemble("산소통", "OxygenTank_Item", OxygenTankPath, copy => BuildOxygenTank(copy, data), false);
    }

    /// <summary>
    /// 장비 (3c 명세 4장): 부품 4종을 먼저 만들고, 원본 본체 모델을 복사해 자리 · 조립 위치 · 연출을 조립한다.
    /// 장비에는 조준 입력이 없어 조준 검증은 끈다 (EA14).
    /// </summary>
    public static NetworkObject CreateAssemblyStation()
    {
        NetworkObject[] partPrefabs = new NetworkObject[AssemblyRules.PartCount];

        for (int c = 0; c < partPrefabs.Length; c++)
        {
            partPrefabs[c] = CreateEquipmentPart(c);

            if (partPrefabs[c] == null)
            {
                Debug.LogError($"[LegacyMissionConverter] 장비: {EquipmentPartKoreanNames[c]} 부품을 만들지 못해 장비를 조립하지 않습니다.");
                return null;
            }
        }

        return Convert("장비", AssemblyMachineModelPath, "Assembly_Station", AssemblyStationPath, copy => BuildAssembly(copy, partPrefabs), false);
    }

    /// <summary>장비 부품 아이템 한 색 (3c 명세 4장): 1인칭 모델 → 아이템 데이터 → 월드 아이템 (산소통과 같은 순서).</summary>
    public static NetworkObject CreateEquipmentPart(int color)
    {
        string label = EquipmentPartNames[color];
        string koreanName = EquipmentPartKoreanNames[color];

        GameObject firstPerson = AssemblePrefab($"부품 1인칭 ({koreanName})", $"EquipmentPart_{label}_FP",
            $"{ItemPrefabFolder}/EquipmentPart_{label}_FP.prefab", copy => BuildEquipmentPartFirstPerson(copy, color), false);

        if (firstPerson == null)
            return null;

        ItemData data = CreateItemData($"{ItemDataFolder}/EquipmentPart_{label}Data.asset", EquipmentPartFirstItemId + color,
            $"장비 부품 ({koreanName})", firstPerson);

        if (data == null)
            return null;

        return Assemble($"부품 ({koreanName})", $"EquipmentPart_{label}", $"{ItemPrefabFolder}/EquipmentPart_{label}.prefab",
            copy => BuildEquipmentPart(copy, color, data), false);
    }

    // ═════════════════════════════════════════════════════════════
    //  공통 틀
    // ═════════════════════════════════════════════════════════════

    /// <param name="build">복사본을 새 구조로 바꾼다. 실패하면 오류를 남기고 false.</param>
    /// <param name="verifyTargets">조준 검증을 할지. 스테이션이 아닌 것(아이템 · 1인칭 모델)은 false.</param>
    private static NetworkObject Convert(string what, string legacyPath, string outputName, string outputPath,
        System.Func<GameObject, bool> build, bool verifyTargets = true)
    {
        GameObject saved = ConvertPrefab(what, legacyPath, outputName, outputPath, build, verifyTargets);
        return saved != null ? saved.GetComponent<NetworkObject>() : null;
    }

    /// <summary>Convert 의 본체. NetworkObject 가 없는 프리팹(1인칭 모델)도 변환할 수 있게 저장된 GameObject 를 돌려준다.</summary>
    private static GameObject ConvertPrefab(string what, string legacyPath, string outputName, string outputPath,
        System.Func<GameObject, bool> build, bool verifyTargets)
    {
        EnsureFolder(System.IO.Path.GetDirectoryName(outputPath).Replace('\\', '/'));

        GameObject legacy = AssetDatabase.LoadAssetAtPath<GameObject>(legacyPath);

        if (legacy == null)
        {
            Debug.LogError($"[LegacyMissionConverter] 옛 {what} 프리팹이 없습니다: {legacyPath}");
            return null;
        }

        GameObject copy = Object.Instantiate(legacy);
        copy.name = outputName;
        return FinishPrefab(what, copy, outputPath, build, verifyTargets);
    }

    /// <summary>원본 없이 빈 루트에서 조립한다 (3b — 모델이 없어 기본 도형으로). 검증 · 저장은 Convert 와 같다.</summary>
    private static NetworkObject Assemble(string what, string outputName, string outputPath,
        System.Func<GameObject, bool> build, bool verifyTargets = true)
    {
        GameObject saved = AssemblePrefab(what, outputName, outputPath, build, verifyTargets);
        return saved != null ? saved.GetComponent<NetworkObject>() : null;
    }

    private static GameObject AssemblePrefab(string what, string outputName, string outputPath,
        System.Func<GameObject, bool> build, bool verifyTargets)
    {
        EnsureFolder(System.IO.Path.GetDirectoryName(outputPath).Replace('\\', '/'));
        return FinishPrefab(what, new GameObject(outputName), outputPath, build, verifyTargets);
    }

    /// <summary>공통 뒷부분: 조립 → 안내 빌보드 → 검증 2가지 → 저장 → 복사본 파괴. 하나라도 실패하면 저장하지 않는다.</summary>
    private static GameObject FinishPrefab(string what, GameObject copy, string outputPath,
        System.Func<GameObject, bool> build, bool verifyTargets)
    {
        try
        {
            if (!build(copy))
                return null;

            AddPromptBillboards(copy);

            if (!VerifyNoLegacyScripts(copy) || (verifyTargets && !VerifyTargetResolution(copy)))
                return null;

            GameObject saved = PrefabUtility.SaveAsPrefabAsset(copy, outputPath);
            Debug.Log($"[LegacyMissionConverter] {what} 변환 완료: {outputPath}");
            return saved;
        }
        finally
        {
            Object.DestroyImmediate(copy);
        }
    }

    /// <summary>스테이션 공통 값 설정. 반환한 SerializedObject 에 미니게임별 값을 더 넣고 Apply 한다.</summary>
    private static SerializedObject ConfigureStation(MissionStation station, MissionEventType completionEvent, CompletionPolicy policy, float range)
    {
        SerializedObject so = new SerializedObject(station);
        // enum 은 intValue 로 넣는다: MissionEventType 값이 연속이 아니라 enumValueIndex 를 쓰면 엉뚱한 값이 들어간다
        so.FindProperty("completionEvent").intValue = (int)completionEvent;
        so.FindProperty("completionPolicy").intValue = (int)policy;
        so.FindProperty("interactRange").floatValue = range;
        return so;
    }

    // ═════════════════════════════════════════════════════════════
    //  1단계: 발전기
    // ═════════════════════════════════════════════════════════════

    private static bool BuildGenerator(GameObject copy)
    {
        MonoBehaviour oldMission = FindLegacy(copy, "GeneratorMission");
        MonoBehaviour oldButton = FindLegacy(copy, "MissionButton");

        if (oldMission == null || oldButton == null)
        {
            Debug.LogError("[LegacyMissionConverter] 발전기 프리팹에서 GeneratorMission 또는 MissionButton 을 찾지 못했습니다.");
            return false;
        }

        SerializedObject oldMissionSO = new SerializedObject(oldMission);
        SerializedObject oldButtonSO = new SerializedObject(oldButton);
        GameObject buttonObject = oldButton.gameObject;

        // 루트: GeneratorMission → GeneratorStation
        GeneratorStation station = copy.AddComponent<GeneratorStation>();
        SerializedObject stationSO = ConfigureStation(station, MissionEventType.GeneratorRepaired, CompletionPolicy.Lock, 3.5f);
        CopyReference(oldMissionSO, "guideText", stationSO, "guideText");
        CopyReference(oldMissionSO, "gaugeFill", stationSO, "gaugeFill");
        CopyReference(oldMissionSO, "completeText", stationSO, "completeText");
        CopyFloat(oldMissionSO, "fillDuration", stationSO, "fillDuration");
        CopyString(oldMissionSO, "waitingText", stationSO, "waitingText");
        CopyString(oldMissionSO, "runningText", stationSO, "runningText");
        CopyColliders(oldMissionSO, stationSO, buttonObject);
        stationSO.ApplyModifiedPropertiesWithoutUndo();

        // 버튼: MissionButton → StationButton (partIndex 0)
        // 부품은 "자기 콜라이더가 있는 오브젝트"에 붙인다. 옛 MissionButton 은 루트에 있었지만, 새 GeneratorStation(루트)도
        // 조준 대상(ITargetable)이라 루트에 두면 감지기가 스테이션을 먼저 잡아서 클릭이 버튼에 전달되지 않는다.
        GameObject partObject = FindPartObject(stationSO, copy) ?? buttonObject;
        StationButton button = AddStationButton(partObject, station, 0);
        SerializedObject buttonSO = new SerializedObject(button);
        CopyReference(oldButtonSO, "buttonVisual", buttonSO, "buttonVisual");
        CopyVector3(oldButtonSO, "pressOffset", buttonSO, "pressOffset");
        CopyFloat(oldButtonSO, "pressTime", buttonSO, "pressTime");
        CopyFloat(oldButtonSO, "returnTime", buttonSO, "returnTime");
        buttonSO.ApplyModifiedPropertiesWithoutUndo();

        // 조준 시 외곽선: 버튼을 조준해서 누르는 미니게임이라 버튼 모델에 붙인다 (눌림 애니메이션을 따라 움직인다)
        Transform buttonVisual = buttonSO.FindProperty("buttonVisual").objectReferenceValue as Transform;
        MissionOutlineBuilder.Attach(copy, buttonVisual != null ? buttonVisual.gameObject : buttonObject);

        Object.DestroyImmediate(oldButton);
        Object.DestroyImmediate(oldMission);
        return true;
    }

    // ═════════════════════════════════════════════════════════════
    //  2a: 밸브 (F 홀드로 회전)
    // ═════════════════════════════════════════════════════════════

    private static bool BuildValve(GameObject copy)
    {
        MonoBehaviour oldMission = FindLegacy(copy, "ValveMission");

        if (oldMission == null)
        {
            Debug.LogError("[LegacyMissionConverter] 밸브 프리팹에서 ValveMission 을 찾지 못했습니다.");
            return false;
        }

        SerializedObject oldSO = new SerializedObject(oldMission);

        // 루트: ValveMission → ValveStation. 부품 없음 — 모델 콜라이더에서 위로 올라가면 루트의 ValveStation(F 홀드)이 잡힌다.
        ValveStation station = copy.AddComponent<ValveStation>();
        SerializedObject so = ConfigureStation(station, MissionEventType.ValveClosed, CompletionPolicy.ResetForNext, PersonalInteractRange);
        CopyReference(oldSO, "rotatingPart", so, "rotatingPart");
        CopyVector3(oldSO, "rotationAxis", so, "rotationAxis");
        CopyFloat(oldSO, "rotationSpeed", so, "rotationSpeed");
        CopyFloat(oldSO, "minTargetAngle", so, "minTargetAngle");
        CopyFloat(oldSO, "maxTargetAngle", so, "maxTargetAngle");
        CopyColliders(oldSO, so, copy);
        so.ApplyModifiedPropertiesWithoutUndo();

        // 외곽선: 밸브 프리팹은 이미 MissionPrompt + 외곽선(ValveOutLine)을 갖고 있다 → 비어 있을 때만 만든다
        if (!HasHighlight(copy))
        {
            Transform part = so.FindProperty("rotatingPart").objectReferenceValue as Transform;
            MissionOutlineBuilder.Attach(copy, part != null ? part.gameObject : copy);
        }

        Object.DestroyImmediate(oldMission);
        return true;
    }

    // ═════════════════════════════════════════════════════════════
    //  2a: 안테나 (F 홀드로 회전 → 고정 버튼)
    // ═════════════════════════════════════════════════════════════

    private static bool BuildAntenna(GameObject copy)
    {
        MonoBehaviour oldMission = FindLegacy(copy, "AntennaMission");
        MonoBehaviour oldLock = FindLegacy(copy, "AntennaLockButton");

        if (oldMission == null || oldLock == null)
        {
            Debug.LogError("[LegacyMissionConverter] 안테나 프리팹에서 AntennaMission 또는 AntennaLockButton 을 찾지 못했습니다.");
            return false;
        }

        SerializedObject oldSO = new SerializedObject(oldMission);
        GameObject lockObject = oldLock.gameObject;

        if (lockObject == copy)
        {
            Debug.LogError("[LegacyMissionConverter] 안테나 고정 버튼이 루트에 있어 스테이션에 가려집니다.");
            return false;
        }

        AntennaStation station = copy.AddComponent<AntennaStation>();
        SerializedObject so = ConfigureStation(station, MissionEventType.AntennaAligned, CompletionPolicy.ResetForNext, PersonalInteractRange);
        CopyReference(oldSO, "antennaTransform", so, "rotatingPart");
        CopyVector3(oldSO, "rotationAxis", so, "rotationAxis");
        CopyFloat(oldSO, "rotationSpeed", so, "rotationSpeed");
        // 옛 AntennaMission 은 목표 각도를 상수(MinTargetAngle 360 / MaxTargetAngle 720)로 가졌다
        so.FindProperty("minTargetAngle").floatValue = 360f;
        so.FindProperty("maxTargetAngle").floatValue = 720f;
        CopyReference(oldSO, "gaugeFill", so, "gaugeFill");
        CopyReference(oldSO, "percentText", so, "percentText");
        CopyColliders(oldSO, so, copy);

        // 고정 버튼 콜라이더도 "내 미션 아니면 끄기" 대상에 넣는다 (옛 목록에 빠져 있어도)
        foreach (Collider c in lockObject.GetComponents<Collider>())
            AddCollider(so, c);

        so.ApplyModifiedPropertiesWithoutUndo();

        // 고정 버튼: AntennaLockButton → StationButton(0). 버튼 모양은 캔버스 UI 라 눌림 애니메이션(buttonVisual)은 없다.
        AddStationButton(lockObject, station, AntennaStation.LockButtonPart);

        // 외곽선: 안테나 몸체. ButtonHitbox 근처에는 메시가 없어서(UI 버튼) 자기 MissionPrompt 를 두지 않는다
        //  → 버튼을 조준해도 GetComponentInParent 로 루트 프롬프트가 잡혀 몸체 외곽선이 켜진다 (2a 명세 6장 해소).
        Transform part = so.FindProperty("rotatingPart").objectReferenceValue as Transform;
        MissionOutlineBuilder.Attach(copy, part != null ? part.gameObject : copy);

        Object.DestroyImmediate(oldLock);
        Object.DestroyImmediate(oldMission);
        return true;
    }

    // ═════════════════════════════════════════════════════════════
    //  2a: 차단기 (레버 6개 클릭)
    // ═════════════════════════════════════════════════════════════

    private static bool BuildBreaker(GameObject copy)
    {
        MonoBehaviour oldMission = FindLegacy(copy, "BreakerMission");
        List<MonoBehaviour> oldLevers = FindAllLegacy(copy, "BreakerLever");

        if (oldMission == null || oldLevers.Count == 0)
        {
            Debug.LogError("[LegacyMissionConverter] 차단기 프리팹에서 BreakerMission 또는 BreakerLever 를 찾지 못했습니다.");
            return false;
        }

        if (oldLevers.Count > BreakerStation.MaxLevers)
        {
            Debug.LogError($"[LegacyMissionConverter] 차단기 레버가 {oldLevers.Count}개입니다 (최대 {BreakerStation.MaxLevers}).");
            return false;
        }

        SerializedObject oldSO = new SerializedObject(oldMission);

        BreakerStation station = copy.AddComponent<BreakerStation>();
        SerializedObject so = ConfigureStation(station, MissionEventType.BreakerRestored, CompletionPolicy.ResetForNext, PersonalInteractRange);
        CopyColliders(oldSO, so, copy);

        BreakerLeverVisual[] visuals = new BreakerLeverVisual[oldLevers.Count];

        foreach (MonoBehaviour oldLever in oldLevers)
        {
            SerializedObject leverSO = new SerializedObject(oldLever);
            int index = leverSO.FindProperty("leverIndex").intValue;

            // 레버 번호는 0 ~ (개수-1) 이고 겹치면 안 된다 (배열 인덱스 = partIndex)
            if (index < 0 || index >= visuals.Length || visuals[index] != null)
            {
                Debug.LogError($"[LegacyMissionConverter] 차단기 레버 '{oldLever.name}' 의 번호 {index} 가 범위를 벗어나거나 중복입니다.");
                return false;
            }

            GameObject leverObject = oldLever.gameObject;

            // 모습: BreakerLever 의 연출 값을 BreakerLeverVisual 로
            BreakerLeverVisual visual = leverObject.AddComponent<BreakerLeverVisual>();
            SerializedObject visualSO = new SerializedObject(visual);
            CopyReference(leverSO, "leverTransform", visualSO, "leverTransform");
            CopyReference(leverSO, "leverRenderer", visualSO, "leverRenderer");
            CopyVector3(leverSO, "offRotation", visualSO, "offRotation");
            CopyVector3(leverSO, "onRotation", visualSO, "onRotation");
            CopyFloat(leverSO, "rotateDuration", visualSO, "rotateDuration");
            visualSO.ApplyModifiedPropertiesWithoutUndo();
            visuals[index] = visual;

            // 클릭: StationButton (레버는 회전 연출이 반응을 보여 주므로 눌림 애니메이션 없음)
            AddStationButton(leverObject, station, index);

            foreach (Collider c in leverObject.GetComponents<Collider>())
                AddCollider(so, c);

            // 외곽선: 레버마다 MissionPrompt → 조준한 레버만 켜진다 (2a 명세 P7)
            Renderer leverRenderer = visualSO.FindProperty("leverRenderer").objectReferenceValue as Renderer;
            MissionOutlineBuilder.Attach(leverObject, leverRenderer != null ? leverRenderer.gameObject : leverObject);
        }

        SerializedProperty leverList = so.FindProperty("levers");
        leverList.ClearArray();

        for (int i = 0; i < visuals.Length; i++)
        {
            leverList.InsertArrayElementAtIndex(i);
            leverList.GetArrayElementAtIndex(i).objectReferenceValue = visuals[i];
        }

        so.ApplyModifiedPropertiesWithoutUndo();

        foreach (MonoBehaviour oldLever in oldLevers)
            Object.DestroyImmediate(oldLever);

        Object.DestroyImmediate(oldMission);
        return true;
    }

    // ═════════════════════════════════════════════════════════════
    //  2b: 전선 (시작점 드래그 → 같은 색 도착점 → 레버)
    // ═════════════════════════════════════════════════════════════

    private static bool BuildWiring(GameObject copy)
    {
        const int wireCount = WiringRules.WireCount;

        MonoBehaviour oldMission = FindLegacy(copy, "WiringMission");
        MonoBehaviour oldLever = FindLegacy(copy, "WiringLever");
        List<MonoBehaviour> oldStarts = FindAllLegacy(copy, "WireStartPoint");
        List<MonoBehaviour> oldEnds = FindAllLegacy(copy, "WireEndPoint");

        if (oldMission == null || oldLever == null || oldStarts.Count != wireCount || oldEnds.Count != wireCount)
        {
            Debug.LogError($"[LegacyMissionConverter] 전선 프리팹 구성이 다릅니다 (WiringMission · WiringLever 1개씩, 시작점 · 도착점 {wireCount}개씩 필요).");
            return false;
        }

        SerializedObject oldSO = new SerializedObject(oldMission);

        WiringStation station = copy.AddComponent<WiringStation>();
        SerializedObject so = ConfigureStation(station, MissionEventType.WiresConnected, CompletionPolicy.ResetForNext, PersonalInteractRange);
        CopyColliders(oldSO, so, copy);

        WireVisual[] wires = new WireVisual[wireCount];
        Transform[] endAnchors = new Transform[wireCount];
        int[] endColors = new int[wireCount];
        bool[] endSeen = new bool[wireCount];
        List<MonoBehaviour> oldLines = new List<MonoBehaviour>();

        // 시작점: WireStartPoint → StationDragPart, 그 시작점의 WireConnectionVisual → WireVisual
        foreach (MonoBehaviour oldStart in oldStarts)
        {
            SerializedObject startSO = new SerializedObject(oldStart);
            int index = startSO.FindProperty("index").intValue;

            if (index < 0 || index >= wireCount || wires[index] != null)
            {
                Debug.LogError($"[LegacyMissionConverter] 전선 시작점 '{oldStart.name}' 의 번호 {index} 가 범위를 벗어나거나 중복입니다.");
                return false;
            }

            MonoBehaviour oldLine = startSO.FindProperty("connectionVisual").objectReferenceValue as MonoBehaviour;

            if (oldLine == null)
            {
                Debug.LogError($"[LegacyMissionConverter] 전선 시작점 '{oldStart.name}' 에 connectionVisual 이 없습니다.");
                return false;
            }

            SerializedObject lineSO = new SerializedObject(oldLine);
            WireVisual wire = oldLine.gameObject.AddComponent<WireVisual>();
            SerializedObject wireSO = new SerializedObject(wire);

            // 꽂이 색 (옛 WireStartPoint). anchor 가 비었으면 옛 코드처럼 시작점 자신
            if (startSO.FindProperty("anchor").objectReferenceValue != null)
                CopyReference(startSO, "anchor", wireSO, "startAnchor");
            else
                wireSO.FindProperty("startAnchor").objectReferenceValue = oldStart.transform;

            CopyReference(startSO, "colorRenderer", wireSO, "plugRenderer");
            CopyInt(startSO, "materialIndex", wireSO, "plugMaterialIndex");
            CopyString(startSO, "colorProperty", wireSO, "plugColorProperty");

            // 전선 메시 (옛 WireConnectionVisual). lengthAxis 는 옛 WireMeshAxis 와 같은 값
            CopyReference(lineSO, "wireMesh", wireSO, "wireMesh");
            CopyReference(lineSO, "wireMeshFilter", wireSO, "wireMeshFilter");
            CopyReference(lineSO, "wireRenderer", wireSO, "wireRenderer");
            CopyInt(lineSO, "lengthAxis", wireSO, "lengthAxis");
            CopyReference(lineSO, "wirePlane", wireSO, "wirePlane");
            CopyString(lineSO, "colorProperty", wireSO, "wireColorProperty");
            CopyFloat(lineSO, "extraLength", wireSO, "extraLength");
            CopyFloat(lineSO, "followSpeed", wireSO, "followSpeed");
            wireSO.ApplyModifiedPropertiesWithoutUndo();

            wires[index] = wire;
            oldLines.Add(oldLine);

            GameObject startObject = oldStart.gameObject;
            StationDragPart dragPart = startObject.AddComponent<StationDragPart>();
            SerializedObject dragSO = new SerializedObject(dragPart);
            dragSO.FindProperty("station").objectReferenceValue = station;
            dragSO.FindProperty("partIndex").intValue = index;
            dragSO.FindProperty("preview").objectReferenceValue = wire;
            dragSO.ApplyModifiedPropertiesWithoutUndo();

            foreach (Collider c in startObject.GetComponents<Collider>())
                AddCollider(so, c);

            // 외곽선: 꽂이 오브젝트에는 자기 메시가 없고, 꽂이 모양은 패널 메시의 서브메시(= 색을 칠하는 재질 번호)다.
            //  그 서브메시만 떼어 외곽선을 만든다 → 조준한 줄만 빛난다 (2b 테스트 피드백, 명세 W8)
            Renderer plugRenderer = startSO.FindProperty("colorRenderer").objectReferenceValue as Renderer;
            MeshFilter plugMesh = plugRenderer != null ? plugRenderer.GetComponent<MeshFilter>() : null;
            MissionOutlineBuilder.AttachSubmesh(startObject, plugMesh, startSO.FindProperty("materialIndex").intValue, $"WirePlug_{index}");
        }

        // 도착점: WireEndPoint → StationDropTarget, 색 · 위치는 스테이션 배열로
        foreach (MonoBehaviour oldEnd in oldEnds)
        {
            SerializedObject endSO = new SerializedObject(oldEnd);
            int index = endSO.FindProperty("index").intValue;

            if (index < 0 || index >= wireCount || endSeen[index])
            {
                Debug.LogError($"[LegacyMissionConverter] 전선 도착점 '{oldEnd.name}' 의 번호 {index} 가 범위를 벗어나거나 중복입니다.");
                return false;
            }

            endSeen[index] = true;
            endColors[index] = endSO.FindProperty("wireColor").intValue;

            Transform anchor = endSO.FindProperty("anchor").objectReferenceValue as Transform;
            endAnchors[index] = anchor != null ? anchor : oldEnd.transform;

            GameObject endObject = oldEnd.gameObject;
            StationDropTarget dropTarget = endObject.AddComponent<StationDropTarget>();
            SerializedObject dropSO = new SerializedObject(dropTarget);
            dropSO.FindProperty("station").objectReferenceValue = station;
            dropSO.FindProperty("partIndex").intValue = index;
            dropSO.ApplyModifiedPropertiesWithoutUndo();

            foreach (Collider c in endObject.GetComponents<Collider>())
                AddCollider(so, c);

            // 외곽선 없음: 도착점은 따로 뗄 모양이 없다. 빈 프롬프트를 둬서 패널 전체가 빛나지 않게 한다 (W8)
            MissionOutlineBuilder.AttachEmpty(endObject);
        }

        // 레버: WiringLever → StationButton(LeverPart). 당겨졌다 돌아오는 연출은 pressRotation (W7)
        SerializedObject oldLeverSO = new SerializedObject(oldLever);
        GameObject leverObject = oldLever.gameObject;
        Transform leverTransform = oldLeverSO.FindProperty("leverTransform").objectReferenceValue as Transform;

        if (leverTransform == null)
            leverTransform = leverObject.transform;

        StationButton lever = AddStationButton(leverObject, station, WiringStation.LeverPart);
        SerializedObject leverSO = new SerializedObject(lever);
        leverSO.FindProperty("buttonVisual").objectReferenceValue = leverTransform;

        Vector3 current = leverTransform.localEulerAngles;
        Vector3 completed = oldLeverSO.FindProperty("completedRotation").vector3Value;
        leverSO.FindProperty("pressRotation").vector3Value = new Vector3(
            Mathf.DeltaAngle(current.x, completed.x),
            Mathf.DeltaAngle(current.y, completed.y),
            Mathf.DeltaAngle(current.z, completed.z));

        CopyFloat(oldLeverSO, "rotateDuration", leverSO, "pressTime");
        CopyFloat(oldLeverSO, "rotateDuration", leverSO, "returnTime");
        leverSO.ApplyModifiedPropertiesWithoutUndo();

        foreach (Collider c in leverObject.GetComponents<Collider>())
            AddCollider(so, c);

        AttachPartOutline(leverObject);

        // 스테이션 배열 채우기 (인덱스 = 부품 번호)
        SerializedProperty wireList = so.FindProperty("wires");
        SerializedProperty anchorList = so.FindProperty("endAnchors");
        SerializedProperty colorList = so.FindProperty("endColors");
        wireList.ClearArray();
        anchorList.ClearArray();
        colorList.ClearArray();

        for (int i = 0; i < wireCount; i++)
        {
            wireList.InsertArrayElementAtIndex(i);
            wireList.GetArrayElementAtIndex(i).objectReferenceValue = wires[i];
            anchorList.InsertArrayElementAtIndex(i);
            anchorList.GetArrayElementAtIndex(i).objectReferenceValue = endAnchors[i];
            colorList.InsertArrayElementAtIndex(i);
            colorList.GetArrayElementAtIndex(i).intValue = endColors[i];
        }

        so.ApplyModifiedPropertiesWithoutUndo();

        // 패널 전체 외곽선은 붙이지 않는다: 어느 부품을 조준했는지 알 수 없게 된다 (W8). 모든 부품이 자기 프롬프트를 가진다.

        foreach (MonoBehaviour old in oldStarts)
            Object.DestroyImmediate(old);

        foreach (MonoBehaviour old in oldEnds)
            Object.DestroyImmediate(old);

        foreach (MonoBehaviour old in oldLines)
            Object.DestroyImmediate(old);

        Object.DestroyImmediate(oldLever);
        Object.DestroyImmediate(oldMission);
        return true;
    }

    // ═════════════════════════════════════════════════════════════
    //  2c: 필터 (먼지 = 중첩 NetworkObject 주소 + 스테이션이 상태 관리)
    // ═════════════════════════════════════════════════════════════

    private static bool BuildFilter(GameObject copy)
    {
        MonoBehaviour oldMission = FindLegacy(copy, "FilterCleaningMission");

        if (oldMission == null)
        {
            Debug.LogError("[LegacyMissionConverter] 필터 프리팹에서 FilterCleaningMission 을 찾지 못했습니다.");
            return false;
        }

        SerializedObject oldSO = new SerializedObject(oldMission);
        SerializedProperty oldDusts = oldSO.FindProperty("dusts");
        int count = oldDusts != null ? oldDusts.arraySize : 0;

        if (count == 0 || count > FilterStation.MaxDusts)
        {
            Debug.LogError($"[LegacyMissionConverter] 필터 먼지가 {count}개입니다 (1~{FilterStation.MaxDusts}개 필요).");
            return false;
        }

        FilterStation station = copy.AddComponent<FilterStation>();
        SerializedObject so = ConfigureStation(station, MissionEventType.FilterCleaned, CompletionPolicy.ResetForNext, PersonalInteractRange);

        // F8: 먼지 콜라이더는 FilterStation 이 직접 관리한다 → 베이스 목록은 비운다
        so.FindProperty("interactionColliders").ClearArray();

        FilterDust[] newDusts = new FilterDust[count];
        List<MonoBehaviour> oldDustComponents = new List<MonoBehaviour>();

        // 옛 dusts 배열 순서 = 먼지 번호
        for (int i = 0; i < count; i++)
        {
            MonoBehaviour oldDust = oldDusts.GetArrayElementAtIndex(i).objectReferenceValue as MonoBehaviour;

            if (oldDust == null || oldDustComponents.Contains(oldDust))
            {
                Debug.LogError($"[LegacyMissionConverter] 필터 먼지 {i}번이 비어 있거나 중복입니다.");
                return false;
            }

            if (oldDust.GetComponent<NetworkObject>() == null)
            {
                Debug.LogError($"[LegacyMissionConverter] 필터 먼지 '{oldDust.name}' 에 NetworkObject 가 없습니다 (청소기 우클릭이 먼지를 구분할 수 없음).");
                return false;
            }

            SerializedObject oldDustSO = new SerializedObject(oldDust);
            FilterDust dust = oldDust.gameObject.AddComponent<FilterDust>();
            SerializedObject dustSO = new SerializedObject(dust);
            dustSO.FindProperty("station").objectReferenceValue = station;
            dustSO.FindProperty("index").intValue = i;
            CopyReference(oldDustSO, "dustCollider", dustSO, "dustCollider");
            CopyReference(oldDustSO, "outlineRoot", dustSO, "outlineObject");
            dustSO.ApplyModifiedPropertiesWithoutUndo();

            newDusts[i] = dust;
            oldDustComponents.Add(oldDust);
        }

        SerializedProperty dustList = so.FindProperty("dusts");
        dustList.ClearArray();

        for (int i = 0; i < count; i++)
        {
            dustList.InsertArrayElementAtIndex(i);
            dustList.GetArrayElementAtIndex(i).objectReferenceValue = newDusts[i];
        }

        so.ApplyModifiedPropertiesWithoutUndo();

        foreach (MonoBehaviour old in oldDustComponents)
            Object.DestroyImmediate(old);

        Object.DestroyImmediate(oldMission);
        return true;
    }

    // ═════════════════════════════════════════════════════════════
    //  2c: 청소기 아이템 (1인칭 모델 · 아이템 데이터 · 아이템)
    // ═════════════════════════════════════════════════════════════

    private static bool BuildVacuumFirstPerson(GameObject copy)
    {
        MonoBehaviour oldView = FindLegacy(copy, "FirstPersonVacuumView");

        if (oldView == null)
        {
            Debug.LogError("[LegacyMissionConverter] 청소기 1인칭 프리팹에서 FirstPersonVacuumView 를 찾지 못했습니다.");
            return false;
        }

        SerializedObject oldSO = new SerializedObject(oldView);
        VacuumFirstPersonView view = oldView.gameObject.AddComponent<VacuumFirstPersonView>();
        SerializedObject so = new SerializedObject(view);
        CopyReference(oldSO, "mouth", so, "mouth");
        so.ApplyModifiedPropertiesWithoutUndo();

        Object.DestroyImmediate(oldView);
        return true;
    }

    /// <summary>
    /// 팀원의 VacuumData 를 복사해 1인칭 프리팹만 새 것으로 바꾼다 (결정 F9 — 원본을 바꾸면 교체 전까지 옛 청소기가 깨진다).
    /// 이미 있으면 원본 값을 다시 덮어써 아이콘 · 이름 변경을 따라가고, 에셋 GUID 는 유지한다.
    /// </summary>
    private static Object CreateVacuumToolData(GameObject firstPersonPrefab)
    {
        EnsureFolder(ItemDataFolder);

        Object original = AssetDatabase.LoadAssetAtPath<Object>(LegacyVacuumDataPath);

        if (original == null)
        {
            Debug.LogError($"[LegacyMissionConverter] 청소기 아이템 데이터가 없습니다: {LegacyVacuumDataPath}");
            return null;
        }

        Object data = AssetDatabase.LoadAssetAtPath<Object>(VacuumToolDataPath);

        if (data == null)
        {
            if (!AssetDatabase.CopyAsset(LegacyVacuumDataPath, VacuumToolDataPath))
            {
                Debug.LogError($"[LegacyMissionConverter] 아이템 데이터 복사 실패: {VacuumToolDataPath}");
                return null;
            }

            data = AssetDatabase.LoadAssetAtPath<Object>(VacuumToolDataPath);
        }
        else
        {
            EditorUtility.CopySerialized(original, data);
            data.name = System.IO.Path.GetFileNameWithoutExtension(VacuumToolDataPath);
        }

        SerializedObject so = new SerializedObject(data);
        SerializedProperty firstPerson = so.FindProperty("firstPersonPrefab");

        if (firstPerson == null)
        {
            Debug.LogError("[LegacyMissionConverter] 아이템 데이터에 firstPersonPrefab 이 없습니다.");
            return null;
        }

        firstPerson.objectReferenceValue = firstPersonPrefab;
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(data);

        Debug.Log($"[LegacyMissionConverter] 청소기 아이템 데이터 준비 완료: {VacuumToolDataPath}");
        return data;
    }

    private static bool BuildVacuumTool(GameObject copy, Object itemData)
    {
        MonoBehaviour oldItem = FindLegacy(copy, "VacuumItem");

        if (oldItem == null)
        {
            Debug.LogError("[LegacyMissionConverter] 청소기 프리팹에서 VacuumItem 을 찾지 못했습니다.");
            return false;
        }

        SerializedObject oldSO = new SerializedObject(oldItem);
        VacuumTool tool = oldItem.gameObject.AddComponent<VacuumTool>();
        SerializedObject so = new SerializedObject(tool);
        so.FindProperty("data").objectReferenceValue = itemData;
        CopyReference(oldSO, "mouth", so, "mouth");
        CopyFloat(oldSO, "suctionDuration", so, "suctionDuration");
        so.ApplyModifiedPropertiesWithoutUndo();

        // 조준하면 외곽선 (미션 아이템 공통 — 3c 테스트 후 추가 EA16). 옛 청소기 프리팹에는 외곽선이 없어 모델 메시로 만든다
        if (!HasHighlight(copy))
        {
            Transform model = FindDeep(copy.transform, "Vacuum_Model");
            MissionOutlineBuilder.Attach(copy, model != null ? model.gameObject : copy);
        }

        Object.DestroyImmediate(oldItem);
        return true;
    }

    // ═════════════════════════════════════════════════════════════
    //  2d: 압력 (신규 — 원본 패널 + 피스톤 A/B/C 모델에서 조립)
    // ═════════════════════════════════════════════════════════════

    private static bool BuildPressure(GameObject copy)
    {
        if (copy.GetComponent<NetworkObject>() == null)
            copy.AddComponent<NetworkObject>();

        PressureStation station = copy.AddComponent<PressureStation>();
        SerializedObject so = ConfigureStation(station, MissionEventType.PressureStabilized, CompletionPolicy.ResetForNext, PersonalInteractRange);

        PressureVisual[] visuals = new PressureVisual[PressureStation.PistonCount];

        // 표시등 재질 (PR14): 원본 네온 재질 Neon_green · Neon_red(발광 켜짐 · 단색)를 글자에 그대로 끼운다
        Material lockedMaterial = AssetDatabase.LoadAssetAtPath<Material>($"{SourceMaterialFolder}/Neon_green.mat");
        Material stalledMaterial = AssetDatabase.LoadAssetAtPath<Material>($"{SourceMaterialFolder}/Neon_red.mat");

        if (lockedMaterial == null || stalledMaterial == null)
            Debug.LogWarning("[LegacyMissionConverter] 압력: Neon_green / Neon_red 재질을 찾지 못해 글자 색이 바뀌지 않습니다.");

        for (int i = 0; i < PressureStation.PistonCount; i++)
        {
            string letter = PressureLetters[i];

            // 피스톤 모델 (원본 FBX 를 복사해 자식으로, 테스트 씬과 같은 상대 위치).
            //  PR14: 글자(A/B/C)가 별도 부품으로 분리된 V2 모델을 쓴다 — 글자만 따로 초록 · 빨강으로 바꿀 수 있다
            string pistonPath = $"{SourceModelFolder}/Pressure_piston_{letter}_V2.fbx";
            GameObject pistonModel = AssetDatabase.LoadAssetAtPath<GameObject>(pistonPath);

            if (pistonModel == null)
            {
                Debug.LogError($"[LegacyMissionConverter] 압력 피스톤 모델이 없습니다: {pistonPath}");
                return false;
            }

            GameObject piston = Object.Instantiate(pistonModel, copy.transform);
            piston.name = $"Pressure_piston_{letter}";
            piston.transform.localPosition = PistonOffsets[i];
            piston.transform.localRotation = Quaternion.identity;

            // 노드 찾기: 모델 이름에 오타 · 공백이 있어 "토큰" 단위로 찾는다 (예: Pressure_button_A._nteraction)
            Transform weight = FindByTokens(piston.transform, letter, new[] { "piston", "parts" }, null, false);
            // 원통 본체: V2 의 글자(Pressure_piston_A_Alphabet)도 "piston + A" 라서 alphabet 을 빼야 본체가 잡힌다
            Transform body = FindByTokens(piston.transform, letter, new[] { "piston" }, new[] { "parts", "alphabet" }, true);
            Transform alphabet = FindByTokens(piston.transform, letter, new[] { "alphabet" }, null, true);
            Transform gauge = FindByTokens(copy.transform, letter, new[] { "gauge" }, new[] { "needle", "indicator" }, true);
            Transform needle = FindByTokens(copy.transform, letter, new[] { "needle" }, null, false);
            Transform button = FindByTokens(copy.transform, letter, new[] { "button", "nteraction" }, null, true);

            if (weight == null || body == null || gauge == null || needle == null || button == null)
            {
                Debug.LogError($"[LegacyMissionConverter] 압력 {letter}: 모델 노드를 찾지 못했습니다 " +
                    $"(추 {Found(weight)}, 원통 {Found(body)}, 압력계 {Found(gauge)}, 바늘 {Found(needle)}, 버튼 {Found(button)}).");
                return false;
            }

            // 바늘 회전축 (PR10): 압력계 중심에 만들고 바늘을 그 아래로 옮긴다
            Renderer gaugeRenderer = gauge.GetComponent<Renderer>();
            GameObject pivot = new GameObject($"NeedlePivot_{letter}");
            pivot.transform.SetParent(gauge.parent, false);
            pivot.transform.position = gaugeRenderer.bounds.center;
            pivot.transform.rotation = gauge.rotation;
            needle.SetParent(pivot.transform, true);
            Vector3 needleAxis = ThinnestAxis(gauge);

            // 추 이동 범위 기본값: 원통 높이의 ±30% (부모 로컬 단위로)
            Renderer bodyRenderer = body.GetComponent<Renderer>();
            float parentScale = weight.parent != null ? Mathf.Max(0.0001f, Mathf.Abs(weight.parent.lossyScale.y)) : 1f;
            float travel = bodyRenderer.bounds.size.y * 0.3f / parentScale;
            Vector3 weightAxis = weight.parent != null ? weight.parent.InverseTransformDirection(Vector3.up).normalized : Vector3.up;

            // 표시등 (PR14): 피스톤 글자. 못 찾으면 경고 후 표시등 없이 동작 (게임 규칙에는 영향 없음)
            Renderer letterRenderer = alphabet != null ? alphabet.GetComponent<Renderer>() : null;

            if (letterRenderer == null)
                Debug.LogWarning($"[LegacyMissionConverter] 압력 {letter}: 피스톤 글자(Alphabet)를 찾지 못해 표시등 없이 동작합니다.");

            GameObject visualObject = new GameObject($"Piston_{letter}");
            visualObject.transform.SetParent(copy.transform, false);
            PressureVisual visual = visualObject.AddComponent<PressureVisual>();
            SerializedObject visualSO = new SerializedObject(visual);
            visualSO.FindProperty("needlePivot").objectReferenceValue = pivot.transform;
            visualSO.FindProperty("needleAxis").vector3Value = needleAxis;
            visualSO.FindProperty("weight").objectReferenceValue = weight;
            visualSO.FindProperty("weightAxis").vector3Value = weightAxis;
            visualSO.FindProperty("bottomOffset").floatValue = -travel;
            visualSO.FindProperty("topOffset").floatValue = travel;
            visualSO.FindProperty("lampRenderer").objectReferenceValue = letterRenderer;
            visualSO.FindProperty("lockedMaterial").objectReferenceValue = lockedMaterial;
            visualSO.FindProperty("stalledMaterial").objectReferenceValue = stalledMaterial;
            visualSO.ApplyModifiedPropertiesWithoutUndo();
            visuals[i] = visual;

            // 버튼: 조준 레이어 · 콜라이더 · StationButton · 외곽선 (코드 자판과 같은 공통 함수)
            float pressDepth = SetupPressButton(so, station, button, i);

            Debug.Log($"[LegacyMissionConverter] 압력 {letter} 추정값: 바늘 축 {needleAxis}, 각도 -120~120, 추 범위 ±{travel:0.000} (축 {weightAxis}), 글자 {Found(alphabet)}, 눌림 {pressDepth:0.000}m");
        }

        SerializedProperty pistonList = so.FindProperty("pistons");
        pistonList.ClearArray();

        for (int i = 0; i < visuals.Length; i++)
        {
            pistonList.InsertArrayElementAtIndex(i);
            pistonList.GetArrayElementAtIndex(i).objectReferenceValue = visuals[i];
        }

        so.ApplyModifiedPropertiesWithoutUndo();
        return true;
    }

    // ═════════════════════════════════════════════════════════════
    //  3a: 코드 순서 맞추기 (신규 — 원본 자판 모델에서 조립)
    // ═════════════════════════════════════════════════════════════

    private static bool BuildCode(GameObject copy)
    {
        // 버튼 11개: 부품 번호 0 ~ 8 = 숫자 1 ~ 9, 9 = 빨강, 10 = 파랑 (CodeStation 상수와 같은 순서)
        string[] buttonNames = new string[CodeStation.BlueButton + 1];

        for (int i = 0; i < CodeRules.DigitCount; i++)
            buttonNames[i] = $"Match_code_button_{i + 1}";

        buttonNames[CodeStation.RedButton] = "Match_code_button_red";
        buttonNames[CodeStation.BlueButton] = "Match_code_button_blue";

        Transform[] buttons = new Transform[buttonNames.Length];
        List<string> missing = new List<string>();

        for (int i = 0; i < buttonNames.Length; i++)
        {
            buttons[i] = FindDeep(copy.transform, buttonNames[i]);

            if (buttons[i] == null || buttons[i].GetComponent<Renderer>() == null)
                missing.Add(buttonNames[i]);
        }

        if (missing.Count > 0)
        {
            Debug.LogError($"[LegacyMissionConverter] 코드: 모델 노드를 찾지 못했습니다 ({string.Join(", ", missing)}).");
            return false;
        }

        // 버튼이 있는 쪽을 스테이션 정면(+Z)으로 — 배치할 때 +Z 를 플레이어 쪽으로 돌리기 때문
        float frontAngle = FaceFront(copy, buttons);

        if (copy.GetComponent<NetworkObject>() == null)
            copy.AddComponent<NetworkObject>();

        CodeStation station = copy.AddComponent<CodeStation>();
        SerializedObject so = ConfigureStation(station, MissionEventType.SecurityCodeEntered, CompletionPolicy.Lock, CodeInteractRange);

        float pressDepth = 0f;

        for (int i = 0; i < buttons.Length; i++)
            pressDepth = SetupPressButton(so, station, buttons[i], i);

        // 불빛: 숫자 1 ~ 9 렌더러 + 원본 네온 재질 3종 (C9)
        Material litMaterial = AssetDatabase.LoadAssetAtPath<Material>($"{SourceMaterialFolder}/Neon_blue.mat");
        Material failMaterial = AssetDatabase.LoadAssetAtPath<Material>($"{SourceMaterialFolder}/Neon_red.mat");
        Material doneMaterial = AssetDatabase.LoadAssetAtPath<Material>($"{SourceMaterialFolder}/Neon_green.mat");

        if (litMaterial == null || failMaterial == null || doneMaterial == null)
            Debug.LogWarning("[LegacyMissionConverter] 코드: Neon_blue / Neon_red / Neon_green 재질을 찾지 못해 일부 불빛이 보이지 않습니다.");

        CodeKeypadVisual keypad = copy.AddComponent<CodeKeypadVisual>();
        SerializedObject keypadSO = new SerializedObject(keypad);
        SerializedProperty renderers = keypadSO.FindProperty("digitRenderers");
        renderers.ClearArray();

        for (int i = 0; i < CodeRules.DigitCount; i++)
        {
            renderers.InsertArrayElementAtIndex(i);
            renderers.GetArrayElementAtIndex(i).objectReferenceValue = buttons[i].GetComponent<Renderer>();
        }

        keypadSO.FindProperty("litMaterial").objectReferenceValue = litMaterial;
        keypadSO.FindProperty("failMaterial").objectReferenceValue = failMaterial;
        keypadSO.FindProperty("doneMaterial").objectReferenceValue = doneMaterial;
        keypadSO.ApplyModifiedPropertiesWithoutUndo();

        so.FindProperty("keypad").objectReferenceValue = keypad;
        so.FindProperty("display").objectReferenceValue = BuildCodeDisplay(copy);
        so.ApplyModifiedPropertiesWithoutUndo();

        Debug.Log($"[LegacyMissionConverter] 코드: 버튼 {buttons.Length}개 연결, 정면 맞춤 회전 {frontAngle:0}°, 눌림 {pressDepth:0.000}m");
        return true;
    }

    /// <summary>
    /// 코드 자판 화면 글자 (3a 명세 C13): 패널 화면 위에 LCD 글자(LcdDisplay)를 올린다.
    /// 화면을 못 찾으면 경고 후 null — 스테이션은 화면 없이 동작한다.
    /// </summary>
    private static LcdDisplay BuildCodeDisplay(GameObject root)
    {
        MeshFilter panel = root.GetComponent<MeshFilter>();

        if (panel == null || panel.sharedMesh == null ||
            !TryFindCodeScreen(panel, root.transform, out Vector3 center, out Vector3 normal, out Vector3 up, out Vector2 size))
        {
            Debug.LogWarning("[LegacyMissionConverter] 코드: 패널에서 화면(아틀라스 검은 칸 · 앞을 향한 면)을 찾지 못해 화면 글자 없이 동작합니다.");
            return null;
        }

        // 화면 3mm 앞 (화면 면과 겹쳐 깜빡이지 않게), 글자 앞면(−Z)이 화면 밖(normal)을 보게, 테두리 여백 10%
        LcdDisplay display = CreateLcdText(root.transform, center + normal * 0.003f, Quaternion.LookRotation(-normal, up), size * 0.9f,
            CodeStation.SuccessText, CodeStation.ReadyText);

        TMP_Text text = display.GetComponent<TMP_Text>();
        float tilt = Vector3.Angle(Vector3.forward, Vector3.ProjectOnPlane(normal, Vector3.right));
        Debug.Log($"[LegacyMissionConverter] 코드: 화면 찾음 — 크기 {size.x:0.00} × {size.y:0.00}m, 뒤로 기운 각도 {tilt:0}°, 글자 크기 {text.fontSize:0.00}{(text.enableAutoSizing ? " (자동 크기 유지)" : string.Empty)}");
        return display;
    }

    /// <summary>
    /// LCD 글자 (3a C13 · 3b LS16): parent 아래에 TextMeshPro 글자 + LcdDisplay 를 만든다.
    /// 크기는 가장 긴 문구(longestText)가 영역(size)에 맞도록 한 번 재고 고정한다 (문구마다 글자 크기가 달라지지 않게).
    /// </summary>
    /// <param name="localRotation">글자 앞면(−Z)이 보는 쪽을 정하는 회전</param>
    private static LcdDisplay CreateLcdText(Transform parent, Vector3 localPosition, Quaternion localRotation, Vector2 size,
        string longestText, string initialText)
    {
        GameObject textObject = new GameObject("Display (화면 글자)");
        textObject.transform.SetParent(parent, false);

        // TextMeshPro 를 붙이면 RectTransform 도 함께 생긴다
        TextMeshPro text = textObject.AddComponent<TextMeshPro>();
        RectTransform rect = textObject.GetComponent<RectTransform>();
        rect.localPosition = localPosition;
        rect.localRotation = localRotation;
        rect.sizeDelta = size;

        TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(TmpDefaultFontPath);

        if (font != null)
            text.font = font;
        else
            Debug.LogWarning($"[LegacyMissionConverter] 글꼴을 찾지 못해 TextMeshPro 기본값을 씁니다 ({TmpDefaultFontPath}).");

        text.alignment = TextAlignmentOptions.Center;
        text.enableWordWrapping = false;

        // 가장 긴 문구로 크기를 한 번 재고 고정
        text.enableAutoSizing = true;
        text.fontSizeMin = 0.01f;
        text.fontSizeMax = 100f;
        text.text = longestText;
        text.ForceMeshUpdate(true, true);
        float fitted = text.fontSize;

        if (fitted > 0.01f)
        {
            text.enableAutoSizing = false;
            text.fontSize = fitted;
        }

        text.text = initialText;

        LcdDisplay display = textObject.AddComponent<LcdDisplay>();
        SerializedObject displaySO = new SerializedObject(display);
        displaySO.FindProperty("text").objectReferenceValue = text;
        displaySO.ApplyModifiedPropertiesWithoutUndo();
        return display;
    }

    /// <summary>
    /// 패널 메시에서 화면 면을 찾아 루트 기준 중심 · 바깥 방향(normal) · 화면의 위쪽 · 크기(가로, 세로)를 구한다.
    /// [찾는 규칙 — FBX 분석] 화면은 아틀라스 128×128 의 완전한 검정 칸(UV ≈ 0.3865, 0.5745)을 쓰는 면들 중 앞(+Z)을 향한 면이다.
    ///  가로 약 127cm · 세로 약 51cm, 뒤로 약 18° 기운 넓은 화면. 화면 둘레의 위아래 경사면도 같은 칸을 쓰지만 앞을 향하지 않아 걸러진다.
    /// </summary>
    private static bool TryFindCodeScreen(MeshFilter panel, Transform root, out Vector3 center, out Vector3 normal, out Vector3 up, out Vector2 size)
    {
        center = normal = up = Vector3.zero;
        size = Vector2.zero;

        Mesh mesh = panel.sharedMesh;
        Vector3[] vertices = mesh.vertices;
        Vector2[] uvs = mesh.uv;
        int[] triangles = mesh.triangles;

        if (uvs.Length != vertices.Length)
            return false;

        List<Vector3> points = new List<Vector3>();
        Vector3 normalSum = Vector3.zero;

        for (int i = 0; i + 2 < triangles.Length; i += 3)
        {
            int a = triangles[i], b = triangles[i + 1], c = triangles[i + 2];
            Vector2 uvCenter = (uvs[a] + uvs[b] + uvs[c]) / 3f;

            if (Mathf.Abs(uvCenter.x - CodeScreenAtlasUv.x) > CodeScreenUvTolerance ||
                Mathf.Abs(uvCenter.y - CodeScreenAtlasUv.y) > CodeScreenUvTolerance)
                continue;

            Vector3 pa = root.InverseTransformPoint(panel.transform.TransformPoint(vertices[a]));
            Vector3 pb = root.InverseTransformPoint(panel.transform.TransformPoint(vertices[b]));
            Vector3 pc = root.InverseTransformPoint(panel.transform.TransformPoint(vertices[c]));

            // Unity 메시의 앞면 방향 = Cross(b − a, c − a). 길이는 넓이의 두 배
            Vector3 cross = Vector3.Cross(pb - pa, pc - pa);
            float doubleArea = cross.magnitude;

            if (doubleArea < 1e-8f)
                continue;

            Vector3 faceNormal = cross / doubleArea;

            if (Vector3.Dot(faceNormal, Vector3.forward) < 0.8f)
                continue;

            normalSum += cross;
            points.Add(pa);
            points.Add(pb);
            points.Add(pc);
        }

        if (points.Count == 0)
            return false;

        normal = normalSum.normalized;
        up = Vector3.ProjectOnPlane(Vector3.up, normal).normalized;
        Vector3 right = Vector3.Cross(up, normal).normalized;

        float minR = float.MaxValue, maxR = float.MinValue, minU = float.MaxValue, maxU = float.MinValue, depth = 0f;

        foreach (Vector3 p in points)
        {
            float r = Vector3.Dot(p, right);
            float u = Vector3.Dot(p, up);
            minR = Mathf.Min(minR, r);
            maxR = Mathf.Max(maxR, r);
            minU = Mathf.Min(minU, u);
            maxU = Mathf.Max(maxU, u);
            depth += Vector3.Dot(p, normal);
        }

        depth /= points.Count;
        center = right * (minR + maxR) * 0.5f + up * (minU + maxU) * 0.5f + normal * depth;
        size = new Vector2(maxR - minR, maxU - minU);
        return size.x > 0.01f && size.y > 0.01f;
    }

    // ═════════════════════════════════════════════════════════════
    //  3b: 생명 유지 장치 · 산소통 (신규 — 모델이 없어 기본 도형으로 조립)
    // ═════════════════════════════════════════════════════════════

    /// <summary>산소통 1인칭 모델: 화면 오른쪽 아래에 보이는 작은 빨간 원기둥 (콜라이더 없음). 위치는 에디터 확인 후 조정.</summary>
    private static bool BuildOxygenTankFirstPerson(GameObject copy)
    {
        Material body = GetOrCreateColorMaterial("LifeSupport_TankBody", OxygenTankColor);
        CreateShape(PrimitiveType.Cylinder, "Body (몸통)", copy.transform, new Vector3(0.25f, -0.25f, 0.55f), new Vector3(0.12f, 0.18f, 0.12f), body, false);
        return true;
    }

    /// <summary>산소통 아이템 데이터 (팀원 ItemData): 번호 20 · 이름 · 1인칭 모델 (3b).</summary>
    private static ItemData CreateOxygenTankData(GameObject firstPersonPrefab)
    {
        return CreateItemData(OxygenTankDataPath, OxygenTankItemId, "산소통", firstPersonPrefab);
    }

    /// <summary>
    /// 미션 아이템 데이터 (팀원 ItemData — 3b 산소통 · 3c 장비 부품): 번호 · 이름 · 1인칭 모델. 이미 있으면 값만 갱신한다.
    /// 다른 아이템 데이터가 같은 번호를 쓰면 경고한다.
    /// </summary>
    private static ItemData CreateItemData(string path, int id, string itemName, GameObject firstPersonPrefab)
    {
        EnsureFolder(System.IO.Path.GetDirectoryName(path).Replace('\\', '/'));

        ItemData data = AssetDatabase.LoadAssetAtPath<ItemData>(path);

        if (data == null)
        {
            data = ScriptableObject.CreateInstance<ItemData>();
            AssetDatabase.CreateAsset(data, path);
        }

        SerializedObject so = new SerializedObject(data);
        so.FindProperty("id").intValue = id;
        so.FindProperty("itemName").stringValue = itemName;
        so.FindProperty("firstPersonPrefab").objectReferenceValue = firstPersonPrefab;
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(data);

        foreach (string guid in AssetDatabase.FindAssets("t:ItemData"))
        {
            string otherPath = AssetDatabase.GUIDToAssetPath(guid);
            ItemData other = otherPath != path ? AssetDatabase.LoadAssetAtPath<ItemData>(otherPath) : null;

            if (other != null && other.Id == id)
                Debug.LogWarning($"[LegacyMissionConverter] {itemName}: 아이템 번호 {id} 를 {otherPath} 도 쓰고 있습니다.");
        }

        return data;
    }

    /// <summary>
    /// 산소통 (3b 명세 4장): 빨간 원기둥 몸통(바닥이 루트 원점) + 압력계 구. 루트에 NetworkObject · ItemWorldView · OxygenTank.
    /// 압력계는 원래 재질이 회색이고 가까이 가면 Neon_green / Neon_red 로 바뀐다 (LS1).
    /// </summary>
    private static bool BuildOxygenTank(GameObject copy, ItemData data)
    {
        Material safe = AssetDatabase.LoadAssetAtPath<Material>($"{SourceMaterialFolder}/Neon_green.mat");
        Material danger = AssetDatabase.LoadAssetAtPath<Material>($"{SourceMaterialFolder}/Neon_red.mat");

        if (safe == null || danger == null)
        {
            Debug.LogError("[LegacyMissionConverter] 산소통: Neon_green / Neon_red 재질이 없어 압력계로 정상 · 위험을 구분할 수 없습니다.");
            return false;
        }

        copy.layer = InteractableLayer;
        copy.AddComponent<NetworkObject>();

        // 기본 원기둥은 지름 1 · 높이 2 → 크기 (지름, 높이 / 2, 지름). 조준 · 줍기용 콜라이더는 그대로 둔다
        GameObject body = CreateShape(PrimitiveType.Cylinder, "Body (몸통)", copy.transform,
            new Vector3(0f, OxygenTankHeight * 0.5f, 0f), new Vector3(OxygenTankDiameter, OxygenTankHeight * 0.5f, OxygenTankDiameter),
            GetOrCreateColorMaterial("LifeSupport_TankBody", OxygenTankColor), true);
        body.layer = InteractableLayer;

        GameObject gauge = CreateShape(PrimitiveType.Sphere, "Gauge (압력계)", copy.transform,
            new Vector3(0f, OxygenTankHeight + 0.03f, 0f), Vector3.one * 0.08f,
            GetOrCreateColorMaterial("LifeSupport_Off", LampOffColor), false);

        ItemWorldView view = copy.AddComponent<ItemWorldView>();
        SerializedObject viewSO = new SerializedObject(view);
        SetObjectArray(viewSO.FindProperty("renderers"), body.GetComponent<Renderer>(), gauge.GetComponent<Renderer>());
        SetObjectArray(viewSO.FindProperty("colliders"), body.GetComponent<Collider>());
        viewSO.ApplyModifiedPropertiesWithoutUndo();

        OxygenTank tank = copy.AddComponent<OxygenTank>();
        SerializedObject so = new SerializedObject(tank);
        so.FindProperty("data").objectReferenceValue = data;
        so.FindProperty("gaugeRenderer").objectReferenceValue = gauge.GetComponent<Renderer>();
        so.FindProperty("safeMaterial").objectReferenceValue = safe;
        so.FindProperty("dangerMaterial").objectReferenceValue = danger;
        so.ApplyModifiedPropertiesWithoutUndo();

        // 조준하면 외곽선 (미션 아이템 공통 — 3c 테스트 후 추가 EA16). 몸통만 — 압력계 구는 빼서 외곽선이 압력계 색을 가리지 않게
        MissionOutlineBuilder.Attach(copy, body);

        Debug.Log($"[LegacyMissionConverter] 산소통: 아이템 번호 {OxygenTankItemId}, 지름 {OxygenTankDiameter}m · 높이 {OxygenTankHeight}m");
        return true;
    }

    /// <summary>
    /// 생명 유지 장치 본체 (3b 명세 4장): 상자 몸체(바닥이 원점, 앞 = +Z) + 앞면 화면 · LCD 글자 + 받침 · 꽂는 자리 · 꽂힌 통 + 경고 램프 + 폭발 섬광.
    /// 콜라이더 둘: 몸체(단단함, 기본 레이어 — 플레이어가 통과하지 않게) / 조준 영역(트리거, 조준 레이어 — 내 미션이 아니면 공통 틀이 끈다).
    /// </summary>
    private static bool BuildLifeSupport(GameObject copy, NetworkObject tankPrefab)
    {
        Material warning = AssetDatabase.LoadAssetAtPath<Material>($"{SourceMaterialFolder}/Neon_red.mat");
        Material done = AssetDatabase.LoadAssetAtPath<Material>($"{SourceMaterialFolder}/Neon_green.mat");

        if (warning == null || done == null)
            Debug.LogWarning("[LegacyMissionConverter] 생명 유지 장치: Neon_red / Neon_green 재질을 찾지 못해 경고 램프 · 섬광이 보이지 않습니다.");

        Material bodyMaterial = GetOrCreateColorMaterial("LifeSupport_Body", LifeSupportBodyColor);
        Transform root = copy.transform;
        copy.AddComponent<NetworkObject>();

        Vector3 size = LifeSupportBodySize;
        float front = size.z * 0.5f;
        GameObject body = CreateShape(PrimitiveType.Cube, "Body (몸체)", root, new Vector3(0f, size.y * 0.5f, 0f), size, bodyMaterial, true);

        GameObject aim = new GameObject("Aim (조준 영역)");
        aim.layer = InteractableLayer;
        aim.transform.SetParent(root, false);
        aim.transform.localPosition = body.transform.localPosition;
        BoxCollider aimCollider = aim.AddComponent<BoxCollider>();
        aimCollider.isTrigger = true;
        aimCollider.size = size + Vector3.one * 0.04f;

        // 화면: 앞면 위쪽 검은 판 + 3mm 앞 LCD 글자 (글자 앞면(−Z)이 +Z 를 보게)
        CreateShape(PrimitiveType.Cube, "Screen (화면)", root, new Vector3(0f, LifeSupportScreenHeight, front + 0.01f),
            new Vector3(LifeSupportScreenSize.x, LifeSupportScreenSize.y, 0.02f), GetOrCreateColorMaterial("LifeSupport_Screen", Color.black), false);
        LcdDisplay display = CreateLcdText(root, new Vector3(0f, LifeSupportScreenHeight, front + 0.023f), Quaternion.LookRotation(Vector3.back, Vector3.up),
            LifeSupportScreenSize * 0.9f, LifeSupportStation.LongestScreenText, LifeSupportStation.IdleText(0, 3));

        // 꽂는 자리: 앞으로 튀어나온 받침 + 짙은 원판. 꽂힌 통 = 산소통 몸통과 같은 모양 (평소 숨김)
        Vector3 shelfCenter = new Vector3(0f, LifeSupportShelfHeight, front + 0.15f);
        CreateShape(PrimitiveType.Cube, "Shelf (받침)", root, shelfCenter, new Vector3(0.45f, 0.06f, 0.3f), bodyMaterial, false);
        Vector3 socketCenter = shelfCenter + Vector3.up * 0.04f;
        CreateShape(PrimitiveType.Cylinder, "Socket (꽂는 자리)", root, socketCenter, new Vector3(0.32f, 0.01f, 0.32f),
            GetOrCreateColorMaterial("LifeSupport_Socket", SocketColor), false);
        GameObject inserted = CreateShape(PrimitiveType.Cylinder, "Inserted Tank (꽂힌 산소통)", root,
            socketCenter + Vector3.up * (OxygenTankHeight * 0.5f + 0.01f), new Vector3(OxygenTankDiameter, OxygenTankHeight * 0.5f, OxygenTankDiameter),
            GetOrCreateColorMaterial("LifeSupport_TankBody", OxygenTankColor), false);
        inserted.SetActive(false);

        GameObject lamp = CreateShape(PrimitiveType.Sphere, "Warning Lamp (경고 램프)", root, new Vector3(0f, size.y + 0.06f, 0f), Vector3.one * 0.12f,
            GetOrCreateColorMaterial("LifeSupport_Off", LampOffColor), false);

        GameObject flash = CreateShape(PrimitiveType.Sphere, "Flash (폭발 섬광)", root, new Vector3(0f, size.y * 0.5f, 0f), Vector3.one, warning, false);
        flash.SetActive(false);

        LifeSupportVisual visual = copy.AddComponent<LifeSupportVisual>();
        SerializedObject visualSO = new SerializedObject(visual);
        visualSO.FindProperty("insertedTank").objectReferenceValue = inserted;
        visualSO.FindProperty("warningLamp").objectReferenceValue = lamp.GetComponent<Renderer>();
        visualSO.FindProperty("warningMaterial").objectReferenceValue = warning;
        visualSO.FindProperty("doneMaterial").objectReferenceValue = done;
        visualSO.FindProperty("flash").objectReferenceValue = flash.transform;
        visualSO.ApplyModifiedPropertiesWithoutUndo();

        LifeSupportStation station = copy.AddComponent<LifeSupportStation>();
        SerializedObject so = ConfigureStation(station, MissionEventType.LifeSupportRestored, CompletionPolicy.Lock, LifeSupportInteractRange);
        so.FindProperty("tankPrefab").objectReferenceValue = tankPrefab;
        so.FindProperty("visual").objectReferenceValue = visual;
        so.FindProperty("display").objectReferenceValue = display;
        AddCollider(so, aimCollider);
        so.ApplyModifiedPropertiesWithoutUndo();

        MissionOutlineBuilder.Attach(copy, body);

        Debug.Log($"[LegacyMissionConverter] 생명 유지 장치: 몸체 {size}, 화면 높이 {LifeSupportScreenHeight}m, 꽂는 자리 {socketCenter}, 산소통 프리팹 {tankPrefab.name}");
        return true;
    }

    /// <summary>기본 도형 부품을 만든다. keepCollider 가 false 면 도형의 기본 콜라이더를 지운다 (조준 · 충돌에 끼지 않게).</summary>
    private static GameObject CreateShape(PrimitiveType type, string name, Transform parent, Vector3 localPosition, Vector3 localScale,
        Material material, bool keepCollider)
    {
        GameObject shape = GameObject.CreatePrimitive(type);
        shape.name = name;

        if (!keepCollider)
            Object.DestroyImmediate(shape.GetComponent<Collider>());

        shape.transform.SetParent(parent, false);
        shape.transform.localPosition = localPosition;
        shape.transform.localScale = localScale;

        if (material != null)
            shape.GetComponent<Renderer>().sharedMaterial = material;

        return shape;
    }

    /// <summary>
    /// 단색 재질을 우리 폴더(MissionRedesign_Draft/Materials)에서 읽고, 없으면 만든다 (원본 재질은 수정하지 않는다).
    /// URP Lit 이 없으면 Standard. 색은 Material.color(셰이더의 주 색)와 _BaseColor 둘 다 넣는다.
    /// </summary>
    private static Material GetOrCreateColorMaterial(string fileName, Color color)
    {
        string path = $"{DraftMaterialFolder}/{fileName}.mat";
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);

        if (material != null)
            return material;

        Shader shader = Shader.Find("Universal Render Pipeline/Lit");

        if (shader == null)
            shader = Shader.Find("Standard");

        material = new Material(shader) { name = fileName };
        material.color = color;

        if (material.HasProperty("_BaseColor"))
            material.SetColor("_BaseColor", color);

        EnsureFolder(DraftMaterialFolder);
        AssetDatabase.CreateAsset(material, path);
        return material;
    }

    /// <summary>직렬화된 배열(SerializedProperty)을 items 로 채운다.</summary>
    private static void SetObjectArray(SerializedProperty list, params Object[] items)
    {
        list.ClearArray();

        for (int i = 0; i < items.Length; i++)
        {
            list.InsertArrayElementAtIndex(i);
            list.GetArrayElementAtIndex(i).objectReferenceValue = items[i];
        }
    }

    // ═════════════════════════════════════════════════════════════
    //  3c: 고장난 장비 조립 (신규 — 원본 본체 · 부품 모델에서 조립)
    // ═════════════════════════════════════════════════════════════

    /// <summary>원본 부품 모델(색)을 읽는다. 없으면 오류.</summary>
    private static GameObject LoadEquipmentPartModel(int color)
    {
        string path = $"{SourceModelFolder}/Equipment_repair_machine_parts_{EquipmentPartColors[color]}.fbx";
        GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(path);

        if (model == null)
            Debug.LogError($"[LegacyMissionConverter] 장비 부품 모델이 없습니다: {path}");

        return model;
    }

    /// <summary>부품 1인칭 모델: 빈 루트 + 부품 모델 사본(크기 0.6, 화면 오른쪽 아래). 콜라이더 없음. 위치는 에디터 확인 후 조정.</summary>
    private static bool BuildEquipmentPartFirstPerson(GameObject copy, int color)
    {
        GameObject model = LoadEquipmentPartModel(color);

        if (model == null)
            return false;

        GameObject view = Object.Instantiate(model, copy.transform);
        view.name = "Model (모델)";
        view.transform.localPosition = new Vector3(0.25f, -0.25f, 0.55f);
        view.transform.localRotation = Quaternion.identity;
        view.transform.localScale = Vector3.one * 0.6f;
        return true;
    }

    /// <summary>
    /// 부품 아이템 (3c 명세 4장): 빈 루트(바닥 원점) + 부품 모델 사본을 반 높이만큼 위로 (EA11).
    /// 루트 · 모델을 조준 레이어로, 모델 메시 크기 BoxCollider, 루트에 NetworkObject · ItemWorldView · EquipmentPart.
    /// </summary>
    private static bool BuildEquipmentPart(GameObject copy, int color, ItemData data)
    {
        GameObject model = LoadEquipmentPartModel(color);

        if (model == null)
            return false;

        copy.layer = InteractableLayer;
        copy.AddComponent<NetworkObject>();

        GameObject view = Object.Instantiate(model, copy.transform);
        view.name = "Model (모델)";
        view.transform.localRotation = Quaternion.identity;

        MeshFilter meshFilter = view.GetComponentInChildren<MeshFilter>();
        Renderer renderer = view.GetComponentInChildren<Renderer>();

        if (meshFilter == null || meshFilter.sharedMesh == null || renderer == null)
        {
            Debug.LogError($"[LegacyMissionConverter] 부품 ({EquipmentPartKoreanNames[color]}): 모델에 메시가 없습니다.");
            return false;
        }

        // 원점이 부품 중심이라 그대로 두면 반쯤 묻힌다 → 메시 아래쪽이 루트 원점(바닥)에 오게 올린다 (EA11, 메시는 모델 루트에 있다)
        Bounds bounds = meshFilter.sharedMesh.bounds;
        view.transform.localPosition = Vector3.up * -bounds.min.y;

        GameObject colliderObject = meshFilter.gameObject;
        colliderObject.layer = InteractableLayer;
        BoxCollider box = colliderObject.AddComponent<BoxCollider>();
        box.center = bounds.center;
        box.size = bounds.size;

        ItemWorldView worldView = copy.AddComponent<ItemWorldView>();
        SerializedObject viewSO = new SerializedObject(worldView);
        SetObjectArray(viewSO.FindProperty("renderers"), renderer);
        SetObjectArray(viewSO.FindProperty("colliders"), box);
        viewSO.ApplyModifiedPropertiesWithoutUndo();

        EquipmentPart part = copy.AddComponent<EquipmentPart>();
        SerializedObject so = new SerializedObject(part);
        so.FindProperty("data").objectReferenceValue = data;
        so.FindProperty("colorIndex").intValue = color;
        so.ApplyModifiedPropertiesWithoutUndo();

        // 조준하면 외곽선 (미션 아이템 공통 — 3c 테스트 후 추가 EA16). 외곽선 복제본은 모델 자식이라 아이템과 같이 움직이고,
        // 들고 있으면 팀원 아이템 코드가 콜라이더를 꺼서 조준되지 않으므로 나타나지 않는다
        MissionOutlineBuilder.Attach(copy, view);

        Debug.Log($"[LegacyMissionConverter] 부품 ({EquipmentPartKoreanNames[color]}): 아이템 번호 {EquipmentPartFirstItemId + color}, 크기 {bounds.size}, 바닥 위로 {-bounds.min.y:0.000}m");
        return true;
    }

    /// <summary>
    /// 장비 (3c 명세 4장): 본체 복사본에 자리 4개(부품 모델 사본, 숨김 — EA9) · 몸체 충돌 상자 · 조립 위치 원 · 날아오기 시작점 · 장치 · 연출.
    /// 정면 맞춤은 하지 않는다 — 자리 노드가 +Z(앞) 쪽에 있다 (명세 1장). 뒤쪽이면 경고한다.
    /// </summary>
    private static bool BuildAssembly(GameObject copy, NetworkObject[] partPrefabs)
    {
        Transform root = copy.transform;
        Transform[] slots = new Transform[AssemblyRules.PartCount];
        List<string> missing = new List<string>();

        for (int c = 0; c < slots.Length; c++)
        {
            string slotName = $"Parts_{EquipmentPartColors[c]}_axes";
            slots[c] = FindDeep(root, slotName);

            if (slots[c] == null)
                missing.Add(slotName);
        }

        if (missing.Count > 0)
        {
            Debug.LogError($"[LegacyMissionConverter] 장비: 자리 노드를 찾지 못했습니다 ({string.Join(", ", missing)}).");
            return false;
        }

        // 몸체 메시는 부품 사본을 붙이기 전에 찾는다 (사본도 메시를 가져서)
        MeshFilter body = root.GetComponentInChildren<MeshFilter>();

        if (body == null || body.sharedMesh == null)
        {
            Debug.LogError("[LegacyMissionConverter] 장비: 본체 메시를 찾지 못했습니다.");
            return false;
        }

        // 몸체 충돌 상자 (기본 레이어 — 플레이어가 통과하지 않게, 조준 대상 아님 — EA14)
        BoxCollider bodyCollider = body.gameObject.AddComponent<BoxCollider>();
        bodyCollider.center = body.sharedMesh.bounds.center;
        bodyCollider.size = body.sharedMesh.bounds.size;

        // 앞면 · 중심 (루트 기준): 복사본은 원점 · 회전 0 이라 몸체 렌더러 경계(월드)를 루트 기준으로 바로 쓴다
        Bounds bodyBounds = body.GetComponent<Renderer>().bounds;
        float front = root.InverseTransformPoint(bodyBounds.center + Vector3.forward * bodyBounds.extents.z).z;
        float centerZ = root.InverseTransformPoint(bodyBounds.center).z;
        float slotZ = 0f;

        foreach (Transform slot in slots)
            slotZ += root.InverseTransformPoint(slot.position).z / slots.Length;

        if (slotZ <= centerZ)
            Debug.LogWarning($"[LegacyMissionConverter] 장비: 자리 노드(z {slotZ:0.00})가 몸체 중심(z {centerZ:0.00})보다 뒤에 있습니다 — 조립 위치가 반대편일 수 있습니다.");

        // 자리마다 그 색 부품 모델 사본 (로컬 0, 숨김 — 붙으면 보임, EA9)
        Transform[] attached = new Transform[slots.Length];

        for (int c = 0; c < slots.Length; c++)
        {
            GameObject model = LoadEquipmentPartModel(c);

            if (model == null)
                return false;

            GameObject attachedPart = Object.Instantiate(model, slots[c]);
            attachedPart.name = $"Attached ({EquipmentPartKoreanNames[c]})";
            attachedPart.transform.localPosition = Vector3.zero;
            attachedPart.transform.localRotation = Quaternion.identity;
            attachedPart.transform.localScale = Vector3.one;
            attachedPart.SetActive(false);
            attached[c] = attachedPart.transform;
        }

        // 조립 위치: 앞면 + 2.5m 앞 바닥 원판 (지름 5m · 두께 2cm, 콜라이더 없음) + 그 1.2m 위 날아오기 시작점
        GameObject zone = new GameObject("Zone (조립 위치)");
        zone.transform.SetParent(root, false);
        zone.transform.localPosition = new Vector3(0f, 0f, front + AssemblyZoneGap);
        CreateShape(PrimitiveType.Cylinder, "Ring (바닥 원)", zone.transform, new Vector3(0f, 0.01f, 0f),
            new Vector3(AssemblyZoneRadius * 2f, 0.01f, AssemblyZoneRadius * 2f), GetOrCreateColorMaterial("Assembly_Zone", AssemblyZoneColor), false);

        GameObject flyStart = new GameObject("FlyStart (날아오기 시작)");
        flyStart.transform.SetParent(zone.transform, false);
        flyStart.transform.localPosition = Vector3.up * AssemblyFlyStartHeight;

        if (copy.GetComponent<NetworkObject>() == null)
            copy.AddComponent<NetworkObject>();

        Material done = AssetDatabase.LoadAssetAtPath<Material>($"{SourceMaterialFolder}/Neon_green.mat");

        if (done == null)
            Debug.LogWarning("[LegacyMissionConverter] 장비: Neon_green 재질을 찾지 못해 완료 발광이 보이지 않습니다.");

        AssemblyVisual visual = copy.AddComponent<AssemblyVisual>();
        SerializedObject visualSO = new SerializedObject(visual);
        SetObjectArray(visualSO.FindProperty("attachedParts"), attached);
        visualSO.FindProperty("flyStart").objectReferenceValue = flyStart.transform;
        visualSO.FindProperty("doneMaterial").objectReferenceValue = done;
        visualSO.ApplyModifiedPropertiesWithoutUndo();

        // 범위 = 원 반경 + 0.3m, 범위 기준점 = 원 중심 → 공통 범위 검사가 곧 "원 안" (EA8)
        AssemblyStation station = copy.AddComponent<AssemblyStation>();
        SerializedObject so = ConfigureStation(station, MissionEventType.EquipmentAssembled, CompletionPolicy.Lock, AssemblyZoneRadius + 0.3f);
        so.FindProperty("rangeCenter").objectReferenceValue = zone.transform;
        SetObjectArray(so.FindProperty("partPrefabs"), partPrefabs);
        so.FindProperty("zoneCenter").objectReferenceValue = zone.transform;
        so.FindProperty("zoneRadius").floatValue = AssemblyZoneRadius;
        so.FindProperty("visual").objectReferenceValue = visual;
        so.ApplyModifiedPropertiesWithoutUndo();

        Debug.Log($"[LegacyMissionConverter] 장비: 자리 4개 찾음 (평균 z {slotZ:0.00} > 몸체 중심 z {centerZ:0.00}), 앞면 z {front:0.00}, 조립 위치 {zone.transform.localPosition} 반경 {AssemblyZoneRadius}m, 부품 프리팹 4종");
        return true;
    }

    // ═════════════════════════════════════════════════════════════
    //  원본 모델 조립 도우미 (압력 · 코드가 같이 쓴다)
    // ═════════════════════════════════════════════════════════════

    /// <summary>
    /// 모델의 버튼 메시 하나를 누를 수 있는 부품으로 만든다: 조준 레이어 · 메시 크기 콜라이더 · StationButton · 스테이션 콜라이더 목록 · 외곽선.
    /// 눌림 = 버튼 아래쪽(−up)으로 메시 높이의 30% (0.08초). 압력 · 코드 버튼이 같이 쓴다.
    /// </summary>
    /// <returns>눌림 깊이(m) — 로그용</returns>
    private static float SetupPressButton(SerializedObject stationSO, MissionStation station, Transform button, int partIndex)
    {
        GameObject buttonObject = button.gameObject;
        buttonObject.layer = InteractableLayer;

        MeshFilter buttonMesh = buttonObject.GetComponent<MeshFilter>();
        BoxCollider buttonCollider = buttonObject.AddComponent<BoxCollider>();

        if (buttonMesh != null && buttonMesh.sharedMesh != null)
        {
            buttonCollider.center = buttonMesh.sharedMesh.bounds.center;
            buttonCollider.size = buttonMesh.sharedMesh.bounds.size;
        }

        StationButton stationButton = AddStationButton(buttonObject, station, partIndex);
        SerializedObject buttonSO = new SerializedObject(stationButton);
        buttonSO.FindProperty("buttonVisual").objectReferenceValue = button;

        // 눌림: 버튼의 아래쪽(-up) 으로 메시 높이의 30% (부모 로컬 단위)
        float buttonHeight = buttonMesh != null && buttonMesh.sharedMesh != null ? buttonMesh.sharedMesh.bounds.size.y * Mathf.Abs(button.lossyScale.y) : 0.02f;
        Vector3 pressWorld = -button.up * buttonHeight * 0.3f;
        buttonSO.FindProperty("pressOffset").vector3Value = button.parent != null ? button.parent.InverseTransformVector(pressWorld) : pressWorld;
        buttonSO.FindProperty("pressTime").floatValue = 0.08f;
        buttonSO.FindProperty("returnTime").floatValue = 0.08f;
        buttonSO.ApplyModifiedPropertiesWithoutUndo();

        AddCollider(stationSO, buttonCollider);
        MissionOutlineBuilder.Attach(buttonObject, buttonObject);
        return buttonHeight * 0.3f;
    }

    /// <summary>이름이 정확히 같은 자식(손자 포함)을 찾는다. 없으면 null.</summary>
    private static Transform FindDeep(Transform root, string exactName)
    {
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
        {
            if (t.name == exactName)
                return t;
        }

        return null;
    }

    /// <summary>
    /// 모델을 루트 기준으로 수평 회전해 "앞쪽 부품이 있는 쪽"이 루트의 +Z(정면)를 보게 한다.
    /// [이유] 테스트 씬 · 맵 배치는 스테이션 루트의 +Z 를 플레이어 쪽으로 돌린다. 원본 모델의 앞뒤 방향은 모델마다 달라서 조립할 때 맞춘다.
    /// [방법] 앞쪽 부품 중심 − 모델 전체 중심(수평 성분) 방향을 +Z 로 돌리는 각도만큼, 루트의 직계 자식을 루트 원점 기준으로 돌린다
    ///  (루트 자체의 회전은 배치할 때 덮어써지므로 자식을 돌린다).
    /// </summary>
    /// <returns>돌린 각도(도). 방향을 알 수 없거나 이미 맞으면 0</returns>
    private static float FaceFront(GameObject root, Transform[] frontParts)
    {
        Renderer[] all = root.GetComponentsInChildren<Renderer>();

        if (all.Length == 0 || frontParts.Length == 0)
            return 0f;

        Bounds bounds = all[0].bounds;

        for (int i = 1; i < all.Length; i++)
            bounds.Encapsulate(all[i].bounds);

        Vector3 front = Vector3.zero;

        foreach (Transform part in frontParts)
            front += part.GetComponent<Renderer>().bounds.center;

        Vector3 direction = front / frontParts.Length - bounds.center;
        direction.y = 0f;

        if (direction.sqrMagnitude < 1e-6f)
            return 0f;

        float angle = Vector3.SignedAngle(direction, Vector3.forward, Vector3.up);

        if (Mathf.Abs(angle) < 1f)
            return 0f;

        foreach (Transform child in root.transform)
            child.RotateAround(root.transform.position, Vector3.up, angle);

        return angle;
    }

    /// <summary>
    /// 이름을 영문 · 숫자 토큰으로 나눠 찾는다: 토큰에 letter(a/b/c)가 정확히 있고, required 는 모두 포함(부분 일치), forbidden 은 하나도 없어야 한다.
    /// 원본 모델 노드 이름의 오타 · 공백(Pressure_button_A._nteraction, "Pressure _gauge_indicator_needle_A")을 견디기 위해서다.
    /// </summary>
    private static Transform FindByTokens(Transform root, string letter, string[] required, string[] forbidden, bool requireRenderer)
    {
        string letterToken = letter.ToLowerInvariant();

        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
        {
            string[] tokens = System.Text.RegularExpressions.Regex.Split(t.name.ToLowerInvariant(), "[^a-z0-9]+");

            if (System.Array.IndexOf(tokens, letterToken) < 0)
                continue;

            if (requireRenderer && t.GetComponent<Renderer>() == null)
                continue;

            bool ok = true;

            foreach (string word in required)
            {
                if (!System.Array.Exists(tokens, token => token.Contains(word)))
                {
                    ok = false;
                    break;
                }
            }

            if (ok && forbidden != null)
            {
                foreach (string word in forbidden)
                {
                    if (System.Array.Exists(tokens, token => token.Contains(word)))
                    {
                        ok = false;
                        break;
                    }
                }
            }

            if (ok)
                return t;
        }

        return null;
    }

    /// <summary>메시의 가장 얇은 축 (원판 모양 압력계의 앞면 방향 = 바늘 회전축). 로컬 단위 벡터.</summary>
    private static Vector3 ThinnestAxis(Transform meshObject)
    {
        MeshFilter meshFilter = meshObject.GetComponent<MeshFilter>();

        if (meshFilter == null || meshFilter.sharedMesh == null)
            return Vector3.forward;

        Vector3 size = Vector3.Scale(meshFilter.sharedMesh.bounds.size, meshObject.lossyScale);
        size = new Vector3(Mathf.Abs(size.x), Mathf.Abs(size.y), Mathf.Abs(size.z));

        if (size.x <= size.y && size.x <= size.z)
            return Vector3.right;

        return size.y <= size.z ? Vector3.up : Vector3.forward;
    }

    private static string Found(Transform t) => t != null ? t.name : "없음";

    // ═════════════════════════════════════════════════════════════
    //  부품 · 콜라이더 · 외곽선
    // ═════════════════════════════════════════════════════════════

    private static StationButton AddStationButton(GameObject target, MissionStation station, int partIndex)
    {
        StationButton button = target.AddComponent<StationButton>();
        SerializedObject so = new SerializedObject(button);
        so.FindProperty("station").objectReferenceValue = station;
        so.FindProperty("partIndex").intValue = partIndex;
        so.ApplyModifiedPropertiesWithoutUndo();
        return button;
    }

    /// <summary>interactionColliders 에 없으면 추가한다.</summary>
    private static void AddCollider(SerializedObject stationSO, Collider collider)
    {
        if (collider == null)
            return;

        SerializedProperty list = stationSO.FindProperty("interactionColliders");

        for (int i = 0; i < list.arraySize; i++)
        {
            if (list.GetArrayElementAtIndex(i).objectReferenceValue == collider)
                return;
        }

        list.InsertArrayElementAtIndex(list.arraySize);
        list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = collider;
    }

    /// <summary>루트에 외곽선 오브젝트가 연결된 MissionPrompt 가 이미 있는가.</summary>
    private static bool HasHighlight(GameObject root)
    {
        MissionPrompt prompt = root.GetComponent<MissionPrompt>();

        if (prompt == null)
            return false;

        return new SerializedObject(prompt).FindProperty("highlightObject").objectReferenceValue != null;
    }

    /// <summary>조준 문구 오브젝트(MissionPrompt.promptObject)마다 PromptBillboard 를 붙인다 (이미 있으면 그대로).</summary>
    private static void AddPromptBillboards(GameObject root)
    {
        foreach (MissionPrompt prompt in root.GetComponentsInChildren<MissionPrompt>(true))
        {
            GameObject promptObject = new SerializedObject(prompt).FindProperty("promptObject").objectReferenceValue as GameObject;

            if (promptObject != null && promptObject.GetComponent<PromptBillboard>() == null)
                promptObject.AddComponent<PromptBillboard>();
        }
    }

    /// <summary>
    /// 부품 자신에게 메시가 있으면 그 메시로 부품 전용 외곽선을 붙인다 (조준한 부품만 켜짐, 2a P7).
    /// 자식 메시는 쓰지 않는다 — 전선처럼 자식에 숨겨진 메시가 있으면 엉뚱한 외곽선이 된다.
    /// 메시가 없으면 외곽선 없이 둔다 (빈 프롬프트 — 부모 스테이션 외곽선이 대신 켜지지 않게).
    /// </summary>
    private static void AttachPartOutline(GameObject part)
    {
        MeshFilter meshFilter = part.GetComponent<MeshFilter>();

        if (meshFilter == null || meshFilter.sharedMesh == null)
        {
            Debug.LogWarning($"[LegacyMissionConverter] 부품 '{part.name}' 에 메시가 없어 외곽선 없이 둡니다.");
            MissionOutlineBuilder.AttachEmpty(part);
            return;
        }

        MissionOutlineBuilder.Attach(part, part);
    }

    // ─── 값 옮기기 (옛 값이 비어 있으면 경고만 하고 새 컴포넌트의 기본값을 둔다) ───

    private static void CopyReference(SerializedObject from, string fromName, SerializedObject to, string toName)
    {
        SerializedProperty source = from.FindProperty(fromName);

        if (source == null || source.objectReferenceValue == null)
        {
            Debug.LogWarning($"[LegacyMissionConverter] 옛 값 '{fromName}' 이(가) 비어 있습니다. 새 컴포넌트의 '{toName}' 을(를) 직접 연결하세요.");
            return;
        }

        to.FindProperty(toName).objectReferenceValue = source.objectReferenceValue;
    }

    private static void CopyFloat(SerializedObject from, string fromName, SerializedObject to, string toName)
    {
        SerializedProperty source = from.FindProperty(fromName);

        if (source == null)
        {
            Debug.LogWarning($"[LegacyMissionConverter] 옛 값 '{fromName}' 이(가) 없어 기본값을 씁니다.");
            return;
        }

        to.FindProperty(toName).floatValue = source.floatValue;
    }

    private static void CopyString(SerializedObject from, string fromName, SerializedObject to, string toName)
    {
        SerializedProperty source = from.FindProperty(fromName);

        if (source == null)
        {
            Debug.LogWarning($"[LegacyMissionConverter] 옛 값 '{fromName}' 이(가) 없어 기본값을 씁니다.");
            return;
        }

        to.FindProperty(toName).stringValue = source.stringValue;
    }

    private static void CopyVector3(SerializedObject from, string fromName, SerializedObject to, string toName)
    {
        SerializedProperty source = from.FindProperty(fromName);

        if (source == null)
        {
            Debug.LogWarning($"[LegacyMissionConverter] 옛 값 '{fromName}' 이(가) 없어 기본값을 씁니다.");
            return;
        }

        to.FindProperty(toName).vector3Value = source.vector3Value;
    }

    private static void CopyInt(SerializedObject from, string fromName, SerializedObject to, string toName)
    {
        SerializedProperty source = from.FindProperty(fromName);

        if (source == null)
        {
            Debug.LogWarning($"[LegacyMissionConverter] 옛 값 '{fromName}' 이(가) 없어 기본값을 씁니다.");
            return;
        }

        // enum 도 intValue 로 옮긴다 (값이 같은 enum 끼리 — 예: WireMeshAxis → WireAxis)
        to.FindProperty(toName).intValue = source.intValue;
    }

    /// <summary>옛 interactionColliders 를 옮기고, 비어 있으면 fallbackRoot 아래 콜라이더 전부를 쓴다.</summary>
    private static void CopyColliders(SerializedObject from, SerializedObject to, GameObject fallbackRoot)
    {
        List<Object> colliders = new List<Object>();
        SerializedProperty oldList = from.FindProperty("interactionColliders");

        if (oldList != null && oldList.isArray)
        {
            for (int i = 0; i < oldList.arraySize; i++)
            {
                Object c = oldList.GetArrayElementAtIndex(i).objectReferenceValue;

                if (c != null)
                    colliders.Add(c);
            }
        }

        if (colliders.Count == 0)
        {
            foreach (Collider c in fallbackRoot.GetComponentsInChildren<Collider>(true))
                colliders.Add(c);
        }

        if (colliders.Count == 0)
            Debug.LogWarning("[LegacyMissionConverter] 상호작용 콜라이더가 없습니다. 조준할 수 없습니다.");

        SerializedProperty newList = to.FindProperty("interactionColliders");
        newList.ClearArray();

        for (int i = 0; i < colliders.Count; i++)
        {
            newList.InsertArrayElementAtIndex(i);
            newList.GetArrayElementAtIndex(i).objectReferenceValue = colliders[i];
        }
    }

    /// <summary>스테이션의 첫 상호작용 콜라이더가 있는 오브젝트 (= 부품을 붙일 곳). 루트라면 스테이션에 가려지므로 null.</summary>
    private static GameObject FindPartObject(SerializedObject stationSO, GameObject root)
    {
        SerializedProperty colliders = stationSO.FindProperty("interactionColliders");

        if (colliders == null || colliders.arraySize == 0)
            return null;

        Collider first = colliders.GetArrayElementAtIndex(0).objectReferenceValue as Collider;

        if (first == null)
            return null;

        if (first.gameObject == root)
        {
            Debug.LogWarning("[LegacyMissionConverter] 버튼 콜라이더가 루트에 있어 부품이 스테이션에 가려집니다. 콜라이더를 버튼 오브젝트로 옮겨 주세요.");
            return null;
        }

        return first.gameObject;
    }

    /// <summary>
    /// 각 콜라이더를 조준했을 때 실제로 무엇이 잡히는지 PlayerTargetDetector 와 같은 규칙으로 확인한다:
    /// 콜라이더에서 위로 올라가며 "처음 만나는 ITargetable" 이 조준 대상이다.
    /// 그 대상이 클릭 · F 홀드 · 드래그를 받지 못하고 놓는 곳도 아니면, 조준은 되는데 아무 입력도 안 먹는 상태가 된다.
    /// (1단계에서 실제로 겪은 문제: 루트의 GeneratorStation 이 버튼보다 먼저 잡혀 클릭이 무시됨)
    /// </summary>
    private static bool VerifyTargetResolution(GameObject root)
    {
        bool ok = true;

        foreach (Collider collider in root.GetComponentsInChildren<Collider>(true))
        {
            MonoBehaviour resolved = null;

            foreach (MonoBehaviour behaviour in collider.GetComponentsInParent<MonoBehaviour>(true))
            {
                if (behaviour is ITargetable)
                {
                    resolved = behaviour;
                    break;
                }
            }

            // 클릭 · F 홀드 · 드래그(끄는 부품) · 놓는 곳 · 아이템 우클릭 대상 중 하나면 입력을 받을 수 있다 (2b 3장, 2c F10)
            if (resolved is IInteractable || resolved is IHoldInteractable || resolved is IDragInteractable
                || resolved is StationDropTarget || resolved is IItemUseTarget)
                continue;

            string resolvedName = resolved != null ? resolved.GetType().Name : "없음";
            Debug.LogError($"[LegacyMissionConverter] '{collider.name}' 콜라이더를 조준하면 '{resolvedName}' 가 잡혀 입력을 받을 수 없습니다. 부품 위치를 확인하세요.");
            ok = false;
        }

        return ok;
    }

    private static MonoBehaviour FindLegacy(GameObject root, string typeName)
    {
        foreach (MonoBehaviour behaviour in root.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (behaviour != null && behaviour.GetType().Name == typeName)
                return behaviour;
        }

        return null;
    }

    private static List<MonoBehaviour> FindAllLegacy(GameObject root, string typeName)
    {
        List<MonoBehaviour> found = new List<MonoBehaviour>();

        foreach (MonoBehaviour behaviour in root.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (behaviour != null && behaviour.GetType().Name == typeName)
                found.Add(behaviour);
        }

        return found;
    }

    private static bool VerifyNoLegacyScripts(GameObject root)
    {
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
        {
            if (GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject) > 0)
            {
                Debug.LogError($"[LegacyMissionConverter] '{t.name}' 에 Missing Script 가 있어 저장하지 않습니다.");
                return false;
            }

            foreach (MonoBehaviour behaviour in t.GetComponents<MonoBehaviour>())
            {
                if (behaviour != null && System.Array.IndexOf(LegacyTypeNames, behaviour.GetType().Name) >= 0)
                {
                    Debug.LogError($"[LegacyMissionConverter] 옛 스크립트 {behaviour.GetType().Name} 가 남아 저장하지 않습니다.");
                    return false;
                }
            }
        }

        return true;
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path))
            return;

        string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
    }
}
