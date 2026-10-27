using System.Collections.Generic;
using Fusion;
using TMPro;
using TrustNoOne.Missions;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// [역할 · 에디터 전용] 맵 공통 단계 — 아이템 자리 (12b 명세 IS4 · IS6 ~ IS9 · IS13). 4~12 메뉴가 부른다.
///  1) `Missions (새 미션)/Item Spots (아이템 자리)` 에 자리 관리자(ItemSpotDirector) — 없을 때만
///  2) 구역 상자(ItemZone) — 관리자 아래에 구역 상자가 하나도 없을 때만 설정 묶음(MissionMapSpec.ItemZones) 값으로
///  3) 선반 칸(ShelfSlot) — 칸이 하나도 없는 선반에만: 선반 메시의 위 · 아래를 향한 면 → ShelfSlotRules.Find → 선반 자식 `Slot (선반 칸)`
///     (테스트 후 추가 — IS23) 입구가 다른 선반 몸통 안인 칸은 만들지 않고, 이미 있는 칸도 매번 검사해 지운다 (끝을 맞대고 겹친 선반)
///  4) 아이템 크기표 · 무기 3종 · 청소기 프리팹 — 매번 다시 잰다 (프리팹이 바뀌어도 맞게)
/// [다시 실행해도 안전 — IS9] 구역 · 칸은 이미 있으면 그대로 → 기획자가 옮기거나 지운 것이 남는다. 처음 값으로 되돌리려면 그 오브젝트를 지우고 다시 실행.
/// [콜라이더 없음] 구역 · 칸은 NavMesh 굽기 · 이동 · 조준에 영향이 없어 굽기 순서와 상관없다.
/// [크기표 — IS13 구현] 프리팹의 켜진 렌더러 메시 범위를 아이템 루트 기준(루트 크기 포함 · 루트 위치 · 회전 제외)으로 잰다 —
///  보이는 모양 기준이라 조준 상자가 넉넉한 아이템(청소기)도 칸 판정이 실제와 맞다 (명세의 "조준 콜라이더"에서 바꿈).
/// [근거] 12b 명세 IS4 · IS6 ~ IS9 · IS13 · IS17 (사용자 결정 A — 질문 4, 접근 1), IS23 (테스트 후 추가 — "선반이 겹쳐져 있는 부분은 스폰 제외")
/// </summary>
public static class ItemSpotInstaller
{
    public const string DirectorName = "Item Spots (아이템 자리)";
    private const string SlotName = "Slot (선반 칸)";

    // 판마다 하나씩 만드는 무기 (팀원 프리팹 — 부르기만, IS1)
    private static readonly string[] WeaponPrefabPaths =
    {
        "Assets/Prefabs/Item/Knife_Prefab.prefab",
        "Assets/Prefabs/Item/Scissors_Prefab.prefab",
        "Assets/Prefabs/Item/Screwdriver_Prefab.prefab",
    };

    // 크기표에 넣을 내 아이템 (장치가 자리를 받아 가는 것 + 청소기)
    private const string ItemPrefabFolder = "Assets/0. Member/SuHan/MissionRedesign_Draft/Prefabs/Items";
    private static readonly string[] MissionItemPaths =
    {
        LegacyMissionConverter.OxygenTankPath,
        ItemPrefabFolder + "/EquipmentPart_Blue.prefab",
        ItemPrefabFolder + "/EquipmentPart_Gray.prefab",
        ItemPrefabFolder + "/EquipmentPart_Red.prefab",
        ItemPrefabFolder + "/EquipmentPart_Yellow.prefab",
        LegacyMissionConverter.VacuumToolPath,
    };

    // 선반 메시의 수평 면으로 볼 법선 위쪽 성분 (판 · 천장)
    private const float FlatNormal = 0.95f;

