using TrustNoOne.Missions;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// [에디터 전용] 1층 아이템 NavMesh 를 굽는다 (6b 보강 명세 NS2 ~ NS4). 메인 씬 도구 · 테스트 씬 설정 메뉴가 같이 쓴다.
///
/// [하는 일] parent 아래 `NavMesh (1층 아이템 구역)` 오브젝트(NavMeshSurface)를 찾고, 없으면 만들어 설정을 넣는다:
///  범위 상자(Volume) = volume (높이 지면 −0.5 ~ +2.0m — "1층"), 충돌체로 모음(플레이어 충돌과 같은 기준), 조준 레이어(6) 제외(장치 조준 영역 ·
///  청소기가 길을 막지 않게), 기본 Humanoid. 그다음 굽고 → 데이터 파일을 새로 저장 → 연결 → 삼각형 수 · 넓이 로그.
/// [다시 실행 — NS4] 데이터만 새로 굽는다 (NavMesh 는 맵의 결과물). 이미 있는 오브젝트의 범위 · 설정은 기획자가 바꿨을 수 있어 그대로 둔다.
/// [씬 설정] 씬의 NavMesh 설정 칸(NavMeshSettings)은 쓰지 않는다 — NavMeshSurface 방식만.
/// [근거] 6b 보강 명세 NS2 · NS3 · NS4 (사용자 결정 A — 질문 2)
/// </summary>
public static class ItemNavMeshBaker
{
    public const string ObjectName = "NavMesh (1층 아이템 구역)";

    // ProjectSettings/TagManager 의 "Interactable" — 장치 조준 영역(트리거) · 아이템이 쓰는 레이어
    private const int InteractableLayer = 6;

    /// <returns>굽기에 성공하면 true</returns>
    public static bool Bake(Transform parent, Bounds volume, string assetPath)
    {
        Transform existing = parent.Find(ObjectName);
        NavMeshSurface surface = existing != null ? existing.GetComponent<NavMeshSurface>() : null;

        if (surface == null)
        {
            GameObject surfaceObject = existing != null ? existing.gameObject : new GameObject(ObjectName);
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

        NavMeshTriangulation triangulation = NavMesh.CalculateTriangulation();
        float[] cumulative = new float[triangulation.indices.Length / 3];
        float area = SpawnPointRules.BuildCumulativeAreas(triangulation.vertices, triangulation.indices, cumulative);

        Debug.Log($"[ItemNavMeshBaker] NavMesh 구움 — 삼각형 {cumulative.Length}개 · 넓이 {area:0}m² ({assetPath}). " +
                  "Scene 뷰의 AI Navigation 표시로 파란 바닥이 1층에만 있는지 확인하세요.", surface);
        return true;
    }
}
