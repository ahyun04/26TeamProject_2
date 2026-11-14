using Fusion;
using TrustNoOne.Gimmicks;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// [역할 · 에디터 전용] 살인마 전용 암전 관리자를 열린 씬에 놓는 공통 단계 — 4~12 설치 메뉴 · `Apply Missions To 4~12` · 혼자 테스트 씬 메뉴가 같이 부른다.
///  관리자 오브젝트 하나에 NetworkObject → KillerBlackoutManager → 화면 부품 4개(BlackoutScreenRig · BlackoutArms · BlackoutMapBlocker · BlackoutPromptLabel)
///  → KillerBlackoutView 를 붙이고, BlackoutArms 의 팔 프리팹 칸을 팀원 1인칭 팔 프리팹으로 채운다.
/// [이유] 설치 코드를 한 벌로 두어 혼자 씬과 실제 게임 씬의 관리자 모양이 어긋나지 않게 한다 (16a 의 "16c 실제 게임 씬 설치와 같은 모양"을 코드로 지킴).
///  프리팹 없는 일반 씬 오브젝트다 — 씬을 저장할 때 Fusion 이 씬 오브젝트로 굽는다 (16a 에서 확인).
/// [다시 실행해도 안전] 씬 전체에서 이미 있는 관리자를 찾아(꺼진 것 포함 — 기획자가 옮겨도 그것을 씀) 없는 컴포넌트만 붙인다.
///  팔 프리팹 칸은 비어 있을 때만 채우고, 숫자 칸(첫 대기 · 지속 시간 등)은 건드리지 않는다 — 새로 만들면 코드 기본값(첫 대기 120), 있으면 기획자 값이 남는다.
///  부품을 View 보다 먼저 붙인다 — View 의 RequireComponent 가 이미 있는 오브젝트에도 빠짐없이 붙게 (16b).
/// [근거] 16c 명세 KB45 · KB46 (접근법 3 — 사용자 결정), 16a KB9 · KB20, 16b KB31 · KB34 · KB40, 6b SW7 · 9a MP3 "이름으로 찾아 이미 있으면 건드리지 않는다"
/// </summary>
public static class KillerBlackoutInstaller
{
    /// <summary>새로 만들 때의 오브젝트 이름</summary>
    public const string ManagerName = "KillerBlackoutManager";

    /// <summary>빈손 팔 복사본에 쓸 팀원 1인칭 팔 프리팹 (Player.prefab 의 PlayerFirstPersonItemView.armsPrefab 과 같은 것 — 읽기만, 16b KB34)</summary>
    public const string FirstPersonArmsPrefabPath = "Assets/0. Member/Dinosaur/Prefab/PlayerFirstPersonArms.prefab";

    /// <summary>
    /// 열린 씬에 관리자가 없으면 parent 아래에 만들고, 있으면 그것에 없는 컴포넌트만 붙인다. 관리자를 돌려준다.
    /// created = 이번에 오브젝트를 새로 만들었나, armsFilled = 이번에 팔 프리팹 칸을 채웠나.
    /// </summary>
    public static KillerBlackoutManager Ensure(Transform parent, out bool created, out bool armsFilled)
    {
        KillerBlackoutManager existing = Object.FindFirstObjectByType<KillerBlackoutManager>(FindObjectsInactive.Include);
        created = existing == null;

        GameObject go;

        if (existing != null)
        {
            go = existing.gameObject;
        }
        else
        {
            go = new GameObject(ManagerName);
            go.transform.SetParent(parent, false);
        }

        GetOrAdd<NetworkObject>(go);
        KillerBlackoutManager manager = GetOrAdd<KillerBlackoutManager>(go);

        // 16b: 화면 부품 4개 (KillerBlackoutView 의 RequireComponent 와 같은 것 — 먼저 놓는다)
        GetOrAdd<BlackoutScreenRig>(go);
        BlackoutArms arms = GetOrAdd<BlackoutArms>(go);
        GetOrAdd<BlackoutMapBlocker>(go);
        GetOrAdd<BlackoutPromptLabel>(go);
        GetOrAdd<KillerBlackoutView>(go);

        armsFilled = FillArmsPrefabIfEmpty(arms);
        return manager;
    }

    /// <summary>팔 프리팹 칸이 비어 있을 때만 팀원 1인칭 팔 프리팹으로 채운다. 채웠으면 true. 프리팹을 못 찾으면 오류 로그 (칸은 비운 채)</summary>
    private static bool FillArmsPrefabIfEmpty(BlackoutArms arms)
    {
        SerializedObject so = new SerializedObject(arms);
        SerializedProperty field = so.FindProperty("armsPrefab");

        if (field.objectReferenceValue != null)
            return false;

        GameObject armsPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(FirstPersonArmsPrefabPath);
        PlayerFirstPersonArmsComponent armsComponent = armsPrefab != null ? armsPrefab.GetComponent<PlayerFirstPersonArmsComponent>() : null;

        if (armsComponent == null)
        {
            Debug.LogError($"[KillerBlackoutInstaller] 1인칭 팔 프리팹을 찾지 못했습니다: {FirstPersonArmsPrefabPath} — 암전 중 빈손 팔이 보이지 않습니다.");
            return false;
        }

        field.objectReferenceValue = armsComponent;
        so.ApplyModifiedPropertiesWithoutUndo();
        return true;
    }

    private static T GetOrAdd<T>(GameObject target) where T : Component
    {
        T component = target.GetComponent<T>();
        return component != null ? component : target.AddComponent<T>();
    }
}