    /// <summary>자리 관리자 · 구역 · 선반 칸 · 크기표를 준비하고 결과 문장을 돌려준다.</summary>
    public static string Setup(Scene scene, Transform root, MissionMapSpec spec)
    {
        ItemSpotDirector director = EnsureDirector(root, out bool created);
        int zonesCreated = EnsureZones(director, spec, out int zonesTotal);
        List<ShelfVolume> volumes = ShelfVolumes(scene);
        int slotsCreated = AddShelfSlots(volumes, out int shelvesFilled, out int skippedBlocked);
        int removedBlocked = RemoveBlockedSlots(volumes);
        int sized = FillDirector(director, out int weapons, out bool vacuum);

        EditorUtility.SetDirty(director);
        return $"자리 관리자 {(created ? "새로 만듦" : "있음")}, 구역 상자 {zonesTotal}개 (새로 {zonesCreated}), " +
               $"선반 칸 새로 {slotsCreated}칸 (선반 {volumes.Count}개 중 {shelvesFilled}개에 — 칸이 있던 선반은 그대로, 막혀서 안 만든 칸 {skippedBlocked}), " +
               $"막힌 칸 {removedBlocked}개 지움, 아이템 크기표 {sized}종, 무기 {weapons}종 · 청소기 {(vacuum ? "연결" : "없음")}";
    }

    private static ItemSpotDirector EnsureDirector(Transform root, out bool created)
    {
        Transform existing = root.Find(DirectorName);
        created = existing == null;

        if (existing == null)
        {
            existing = new GameObject(DirectorName).transform;
            existing.SetParent(root, false);
        }

        ItemSpotDirector director = existing.GetComponent<ItemSpotDirector>();
        return director != null ? director : existing.gameObject.AddComponent<ItemSpotDirector>();
    }

    /// <summary>관리자 아래에 구역 상자가 하나도 없을 때만 설정 묶음 값으로 만든다 (IS9). 만든 수를 돌려준다.</summary>
    private static int EnsureZones(ItemSpotDirector director, MissionMapSpec spec, out int total)
    {
        int existing = director.GetComponentsInChildren<ItemZone>(true).Length;
        total = existing;

        if (existing > 0 || spec.ItemZones == null)
            return 0;

        foreach (ItemZoneSpec zone in spec.ItemZones)
        {
            GameObject zoneObject = new GameObject(zone.Name);
            zoneObject.transform.SetParent(director.transform, false);
            zoneObject.transform.position = zone.Center;
            zoneObject.transform.localScale = zone.Size;
            zoneObject.AddComponent<ItemZone>();
        }

        total = spec.ItemZones.Length;
        return spec.ItemZones.Length;
    }

    /// <summary>선반 하나의 몸통 범위 (그 선반 기준) — 막힌 칸 검사용 (IS23).</summary>
    private sealed class ShelfVolume
    {
        public GameObject Shelf;
        public Bounds LocalBounds;
    }

    private static List<ShelfVolume> ShelfVolumes(Scene scene)
    {
        List<ShelfVolume> volumes = new List<ShelfVolume>();

        foreach (GameObject shelf in MissionBodyBuilder.FindShelves(scene))
            volumes.Add(new ShelfVolume { Shelf = shelf, LocalBounds = LocalMeshBounds(shelf.transform) });

        return volumes;
    }

    /// <summary>입구 가운데(월드)가 자기 말고 다른 선반 몸통 안이면 막힘 (IS23 — ShelfSlotRules.IsBlocked).</summary>
    private static bool IsBlocked(Vector3 openingWorld, GameObject owner, List<ShelfVolume> volumes)
    {
        foreach (ShelfVolume other in volumes)
        {
            if (other.Shelf != owner && ShelfSlotRules.IsBlocked(openingWorld, other.Shelf.transform.worldToLocalMatrix, other.LocalBounds))
                return true;
        }

        return false;
    }

