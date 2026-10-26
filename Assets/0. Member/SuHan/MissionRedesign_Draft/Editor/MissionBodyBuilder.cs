using System.Collections.Generic;
using System.Linq;
using TMPro;
using TrustNoOne.Missions;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// [역할 · 에디터 전용] 장치 · 선반에 플레이어가 뚫지 못하는 "막는 몸체"를 붙인다 (11a 명세 BC5 ~ BC12).
///  Attach: 켜진 렌더러의 메시 범위를 루트 기준 상자로 모아 → 가로 0.35m 넓힘 → 겹치거나 닿는 상자 합치기 (순수 규칙 BodyBoxRules)
///   → 루트 아래 자식 `Body (막는 몸체)` (Default 레이어) 에 상자 콜라이더 여러 개.
///  AddShelfBodies: 씬의 선반 모델(모델 폴더 Shelf*.fbx) 인스턴스마다 Attach — 이미 막혀 있으면 건너뜀.
/// [이유] 플레이어 이동 충돌은 Default 레이어만 본다. 장치 콜라이더는 대부분 조준 레이어(Interactable)에만 있어 통과되고,
///  선반은 콜라이더가 없다. 모델보다 넓게 막아야 카메라(몸 앞면, Near 0.3)가 표면에 닿아도 안이 보이지 않는다 (BC2).
///  조준 감지는 조준 레이어만 보므로 Default 몸체는 조준을 가리지 않는다. 아이템 NavMesh 는 몸체를 장애물로 본다.
/// [쓰는 곳] 변환 도구(LegacyMissionConverter.FinishPrefab — 방화문을 뺀 장치), 배치 도구(4~12 · GamePlay — NavMesh 굽기 전에 선반).
///  방화문(BC18 — 테스트 후 추가): 변환 도구의 방화문 조립이 문짝 · 기둥 충돌을 PadHorizontal 로 넓히고 양쪽 패널 묶음에 Attach.
/// [근거] 11a 명세 BC2 · BC5 ~ BC12 (사용자 결정 C · A, 접근 1), BC18 (사용자 결정 A — 방화문도 닫혀 있을 때 막기)
/// </summary>
public static class MissionBodyBuilder
{
    public const string BodyName = "Body (막는 몸체)";

    // 플레이어 이동(SimpleKCC)이 부딪히는 레이어 (Player.prefab CollisionLayerMask = Default)
    private const int DefaultLayer = 0;

    // 선반 모델: 모델 폴더의 Shelf.fbx · 색 버전 Shelf 1.fbx … (BC12)
    private const string ShelfModelPrefix = "Assets/0. Member/Taewoo/Model/Shelf";

    /// <summary>
    /// 루트에 막는 몸체를 붙이고 상자 수를 돌려준다 (0 이면 자식을 만들지 않는다). 이미 `Body (막는 몸체)` 가 있으면 지우고 새로 만든다.
    /// 범위 재료(BC6): 켜진 오브젝트의 켜진 렌더러 중 메시가 있는 것. 글자(TMP — 안내 문구 · 화면 글자)는 뺀다.
    /// 외곽선 사본 · 숨긴 부품은 꺼져 있어 저절로 빠진다. 움직이는 부품은 지금 모습으로 잰다 (BC9).
    /// </summary>
    public static int Attach(GameObject root)
    {
        Transform rootTransform = root.transform;
        Transform old = rootTransform.Find(BodyName);

        if (old != null)
            Object.DestroyImmediate(old.gameObject);

        // 0.35m 는 월드 거리 → 루트 기준 좌표에서는 루트 크기로 나눈다 (장치 루트는 모두 1, 선반 인스턴스는 다를 수 있다)
        Vector3 scale = rootTransform.lossyScale;
        float padX = BodyBoxRules.CameraClearance / Mathf.Max(Mathf.Abs(scale.x), 0.0001f);
        float padZ = BodyBoxRules.CameraClearance / Mathf.Max(Mathf.Abs(scale.z), 0.0001f);

        List<Bounds> boxes = new List<Bounds>();

        foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(false))
        {
            if (!renderer.enabled || renderer.GetComponentInParent<TMP_Text>() != null || !TryGetMesh(renderer, out Mesh mesh))
                continue;

            boxes.Add(BodyBoxRules.PadHorizontal(ToRootSpace(rootTransform, renderer.transform, mesh.bounds), padX, padZ));
        }

        List<Bounds> merged = BodyBoxRules.Merge(boxes);

        if (merged.Count == 0)
            return 0;

        GameObject body = new GameObject(BodyName);
        body.layer = DefaultLayer;
        body.transform.SetParent(rootTransform, false);

        foreach (Bounds box in merged)
        {
            BoxCollider collider = body.AddComponent<BoxCollider>();
            collider.center = box.center;
            collider.size = box.size;
        }

