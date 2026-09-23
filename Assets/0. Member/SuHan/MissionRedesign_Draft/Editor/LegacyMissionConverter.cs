using System.Collections.Generic;
using Fusion;
using TrustNoOne.Missions;
using UnityEditor;
using UnityEngine;

/// <summary>
/// [에디터 전용] 옛 미션 프리팹(Assets/Prefabs/Mission)을 새 구조(MissionStation)로 바꾼 "복사본"을 만든다.
///  원본 프리팹은 수정하지 않는다 (명세 S1: 옛 코드·프리팹은 교체 단계까지 그대로).
///
/// [방식] 모델·월드 UI·NetworkObject 는 그대로 두고 스크립트만 교체한다. 옛 값은 SerializedObject 로 읽어 옮긴다.
///  옛 타입은 "이름(문자열)"으로 찾는다 → 교체 단계에서 옛 코드를 지워도 이 파일은 그대로 컴파일된다.
/// [안전장치] 변환 결과에 옛 스크립트나 Missing Script 가 남으면 저장하지 않는다.
/// [조준 외곽선] 조작 부품(버튼)에 MissionOutlineBuilder 로 외곽선을 붙인다 — 옛 프리팹은 조준 여부를 알려주는 표시가 없었다.
/// [확장] 미니게임을 이식할 때마다 Convert… 함수와 메뉴 호출을 하나씩 추가한다. (1단계: 발전기)
/// </summary>
public static class LegacyMissionConverter
{
    private const string OutputFolder = "Assets/0. Member/SuHan/MissionRedesign_Draft/Prefabs/Missions";
    private const string LegacyGeneratorPath = "Assets/Prefabs/Mission/Generator_Prefab.prefab";

    public const string GeneratorStationPath = OutputFolder + "/Generator_Station.prefab";

    private static readonly string[] LegacyTypeNames = { "GeneratorMission", "MissionButton", "MissionMiniGameBase" };

    [MenuItem("SuHan/Convert Legacy Mission Prefabs")]
    public static void ConvertAllFromMenu()
    {
        ConvertGenerator();
        AssetDatabase.SaveAssets();

        // 새 NetworkObject 프리팹을 Fusion 네트워크 프리팹 목록에 즉시 반영 (안 하면 Runner.Spawn 이 실패한다)
        Fusion.Editor.NetworkProjectConfigUtilities.RebuildPrefabTable();
    }

    /// <summary>발전기 변환. 성공하면 저장된 프리팹의 NetworkObject, 실패하면 null. Fusion 프리팹 목록 갱신은 호출한 쪽 책임.</summary>
    public static NetworkObject ConvertGenerator()
    {
        EnsureFolder(OutputFolder);

        GameObject legacy = AssetDatabase.LoadAssetAtPath<GameObject>(LegacyGeneratorPath);

        if (legacy == null)
        {
            Debug.LogError($"[LegacyMissionConverter] 옛 발전기 프리팹이 없습니다: {LegacyGeneratorPath}");
            return null;
        }

        GameObject copy = Object.Instantiate(legacy);
        copy.name = "Generator_Station";

        try
        {
            MonoBehaviour oldMission = FindLegacy(copy, "GeneratorMission");
            MonoBehaviour oldButton = FindLegacy(copy, "MissionButton");

            if (oldMission == null || oldButton == null)
            {
                Debug.LogError("[LegacyMissionConverter] 발전기 프리팹에서 GeneratorMission 또는 MissionButton 을 찾지 못했습니다.");
                return null;
            }

            SerializedObject oldMissionSO = new SerializedObject(oldMission);
            SerializedObject oldButtonSO = new SerializedObject(oldButton);
            GameObject buttonObject = oldButton.gameObject;

            // 루트: GeneratorMission → GeneratorStation
            GeneratorStation station = copy.AddComponent<GeneratorStation>();
            SerializedObject stationSO = new SerializedObject(station);
            stationSO.FindProperty("completionEvent").intValue = (int)MissionEventType.GeneratorRepaired;
            stationSO.FindProperty("completionPolicy").intValue = (int)CompletionPolicy.Lock;
            stationSO.FindProperty("interactRange").floatValue = 3.5f;
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
            StationButton button = partObject.AddComponent<StationButton>();
            SerializedObject buttonSO = new SerializedObject(button);
            buttonSO.FindProperty("station").objectReferenceValue = station;
            buttonSO.FindProperty("partIndex").intValue = 0;
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

            if (!VerifyNoLegacyScripts(copy) || !VerifyTargetResolution(copy))
                return null;

            GameObject saved = PrefabUtility.SaveAsPrefabAsset(copy, GeneratorStationPath);
            Debug.Log($"[LegacyMissionConverter] 발전기 변환 완료: {GeneratorStationPath}");
            return saved != null ? saved.GetComponent<NetworkObject>() : null;
        }
        finally
        {
            Object.DestroyImmediate(copy);
        }
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

    /// <summary>옛 interactionColliders 를 옮기고, 비어 있으면 버튼의 콜라이더를 쓴다 (발전기는 버튼을 조준해서 누른다).</summary>
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
            Debug.LogWarning("[LegacyMissionConverter] 상호작용 콜라이더가 없습니다. 버튼을 감지할 수 없습니다.");

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
    /// 그 대상이 클릭(IInteractable)도 F 홀드(IHoldInteractable)도 받지 못하면, 조준은 되는데 아무 입력도 안 먹는 상태가 된다.
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

            if (resolved is IInteractable || resolved is IHoldInteractable)
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