    /// <summary>이미 있는 칸 중 막힌 칸을 지운다 (매번 — 기획자가 선반을 옮겨 붙여도 맞게). 지운 수를 돌려준다.</summary>
    private static int RemoveBlockedSlots(List<ShelfVolume> volumes)
    {
        int removed = 0;

        foreach (ShelfVolume volume in volumes)
        {
            foreach (ShelfSlot slot in volume.Shelf.GetComponentsInChildren<ShelfSlot>(true))
            {
                // 칸 공간: 앞(+Z) = 열린 쪽, 원점 = 바닥판 가운데 → 입구 가운데 = (0, 높이 절반, 깊이 절반)
                Vector3 opening = slot.transform.TransformPoint(new Vector3(0f, slot.Height * 0.5f, slot.Depth * 0.5f));

                if (!IsBlocked(opening, volume.Shelf, volumes))
                    continue;

                Debug.Log($"[ItemSpotInstaller] 선반 '{volume.Shelf.name}' 의 '{slot.name}': 입구가 다른 선반 안이라 지웁니다 (겹친 부분 — IS23).", volume.Shelf);
                Object.DestroyImmediate(slot.gameObject);
                EditorUtility.SetDirty(volume.Shelf);
                removed++;
            }
        }

        return removed;
    }

    /// <summary>칸이 하나도 없는 선반마다 메시를 분석해 칸을 만든다 (IS7 · IS8). 막힌 칸은 만들지 않는다 (IS23). 만든 칸 수를 돌려준다.</summary>
    private static int AddShelfSlots(List<ShelfVolume> volumes, out int shelvesFilled, out int skippedBlocked)
    {
        shelvesFilled = 0;
        skippedBlocked = 0;
        int created = 0;

        foreach (ShelfVolume volume in volumes)
        {
            GameObject shelf = volume.Shelf;

            if (shelf.GetComponentsInChildren<ShelfSlot>(true).Length > 0)
                continue;

            List<ShelfCompartment> compartments = Analyze(shelf.transform, out Vector3 center);

            if (compartments.Count == 0)
            {
                Debug.LogWarning($"[ItemSpotInstaller] 선반 '{shelf.name}': 칸을 찾지 못했습니다 (모델이 바뀌었는지 확인).", shelf);
                continue;
            }

            for (int i = compartments.Count - 1; i >= 0; i--)
            {
                ShelfCompartment c = compartments[i];
                Vector3 opening = shelf.transform.TransformPoint(ShelfSlotRules.OpeningPoint(c.BoardCenter, c.Front, c.Height, c.Depth));

                if (IsBlocked(opening, shelf, volumes))
                {
                    compartments.RemoveAt(i);
                    skippedBlocked++;
                }
            }

            for (int i = 0; i < compartments.Count; i++)
            {
                ShelfCompartment c = compartments[i];
                GameObject slotObject = new GameObject($"{SlotName} {i + 1} — 판 {c.BoardCenter.y:0.00}m");
                slotObject.transform.SetParent(shelf.transform, false);
                slotObject.transform.localPosition = c.BoardCenter;
                // 앞(+Z) = 열린 쪽, 위 = 위 → 오른쪽(x) 은 저절로 칸 폭 방향
                slotObject.transform.localRotation = Quaternion.LookRotation(c.Front, Vector3.up);
                slotObject.AddComponent<ShelfSlot>().Configure(c.Width, c.Height, c.Depth);
            }

            EditorUtility.SetDirty(shelf);
            shelvesFilled++;
            created += compartments.Count;
            Debug.Log($"[ItemSpotInstaller] 선반 '{shelf.name}': 칸 {compartments.Count}개 — {Describe(compartments)}", shelf);
        }

        return created;
    }

