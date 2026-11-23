using LockdownProtocol.Networking;
using TrustNoOne.Gimmicks;
using TrustNoOne.Missions;
using Fusion;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// [에디터 전용, 1클릭 도구] 무중력 테스트 씬 만들기 — 메뉴 `SuHan/Build Zero-G Test Scene`.
/// [역할] 한 번에 세 가지를 만들거나 고친다.
///  1) 설정 에셋 `ZeroGMovementSettings.asset` — 없을 때만 만든다 (Play 중 바꾼 값을 덮어쓰지 않는다 — ZG12)
///  2) 프리팹 변형 `Spectator_ZeroG` · `Player_ZeroG` — 팀원 원본(Spectator · Player)의 변형. 변형에 내가 바꾸는 칸만 다시 쓴다:
///     팀원 이동 부품 꺼짐 · ZeroGMovement + 설정 연결 · (플레이어) 관전자 프리팹 칸 → Spectator_ZeroG (ZG17)
///  3) 씬 `ZeroGTest.unity` — 처음부터 다시: 20 × 20 m · 높이 8 m 닫힌 방(면마다 다른 색) · 기둥 3 · 상자 1 ·
///     북쪽 벽에 내 장치 3개(놓기만) · 미션 관리자 · 테스트 시작 도구(플레이어 = Player_ZeroG) · 시작 위치 방 가운데 높이 3 m (ZG20)
/// [이유] 혼자 씬 메뉴(SuHan/Setup Solo Mission Test)는 변환 도구가 팀원이 내 장치 프리팹에 넣은 미니맵 마커를 지워 쓰지 않는다
///  (verification.md 4장 43). 이 메뉴는 장치 프리팹을 다시 만들지 않고 놓기만 한다. 팀원 원본 프리팹 · 씬은 바꾸지 않는다.
/// [근거] 17a 명세 ZG7 · ZG12 · ZG17 · ZG18 · ZG20 · ZG21
/// </summary>
public static class ZeroGTestSceneBuilder
{
    private const string DraftFolder = "Assets/0. Member/SuHan/MissionRedesign_Draft";
    private const string PrefabFolder = DraftFolder + "/Prefabs/ZeroG";
    private const string MaterialFolder = DraftFolder + "/Materials/ZeroG";
    private const string StationFolder = DraftFolder + "/Prefabs/Missions";
    private const string SettingsPath = PrefabFolder + "/ZeroGMovementSettings.asset";
    private const string PlayerVariantPath = PrefabFolder + "/Player_ZeroG.prefab";
    private const string SpectatorVariantPath = PrefabFolder + "/Spectator_ZeroG.prefab";
    private const string MissionManagerPrefabPath = "Assets/0. Member/SuHan/MissionTest/Prefabs/MissionManager.prefab";

    // 팀원 원본 프리팹 (이름이 바뀌어도 찾게 GUID 로 — 혼자 씬 메뉴의 Player GUID 와 같다)
    private const string PlayerPrefabGuid = "36eaf6db234c47649a9aad19fe7fb7cd";      // Assets/Prefabs/Player/Player.prefab
    private const string SpectatorPrefabGuid = "6fc0776cb3c4b6343b7114a25e6892b8";   // Assets/Prefabs/Player/Spectator.prefab

    // 방 크기 (ZG20) — 안쪽 20 × 20 m, 높이 8 m, 면 두께 0.5 m
    private const float RoomHalf = 10f;
    private const float RoomHeight = 8f;
    private const float Thickness = 0.5f;

    // 밸브 모델은 피벗이 손잡이 중심이라 벽에 달린 높이로 띄운다 (혼자 씬 메뉴의 ValveMountHeight 와 같은 값)
    private const float ValveMountHeight = 1.2f;

    [MenuItem("SuHan/Build Zero-G Test Scene")]
    public static void Run()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            return;

        EnsureFolder(DraftFolder + "/Prefabs", "ZeroG");
        EnsureFolder(DraftFolder, "Materials");
        EnsureFolder(DraftFolder + "/Materials", "ZeroG");

        ZeroGMovementSettings settings = EnsureSettings();
        NetworkObject spectator = EnsureVariant(SpectatorPrefabGuid, SpectatorVariantPath, "관전자", settings, null);
        NetworkObject player = spectator != null ? EnsureVariant(PlayerPrefabGuid, PlayerVariantPath, "플레이어", settings, spectator) : null;

        if (player == null)
        {
            Debug.LogError("[ZeroGTestSceneBuilder] 프리팹 변형을 만들지 못해 씬을 만들지 않았습니다 (위의 오류 확인).");
            return;
        }

