using System.Collections.Generic;
using TrustNoOne.Missions;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

/// <summary>
/// [에디터 전용] 1층 아이템 NavMesh 를 굽는다 (6b 보강 명세 NS2 ~ NS4 · NS14 · NS15). 메인 씬 도구 · 테스트 씬 설정 메뉴가 같이 쓴다.
///
/// [하는 일] 씬 전체에서 `NavMesh (1층 아이템 구역)` 오브젝트(NavMeshSurface)를 이름으로 찾고, 없으면 parent 아래에 만들어 설정을 넣는다:
///  범위 상자(Volume) = volume (높이 지면 −0.5 ~ +2.0m — "1층"), 충돌체로 모음(플레이어 충돌과 같은 기준), 조준 레이어(6) 제외(장치 조준 영역 ·
///  청소기가 길을 막지 않게), 기본 Humanoid. 그다음 굽고 → 데이터 파일을 새로 저장 → 연결 → 삼각형 수 · 넓이 로그.
/// [씬 전체에서 찾는 이유 — NS14] 기획자가 오브젝트를 다른 묶음으로 옮겨도 다시 실행할 때 두 번째 NavMesh 가 생겨 겹치지 않게. 옮긴 자리는 그대로 둔다.
/// [다시 실행 — NS4] 데이터만 새로 굽는다 (NavMesh 는 맵의 결과물). 이미 있는 오브젝트의 범위 · 설정은 기획자가 바꿨을 수 있어 그대로 둔다.
///  단, 범위가 예전 기본값(outdatedVolumes) 그대로면 새 범위로 바꾼다 (기본값을 고친 경우 — 미션 목록 칸 설정 UI4 와 같은 방식).
/// [예전 데이터 파일 — NS15] 인스펙터의 Bake 버튼은 데이터를 씬 폴더(`Assets/Scenes/<씬 이름>/NavMesh-<오브젝트 이름>.asset`)에 저장한다.
///  다시 구울 때 예전 데이터가 assetPath 가 아닌 곳에 있었으면 그 파일을 지운다 (Unity 의 Bake 버튼도 예전 데이터 파일을 지운다) → 쓰이지 않는 파일이 남지 않는다.
/// [씬 설정] 씬의 NavMesh 설정 칸(NavMeshSettings)은 쓰지 않는다 — NavMeshSurface 방식만.
/// [근거] 6b 보강 명세 NS2 · NS3 · NS4 (사용자 결정 A — 질문 2), NS14 · NS15 (테스트 후 추가 — 사용자 승인)
/// </summary>
public static class ItemNavMeshBaker
{
    public const string ObjectName = "NavMesh (1층 아이템 구역)";

    // ProjectSettings/TagManager 의 "Interactable" — 장치 조준 영역(트리거) · 아이템이 쓰는 레이어
    private const int InteractableLayer = 6;

    // 범위가 예전 기본값과 "같다"고 볼 오차 (m) — 인스펙터에서 손으로 바꾼 값은 이보다 훨씬 크게 달라진다
    private const float SameVolumeTolerance = 0.01f;