    /// <summary>선반 아래 메시들의 삼각형을 선반 루트 기준으로 옮겨 판(위를 향함) · 천장(아래를 향함) 사각형으로 나눈다.</summary>
    private static List<ShelfCompartment> Analyze(Transform shelf, out Vector3 center)
    {
        List<ShelfFaceRect> boards = new List<ShelfFaceRect>();
        List<ShelfFaceRect> ceilings = new List<ShelfFaceRect>();
        Bounds all = default;
        bool first = true;

        foreach (MeshFilter filter in shelf.GetComponentsInChildren<MeshFilter>(true))
        {
            Mesh mesh = filter.sharedMesh;

            if (mesh == null)
                continue;

            Vector3[] vertices = mesh.vertices;
            int[] triangles = mesh.triangles;
            Vector3[] local = new Vector3[vertices.Length];

            for (int v = 0; v < vertices.Length; v++)
            {
                local[v] = shelf.InverseTransformPoint(filter.transform.TransformPoint(vertices[v]));

                if (first)
                {
                    all = new Bounds(local[v], Vector3.zero);
                    first = false;
                }
                else
                {
                    all.Encapsulate(local[v]);
                }
            }

            for (int i = 0; i + 2 < triangles.Length; i += 3)
            {
                Vector3 a = local[triangles[i]], b = local[triangles[i + 1]], c = local[triangles[i + 2]];
                Vector3 normal = Vector3.Cross(b - a, c - a);

                if (normal.sqrMagnitude < 1e-12f)
                    continue;

                normal.Normalize();

                if (Mathf.Abs(normal.y) < FlatNormal)
                    continue;

                ShelfFaceRect rect = new ShelfFaceRect((a.y + b.y + c.y) / 3f,
                    Mathf.Min(a.x, Mathf.Min(b.x, c.x)), Mathf.Max(a.x, Mathf.Max(b.x, c.x)),
                    Mathf.Min(a.z, Mathf.Min(b.z, c.z)), Mathf.Max(a.z, Mathf.Max(b.z, c.z)));

                if (normal.y > 0f)
                    boards.Add(rect);
                else
                    ceilings.Add(rect);
            }
        }

        center = all.center;
        return ShelfSlotRules.Find(boards, ceilings, center);
    }

    /// <summary>선반 아래 메시 범위를 선반 기준으로 (몸통 범위 — IS23).</summary>
    private static Bounds LocalMeshBounds(Transform shelf)
    {
        Bounds bounds = default;
        bool any = false;

        foreach (MeshFilter filter in shelf.GetComponentsInChildren<MeshFilter>(true))
        {
            if (filter.sharedMesh == null)
                continue;

            Bounds mesh = filter.sharedMesh.bounds;

            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = new Vector3(i % 2 == 0 ? mesh.min.x : mesh.max.x, (i / 2) % 2 == 0 ? mesh.min.y : mesh.max.y, i / 4 == 0 ? mesh.min.z : mesh.max.z);
                Vector3 point = shelf.InverseTransformPoint(filter.transform.TransformPoint(corner));

                if (!any)
                {
                    bounds = new Bounds(point, Vector3.zero);
                    any = true;
                }
                else
                {
                    bounds.Encapsulate(point);
                }
            }
        }