        AssetDatabase.SaveAssets();

        // 새 네트워크 프리팹(변형 2개)을 Fusion 네트워크 프리팹 목록에 바로 반영 (안 하면 Runner.Spawn 이 실패 — 혼자 씬 메뉴와 같은 방법)
        Fusion.Editor.NetworkProjectConfigUtilities.RebuildPrefabTable();

        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        int stations = BuildScene(player);

        EditorSceneManager.MarkSceneDirty(scene);
        // 저장할 때 Fusion 이 씬 오브젝트의 정렬 키를 굽는다 (호스트 · 손님이 같은 오브젝트로 짝을 맞추는 번호)
        EditorSceneManager.SaveScene(scene, SuhanTestSceneManager.ZeroGScenePath);

        Debug.Log($"[ZeroGTestSceneBuilder] 완료 — 씬 {SuhanTestSceneManager.ZeroGScenePath} · 장치 {stations}개 · 변형 2개 · 설정 {SettingsPath}. " +
                  "Play → [혼자] / [호스트] / [손님]. 2인 테스트 전에는 메뉴 SuHan/Build Zero-G Test (Windows) 로 다시 빌드하세요.");
    }

    // ═════════════════════════════════════════════════════════════
    //  설정 에셋 · 프리팹 변형
    // ═════════════════════════════════════════════════════════════

    private static ZeroGMovementSettings EnsureSettings()
    {
        ZeroGMovementSettings settings = AssetDatabase.LoadAssetAtPath<ZeroGMovementSettings>(SettingsPath);

        if (settings != null)
            return settings;   // 이미 있으면 값을 그대로 둔다 (Play 중 맞춘 숫자 — ZG12)

        settings = ScriptableObject.CreateInstance<ZeroGMovementSettings>();
        AssetDatabase.CreateAsset(settings, SettingsPath);
        Debug.Log($"[ZeroGTestSceneBuilder] 설정 에셋을 만들었습니다 (기본값 2 · 0.5 · 3.5 · 1 · 3 · 10): {SettingsPath}");
        return settings;
    }

    /// <summary>
    /// 팀원 원본의 변형을 만들거나(없을 때) 열어서, 내가 바꾸는 칸만 다시 쓴다 (ZG17).
    /// 변형에 남는 것은 "원본과 다른 칸"뿐이라 팀원이 원본을 고치면 변형도 따라간다.
    /// </summary>
    private static NetworkObject EnsureVariant(string sourceGuid, string variantPath, string what,
        ZeroGMovementSettings settings, NetworkObject spectatorVariant)
    {
        GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(sourceGuid));

        if (source == null)
        {
            Debug.LogError($"[ZeroGTestSceneBuilder] 팀원 {what} 프리팹을 찾을 수 없습니다 (guid: {sourceGuid}).");
            return null;
        }

        if (AssetDatabase.LoadAssetAtPath<GameObject>(variantPath) == null)
        {
            // 프리팹 인스턴스를 새 경로로 저장하면 원본과 연결된 변형이 된다
            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(source);
            PrefabUtility.SaveAsPrefabAsset(instance, variantPath);
            Object.DestroyImmediate(instance);
            Debug.Log($"[ZeroGTestSceneBuilder] {what} 변형을 만들었습니다: {variantPath} (원본 {AssetDatabase.GetAssetPath(source)})");
        }

        GameObject contents = PrefabUtility.LoadPrefabContents(variantPath);

        try
        {
            PlayerMovement teamMovement = contents.GetComponent<PlayerMovement>();

            if (teamMovement != null)
                teamMovement.enabled = false;   // 지우지 않고 끈다 — 다른 팀원 코드가 GetComponent 로 찾는다 (ZG8)
            else
                Debug.LogWarning($"[ZeroGTestSceneBuilder] {what} 변형에 팀원 이동 부품(PlayerMovement)이 없습니다 — 원본이 바뀌었는지 확인하세요 (위험 5).");

            ZeroGMovement zeroG = contents.GetComponent<ZeroGMovement>();

            if (zeroG == null)
                zeroG = contents.AddComponent<ZeroGMovement>();

            SerializedObject zeroGSO = new SerializedObject(zeroG);
            zeroGSO.FindProperty("settings").objectReferenceValue = settings;
            zeroGSO.ApplyModifiedPropertiesWithoutUndo();

            if (spectatorVariant != null)
            {
                SpectatorManager spectatorManager = contents.GetComponent<SpectatorManager>();

                if (spectatorManager != null)
                {
                    SerializedObject managerSO = new SerializedObject(spectatorManager);
                    managerSO.FindProperty("spectatorPrefab").objectReferenceValue = spectatorVariant;
                    managerSO.ApplyModifiedPropertiesWithoutUndo();
                }
                else
                {
                    Debug.LogWarning($"[ZeroGTestSceneBuilder] {what} 변형에 관전자 관리자(SpectatorManager)가 없어 관전자 칸을 바꾸지 못했습니다.");
                }
            }

            PrefabUtility.SaveAsPrefabAsset(contents, variantPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(contents);
        }

        GameObject saved = AssetDatabase.LoadAssetAtPath<GameObject>(variantPath);
        return saved != null ? saved.GetComponent<NetworkObject>() : null;
    }

    // ═════════════════════════════════════════════════════════════
    //  씬 구성
    // ═════════════════════════════════════════════════════════════

    private static int BuildScene(NetworkObject player)
    {
        // 방: 면마다 다른 색 — 무중력에서 어디가 위인지 헷갈리지 않게, 17b 의 뒤집힌 맵과 비교하기 쉽게 (ZG20)
        Transform room = new GameObject("ZeroGRoom").transform;
        float wallHeight = RoomHeight + Thickness * 2f;
        float span = RoomHalf * 2f + Thickness * 2f;

        CreateBox(room, "바닥 (회색)", new Vector3(0f, -Thickness * 0.5f, 0f), new Vector3(span, Thickness, span), EnsureMaterial("ZeroG_Floor", new Color(0.45f, 0.45f, 0.45f)));
        CreateBox(room, "천장 (하늘색)", new Vector3(0f, RoomHeight + Thickness * 0.5f, 0f), new Vector3(span, Thickness, span), EnsureMaterial("ZeroG_Ceiling", new Color(0.55f, 0.75f, 0.95f)));
        CreateBox(room, "북쪽 벽 (빨강 — 장치)", new Vector3(0f, RoomHeight * 0.5f, RoomHalf + Thickness * 0.5f), new Vector3(span, wallHeight, Thickness), EnsureMaterial("ZeroG_North", new Color(0.75f, 0.25f, 0.25f)));
        CreateBox(room, "남쪽 벽 (파랑)", new Vector3(0f, RoomHeight * 0.5f, -RoomHalf - Thickness * 0.5f), new Vector3(span, wallHeight, Thickness), EnsureMaterial("ZeroG_South", new Color(0.25f, 0.35f, 0.8f)));
        CreateBox(room, "동쪽 벽 (초록)", new Vector3(RoomHalf + Thickness * 0.5f, RoomHeight * 0.5f, 0f), new Vector3(Thickness, wallHeight, span), EnsureMaterial("ZeroG_East", new Color(0.3f, 0.65f, 0.3f)));
        CreateBox(room, "서쪽 벽 (노랑)", new Vector3(-RoomHalf - Thickness * 0.5f, RoomHeight * 0.5f, 0f), new Vector3(Thickness, wallHeight, span), EnsureMaterial("ZeroG_West", new Color(0.85f, 0.75f, 0.25f)));

        // 기둥 3 · 상자 1 — 벽을 따라 미끄러지기 · 모서리 지나가기 확인용 (시안 그림의 자리)
        Material structure = EnsureMaterial("ZeroG_Structure", new Color(0.3f, 0.3f, 0.32f));
        CreateBox(room, "기둥 1", new Vector3(-5f, RoomHeight * 0.5f, 3f), new Vector3(1f, RoomHeight, 1f), structure);
        CreateBox(room, "기둥 2", new Vector3(5f, RoomHeight * 0.5f, 3f), new Vector3(1f, RoomHeight, 1f), structure);
        CreateBox(room, "기둥 3", new Vector3(-5f, RoomHeight * 0.5f, -4f), new Vector3(1f, RoomHeight, 1f), structure);
        CreateBox(room, "상자", new Vector3(4.5f, 1f, -5f), new Vector3(3f, 2f, 2f), structure);

        // 조명 · 시작 전 화면용 카메라 (플레이어 카메라가 생기기 전 — 혼자 씬과 같은 구성)
        GameObject lightObject = new GameObject("Directional Light");
        Light light = lightObject.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1f;
        lightObject.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

        GameObject cameraObject = new GameObject("Main Camera");
        cameraObject.tag = "MainCamera";
        cameraObject.AddComponent<Camera>();
        cameraObject.AddComponent<AudioListener>();
        cameraObject.transform.SetPositionAndRotation(new Vector3(0f, 5f, -9f), Quaternion.Euler(15f, 0f, 0f));

        // 장치 조건 확인 (MissionStation 이 자동으로 찾아 쓴다 — 혼자 씬과 같음)
        new GameObject("PlayerHealthActorGate").AddComponent<PlayerHealthActorGate>();

        // 네트워크 오브젝트는 씬에 프리팹 인스턴스로 미리 놓는다 (6a 명세 MT7) — 실행하면 테스트 씬 관리자가 넘겨받아 등록한다
        Transform networkRoot = new GameObject("NetworkObjects (씬 배치)").transform;
        PlacePrefab(MissionManagerPrefabPath, networkRoot, "MissionManager", Vector3.zero, Quaternion.identity);

        // 북쪽 벽에 장치 3개 — 방 가운데(시작 위치)를 바라보게. 프리팹을 다시 만들지 않고 놓기만 한다 (verification.md 4장 43)
        Quaternion faceCenter = Quaternion.LookRotation(Vector3.back);
        int stations = 0;
        stations += PlacePrefab($"{StationFolder}/Code_Station.prefab", networkRoot, "Code_Station (키패드 — 누를 때마다 멈춤)", new Vector3(-5f, 0f, 8.5f), faceCenter) ? 1 : 0;
        stations += PlacePrefab($"{StationFolder}/Valve_Station.prefab", networkRoot, "Valve_Station (F 누르는 동안 멈춤)", new Vector3(0f, ValveMountHeight, 8.5f), faceCenter) ? 1 : 0;
        stations += PlacePrefab($"{StationFolder}/Pressure_Station.prefab", networkRoot, "Pressure_Station (버튼 여러 번)", new Vector3(5f, 0f, 8.5f), faceCenter) ? 1 : 0;

        // 시작 위치: 방 가운데 높이 3 m — 공중에서 시작해 바로 떠 있는지 본다. 장치 쪽(북)을 바라봄
        Transform spawnPoint = new GameObject("PlayerSpawnPoint").transform;
        spawnPoint.SetPositionAndRotation(new Vector3(0f, 3f, 0f), Quaternion.identity);

        // 테스트 시작 도구 (혼자 · F5/F6 2인) + 누르고 있기 게이지 + 무중력 테스트 글자 (ZG21)
        GameObject bootstrapObject = new GameObject("MissionTestBootstrap");
        MissionTestBootstrap bootstrap = bootstrapObject.AddComponent<MissionTestBootstrap>();
        bootstrapObject.AddComponent<MissionTestAssignmentPanel>();
        bootstrapObject.AddComponent<MissionHoldGaugeHUD>();
        bootstrapObject.AddComponent<ZeroGDebugLabel>();

        SerializedObject so = new SerializedObject(bootstrap);
        so.FindProperty("playerPrefab").objectReferenceValue = player;
        so.FindProperty("playerSpawnPoint").objectReferenceValue = spawnPoint;
        so.ApplyModifiedPropertiesWithoutUndo();

        return stations;
    }

    private static void CreateBox(Transform parent, string name, Vector3 center, Vector3 size, Material material)
    {
        GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);   // 상자 충돌체 포함 — 몸이 뚫지 않게 (기획서 5장)
        box.name = name;
        box.transform.SetParent(parent, false);
        box.transform.localPosition = center;
        box.transform.localScale = size;
        box.GetComponent<MeshRenderer>().sharedMaterial = material;
    }

    private static Material EnsureMaterial(string name, Color color)
    {
        string path = $"{MaterialFolder}/{name}.mat";
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);

        if (material == null)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            material = new Material(shader != null ? shader : Shader.Find("Standard"));
            AssetDatabase.CreateAsset(material, path);
        }

        material.SetColor("_BaseColor", color);
        material.color = color;
        EditorUtility.SetDirty(material);
        return material;
    }

    private static bool PlacePrefab(string path, Transform parent, string name, Vector3 position, Quaternion rotation)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);

        if (prefab == null)
        {
            Debug.LogError($"[ZeroGTestSceneBuilder] 프리팹이 없어 놓지 못했습니다: {path}");
            return false;
        }

        GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
        instance.name = name;
        instance.transform.SetPositionAndRotation(position, rotation);
        return true;
    }

    private static void EnsureFolder(string parent, string child)
    {
        if (!AssetDatabase.IsValidFolder($"{parent}/{child}"))
            AssetDatabase.CreateFolder(parent, child);
    }
}