    /// <param name="outdatedVolumes">예전 기본 범위들. 이미 있는 오브젝트의 범위가 이 중 하나와 같으면 volume 으로 바꾼다 (없으면 null)</param>
    /// <returns>굽기에 성공하면 true</returns>
    public static bool Bake(Transform parent, Bounds volume, string assetPath, params Bounds[] outdatedVolumes)
    {
        NavMeshSurface surface = FindInScene(parent.gameObject.scene);

        if (surface == null)
        {
            GameObject surfaceObject = new GameObject(ObjectName);
            surfaceObject.transform.SetParent(parent, false);
            // 범위 상자 좌표 = 월드 좌표가 되게 원점 · 회전 없음
            surfaceObject.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

            surface = surfaceObject.AddComponent<NavMeshSurface>();
            surface.collectObjects = CollectObjects.Volume;
            surface.center = volume.center;
            surface.size = volume.size;
            surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            surface.layerMask = ~(1 << InteractableLayer);
            surface.agentTypeID = 0;   // 기본 Humanoid (반지름 0.5 · 높이 2 · 턱 0.75 · 경사 45°)
        }
        else if (IsOutdated(surface, outdatedVolumes))
        {
            Debug.Log($"[ItemNavMeshBaker] 범위 상자가 예전 기본값(가운데 {surface.center} · 크기 {surface.size})이라 새 범위(가운데 {volume.center} · 크기 {volume.size})로 바꿉니다.", surface);
            surface.center = volume.center;
            surface.size = volume.size;
        }

        // 굽기 전에 예전 데이터 파일 위치를 기억한다 (굽고 나면 새 데이터로 바뀐다 — NS15)
        string previousPath = surface.navMeshData != null ? AssetDatabase.GetAssetPath(surface.navMeshData) : null;

        surface.BuildNavMesh();

        if (surface.navMeshData == null)
        {
            Debug.LogError($"[ItemNavMeshBaker] NavMesh 를 굽지 못했습니다 — '{ObjectName}' 범위 상자 안에 바닥 충돌체가 있는지 확인하세요.", surface);
            return false;
        }

        // 앞의 데이터 파일을 지우고 새로 저장 (구운 결과가 이미 새 데이터라 지워도 연결이 끊기지 않는다)
        AssetDatabase.DeleteAsset(assetPath);
        AssetDatabase.CreateAsset(surface.navMeshData, assetPath);
        EditorUtility.SetDirty(surface);

        // 예전 데이터가 다른 곳(인스펙터 Bake 의 씬 폴더 등)에 있었으면 지운다 — 이제 아무도 쓰지 않는 파일
        if (!string.IsNullOrEmpty(previousPath) && previousPath != assetPath && AssetDatabase.DeleteAsset(previousPath))
            Debug.Log($"[ItemNavMeshBaker] 예전 NavMesh 데이터 파일을 지웠습니다: {previousPath} (새 데이터는 {assetPath}).");

        NavMeshTriangulation triangulation = NavMesh.CalculateTriangulation();
        float[] cumulative = new float[triangulation.indices.Length / 3];
        float area = SpawnPointRules.BuildCumulativeAreas(triangulation.vertices, triangulation.indices, cumulative);

        Debug.Log($"[ItemNavMeshBaker] NavMesh 구움 — 삼각형 {cumulative.Length}개 · 넓이 {area:0}m² ({assetPath}). " +
                  "Scene 뷰의 AI Navigation 표시로 파란 바닥이 1층에만 있는지 확인하세요.", surface);
        return true;
    }

    /// <summary>씬 전체(꺼진 오브젝트 포함)에서 이름이 ObjectName 인 NavMeshSurface 를 찾는다. 여러 개면 첫 번째를 쓰고 경고한다.</summary>
    private static NavMeshSurface FindInScene(Scene scene)
    {
        List<NavMeshSurface> found = new List<NavMeshSurface>();

        foreach (GameObject root in scene.GetRootGameObjects())
        {
            foreach (NavMeshSurface surface in root.GetComponentsInChildren<NavMeshSurface>(true))
            {
                if (surface.gameObject.name == ObjectName)
                    found.Add(surface);
            }
        }

        if (found.Count > 1)
            Debug.LogWarning($"[ItemNavMeshBaker] '{ObjectName}' 이 {found.Count}개 있습니다 — 첫 번째만 굽습니다. 나머지는 지워 주세요 (NavMesh 가 겹친다).", found[1]);

        return found.Count > 0 ? found[0] : null;
    }

    private static bool IsOutdated(NavMeshSurface surface, Bounds[] outdatedVolumes)
    {
        if (outdatedVolumes == null)
            return false;

        foreach (Bounds old in outdatedVolumes)
        {
            if (Vector3.Distance(surface.center, old.center) < SameVolumeTolerance && Vector3.Distance(surface.size, old.size) < SameVolumeTolerance)
                return true;
        }

        return false;
    }
}