        return bounds;
    }

    private static string Describe(List<ShelfCompartment> compartments)
    {
        List<string> parts = new List<string>();

        foreach (ShelfCompartment c in compartments)
            parts.Add($"판 {c.BoardCenter.y:0.00} · {c.Width:0.00} × 높이 {c.Height:0.00} × 깊이 {c.Depth:0.00}");

        return string.Join(" / ", parts);
    }

    /// <summary>크기표 · 무기 · 청소기 프리팹을 관리자에 넣는다 (매번). 크기를 잰 아이템 수를 돌려준다.</summary>
    private static int FillDirector(ItemSpotDirector director, out int weapons, out bool vacuum)
    {
        List<NetworkObject> sizedPrefabs = new List<NetworkObject>();
        List<Bounds> sizes = new List<Bounds>();
        List<NetworkObject> weaponList = new List<NetworkObject>();

        foreach (string path in MissionItemPaths)
            AddSize(path, sizedPrefabs, sizes);

        foreach (string path in WeaponPrefabPaths)
        {
            NetworkObject weapon = AddSize(path, sizedPrefabs, sizes);

            if (weapon != null)
                weaponList.Add(weapon);
        }

        NetworkObject vacuumPrefab = LoadNetworkObject(LegacyMissionConverter.VacuumToolPath);

        SerializedObject so = new SerializedObject(director);
        SerializedProperty table = so.FindProperty("itemSizes");
        table.arraySize = sizedPrefabs.Count;

        for (int i = 0; i < sizedPrefabs.Count; i++)
        {
            SerializedProperty entry = table.GetArrayElementAtIndex(i);
            entry.FindPropertyRelative("Prefab").objectReferenceValue = sizedPrefabs[i];
            entry.FindPropertyRelative("Center").vector3Value = sizes[i].center;
            entry.FindPropertyRelative("Size").vector3Value = sizes[i].size;
        }

        SerializedProperty weaponProperty = so.FindProperty("weaponPrefabs");
        weaponProperty.arraySize = weaponList.Count;

        for (int i = 0; i < weaponList.Count; i++)
            weaponProperty.GetArrayElementAtIndex(i).objectReferenceValue = weaponList[i];

        so.FindProperty("vacuumPrefab").objectReferenceValue = vacuumPrefab;
        so.ApplyModifiedPropertiesWithoutUndo();

        weapons = weaponList.Count;
        vacuum = vacuumPrefab != null;
        return sizedPrefabs.Count;
    }

    /// <summary>프리팹을 불러 크기를 재서 목록에 더한다. 없으면 경고 후 null.</summary>
    private static NetworkObject AddSize(string path, List<NetworkObject> prefabs, List<Bounds> sizes)
    {
        NetworkObject prefab = LoadNetworkObject(path);

        if (prefab == null)
            return null;

        if (!TryMeasure(prefab.transform, out Bounds bounds))
        {
            Debug.LogWarning($"[ItemSpotInstaller] {prefab.name}: 렌더러가 없어 크기를 잴 수 없습니다 — 바닥에만 놓입니다.");
            return prefab;
        }

        prefabs.Add(prefab);
        sizes.Add(bounds);
        Debug.Log($"[ItemSpotInstaller] 아이템 크기 {prefab.name}: {bounds.size.x:0.00} × {bounds.size.y:0.00} × {bounds.size.z:0.00}m (가운데 {bounds.center})");
        return prefab;
    }

    private static NetworkObject LoadNetworkObject(string path)
    {
        GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        NetworkObject networkObject = asset != null ? asset.GetComponent<NetworkObject>() : null;

        if (networkObject == null)
            Debug.LogWarning($"[ItemSpotInstaller] 아이템 프리팹을 찾지 못했습니다 (NetworkObject 가 있는 프리팹): {path}");

        return networkObject;
    }

    /// <summary>
    /// 켜진 렌더러 메시 범위를 아이템 루트 기준(루트 크기 포함, 루트 위치 · 회전 제외)으로 잰다 (IS13).
    /// 꺼진 오브젝트(외곽선 사본 등) · 글자(TMP)는 뺀다.
    /// </summary>
    private static bool TryMeasure(Transform root, out Bounds bounds)
    {
        bounds = default;
        bool any = false;
        Quaternion inverse = Quaternion.Inverse(root.rotation);

        foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            if (!IsActiveUnder(renderer.transform, root) || renderer.GetComponentInParent<TMP_Text>(true) != null)
                continue;

            Mesh mesh = renderer is SkinnedMeshRenderer skinned ? skinned.sharedMesh
                : renderer.GetComponent<MeshFilter>() != null ? renderer.GetComponent<MeshFilter>().sharedMesh : null;

            if (mesh == null)
                continue;

            Bounds local = mesh.bounds;

            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = new Vector3(i % 2 == 0 ? local.min.x : local.max.x, (i / 2) % 2 == 0 ? local.min.y : local.max.y, i / 4 == 0 ? local.min.z : local.max.z);
                Vector3 point = inverse * (renderer.transform.TransformPoint(corner) - root.position);

                if (!any)
                {
                    bounds = new Bounds(point, Vector3.zero);
                    any = true;
                }
                else
                {
                    bounds.Encapsulate(point);
                }
            }
        }

        return any;
    }

    /// <summary>프리팹 안에서 root 까지 거슬러 올라가며 꺼진 오브젝트가 없는가.</summary>
    private static bool IsActiveUnder(Transform node, Transform root)
    {
        for (Transform t = node; t != null; t = t.parent)
        {
            if (!t.gameObject.activeSelf)
                return false;

            if (t == root)
                return true;
        }

        return true;
    }
}