        return merged.Count;
    }

    /// <summary>
    /// 이미 있는 상자 콜라이더를 루트 기준 가로(앞뒤 · 좌우)로 0.35m 씩 넓힌다. 루트 기준으로 세로(위아래)인 축은 그대로 (11a 명세 BC18 — 방화문 문짝 · 기둥).
    /// 콜라이더의 각 축 1 단위가 월드에서 어느 쪽 · 얼마 길이인지 보고 넓히므로, 모델이 어떤 각도 · 크기로 들어와 있어도 맞다.
    /// 같은 콜라이더의 크기만 바꾼다 → 다른 코드가 이 콜라이더를 켜고 끄는 것(방화문 열림 — FD14)은 그대로 동작한다.
    /// </summary>
    public static void PadHorizontal(BoxCollider collider, Transform root)
    {
        Transform part = collider.transform;
        Vector3 size = collider.size;

        for (int axis = 0; axis < 3; axis++)
        {
            Vector3 unit = Vector3.zero;
            unit[axis] = 1f;
            Vector3 world = part.TransformVector(unit);
            float length = world.magnitude;

            if (length < 0.0001f)
                continue;

            // 루트 기준으로 거의 위아래인 축은 넓히지 않는다 (높이는 모델 그대로 — BC7 과 같은 규칙)
            if (Mathf.Abs(root.InverseTransformDirection(world / length).y) > 0.5f)
                continue;

            size[axis] += BodyBoxRules.CameraClearance * 2f / length;
        }

        collider.size = size;
    }

    /// <summary>로그용: 몸체 상자마다 "가로 × 세로(앞뒤) · 높이 아래 ~ 위" (루트 기준 m). 턱 높이 0.3 보다 낮은 상자 확인용 (명세 5장 4).</summary>
    public static string Describe(GameObject root)
    {
        Transform body = root.transform.Find(BodyName);

        if (body == null)
            return "없음";

        return string.Join(" / ", body.GetComponents<BoxCollider>().Select(box =>
            $"{box.size.x:0.00} × {box.size.z:0.00} · 높이 {box.center.y - box.size.y * 0.5f:0.00} ~ {box.center.y + box.size.y * 0.5f:0.00}"));
    }

    /// <summary>
    /// 씬의 선반을 막는다 (BC12). 막은 선반 수를 돌려준다.
    /// 메시가 모델 폴더의 Shelf*.fbx 에서 온 오브젝트 → 가장 가까운 프리팹(모델) 인스턴스 루트 단위.
    /// 안에 통과 못 하는 Default 콜라이더가 이미 있으면 건너뛴다 (메뉴를 다시 실행해도 안전 · 맵 담당이 붙인 콜라이더 존중).
    /// 모델 파일은 고치지 않는다 — 씬의 인스턴스에 자식을 더할 뿐이다.
    /// </summary>
    public static int AddShelfBodies(Scene scene)
    {
        HashSet<GameObject> shelves = new HashSet<GameObject>();

        foreach (GameObject sceneRoot in scene.GetRootGameObjects())
        {
            foreach (MeshFilter filter in sceneRoot.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh == null || !IsShelfModel(AssetDatabase.GetAssetPath(filter.sharedMesh)))
                    continue;

                GameObject shelf = PrefabUtility.GetNearestPrefabInstanceRoot(filter.gameObject);
                shelves.Add(shelf != null ? shelf : filter.gameObject);
            }
        }

        int blocked = 0;

        foreach (GameObject shelf in shelves)
        {
            if (HasSolidDefaultCollider(shelf))
                continue;

            if (Attach(shelf) == 0)
                continue;

            EditorUtility.SetDirty(shelf);
            blocked++;
            Debug.Log($"[MissionBodyBuilder] 선반 '{shelf.name}' 막음 — {Describe(shelf)}", shelf);
        }

        return blocked;
    }

    private static bool IsShelfModel(string assetPath)
    {
        return assetPath.StartsWith(ShelfModelPrefix) && assetPath.EndsWith(".fbx", System.StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasSolidDefaultCollider(GameObject root)
    {
        return root.GetComponentsInChildren<Collider>(true).Any(collider => collider.gameObject.layer == DefaultLayer && !collider.isTrigger);
    }

    private static bool TryGetMesh(Renderer renderer, out Mesh mesh)
    {
        mesh = null;

        if (renderer is MeshRenderer)
        {
            MeshFilter filter = renderer.GetComponent<MeshFilter>();
            mesh = filter != null ? filter.sharedMesh : null;
        }
        else if (renderer is SkinnedMeshRenderer skinned)
        {
            mesh = skinned.sharedMesh;
        }

        return mesh != null;
    }

    /// <summary>메시 범위(렌더러 기준)의 8 꼭짓점을 루트 기준으로 옮겨 감싸는 상자 (회전된 부품은 조금 넉넉해진다).</summary>
    private static Bounds ToRootSpace(Transform root, Transform part, Bounds local)
    {
        Vector3 min = local.min;
        Vector3 max = local.max;
        Bounds result = default;
        bool first = true;

        for (int i = 0; i < 8; i++)
        {
            Vector3 corner = new Vector3((i & 1) == 0 ? min.x : max.x, (i & 2) == 0 ? min.y : max.y, (i & 4) == 0 ? min.z : max.z);
            Vector3 point = root.InverseTransformPoint(part.TransformPoint(corner));

            if (first)
            {
                result = new Bounds(point, Vector3.zero);
                first = false;
            }
            else
            {
                result.Encapsulate(point);
            }
        }

        return result;
    }
}
