using System.Collections.Generic;
using Fusion;
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
/// [변환 목록] 1단계 발전기 / 2a 밸브 · 안테나 · 차단기 / 2b 전선. 미니게임을 이식할 때마다 Build… 와 Convert… 를 하나씩 추가한다.
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

    // 개인 미니게임 공통 거리 (2a 명세 3장). 발전기는 1단계 값 3.5 유지.
    private const float PersonalInteractRange = 3f;

    private static readonly string[] LegacyTypeNames =
    {
        "MissionMiniGameBase", "GeneratorMission", "MissionButton",
        "ValveMission", "AntennaMission", "AntennaLockButton", "BreakerMission", "BreakerLever",
        "WiringMission", "WireStartPoint", "WireEndPoint", "WireConnectionVisual", "WiringLever",
    };

    [MenuItem("SuHan/Convert Legacy Mission Prefabs")]
    public static void ConvertAllFromMenu()
    {
        Report("발전기", ConvertGenerator());
        Report("밸브", ConvertValve());
        Report("안테나", ConvertAntenna());
        Report("차단기", ConvertBreaker());
        Report("전선", ConvertWiring());
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

    // ═════════════════════════════════════════════════════════════
    //  공통 틀
    // ═════════════════════════════════════════════════════════════

    /// <param name="build">복사본을 새 구조로 바꾼다. 실패하면 오류를 남기고 false.</param>
    private static NetworkObject Convert(string what, string legacyPath, string outputName, string outputPath, System.Func<GameObject, bool> build)
    {
        EnsureFolder(OutputFolder);

        GameObject legacy = AssetDatabase.LoadAssetAtPath<GameObject>(legacyPath);

        if (legacy == null)
        {
            Debug.LogError($"[LegacyMissionConverter] 옛 {what} 프리팹이 없습니다: {legacyPath}");
            return null;
        }

        GameObject copy = Object.Instantiate(legacy);
        copy.name = outputName;

        try
        {
            if (!build(copy))
                return null;

            AddPromptBillboards(copy);

            if (!VerifyNoLegacyScripts(copy) || !VerifyTargetResolution(copy))
                return null;

            GameObject saved = PrefabUtility.SaveAsPrefabAsset(copy, outputPath);
            Debug.Log($"[LegacyMissionConverter] {what} 변환 완료: {outputPath}");
            return saved != null ? saved.GetComponent<NetworkObject>() : null;
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

            // 클릭 · F 홀드 · 드래그(끄는 부품) · 놓는 곳 중 하나면 입력을 받을 수 있다 (2b 명세 3장)
            if (resolved is IInteractable || resolved is IHoldInteractable || resolved is IDragInteractable || resolved is StationDropTarget)
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
