using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// [에디터 전용] 미션 오브젝트에 "조준하면 켜지는 외곽선"을 붙인다.
///
/// [방식 — 팀이 밸브에 쓴 방식 그대로] 대상 메시를 하나 더 복제해 살짝 키우고, 앞면을 그리지 않는(Cull Front)
///  흰색 발광 머티리얼을 입힌다 → 원래 모델 가장자리에 흰 테두리만 보인다.
///  켜고 끄는 건 Player 프리팹에 이미 있는 PlayerMissionPromptController 가 한다:
///  조준 대상(ITargetable)의 부모에서 MissionPrompt 를 찾아 Show/Hide → 여기서 만든 외곽선 오브젝트가 켜지고 꺼진다.
///  그래서 플레이어 코드는 수정하지 않는다.
///
/// [머티리얼] 현우님 폴더의 obj_Outline.mat 을 우리 폴더로 복사해서 쓴다.
///  다른 사람 개인 폴더를 직접 참조하면, 원본이 옮겨지거나 지워질 때 우리 프리팹이 깨지기 때문이다.
///
/// [두께] 확대 비율을 축마다 계산해 오브젝트 크기와 상관없이 테두리가 약 2cm 가 되게 한다.
///  (같은 비율을 쓰면 14cm 발전기 버튼은 테두리가 거의 안 보이고, 1.2m 큐브는 너무 두꺼워진다)
///  메시 중심을 기준으로 키우므로 피벗이 중심에 있지 않은 메시도 테두리가 한쪽으로 쏠리지 않는다.
/// [서브메시] 재질이 여러 개인 메시는 서브메시 수만큼 외곽선 재질을 넣는다 (2b — 전선 패널).
/// [부품이 큰 메시의 일부일 때 — AttachSubmesh] 전선 꽂이처럼 부품 모양이 패널 메시의 서브메시 하나인 경우,
///  그 서브메시만 떼어 작은 메시 에셋(MissionRedesign_Draft/Meshes)으로 저장하고 그걸로 외곽선을 만든다 → 조준한 부품만 빛난다.
///  (2b 테스트에서 발견: 꽂이 오브젝트에 자기 메시가 없어 패널 전체가 빛나서 어느 선을 옮기는지 알 수 없었다)
/// [외곽선 없음 — AttachEmpty] 떼어낼 모양이 없는 부품(전선 도착점)은 빈 MissionPrompt 를 둔다.
///  감지기는 가장 가까운 MissionPrompt 를 쓰므로, 이게 없으면 부모(스테이션 전체) 외곽선이 켜진다.
/// </summary>
public static class MissionOutlineBuilder
{
    private const string SourceMaterialPath = "Assets/0. Member/Hyunwoo/HW_Material/obj_Outline.mat";
    private const string MaterialFolder = "Assets/0. Member/SuHan/MissionRedesign_Draft/Materials";
    private const string MaterialPath = MaterialFolder + "/MissionOutline.mat";
    private const float OutlineThickness = 0.02f;
    private const string OutlineObjectName = "Outline (조준 시 표시)";
    private const string MeshFolder = "Assets/0. Member/SuHan/MissionRedesign_Draft/Meshes";

    /// <summary>
    /// meshSource(또는 그 자식)의 첫 메시에 외곽선 복제본을 붙이고, promptRoot 에 MissionPrompt(외곽선만 표시)를 설정한다.
    /// 머티리얼이나 메시가 없으면 경고만 하고 넘어간다 (외곽선은 연출이라 없어도 미션은 동작한다).
    /// </summary>
    public static void Attach(GameObject promptRoot, GameObject meshSource)
    {
        Material material = GetOrCreateMaterial();

        if (material == null)
            return;

        MeshFilter meshFilter = meshSource != null ? meshSource.GetComponentInChildren<MeshFilter>(true) : null;

        if (meshFilter == null || meshFilter.sharedMesh == null)
        {
            Debug.LogWarning($"[MissionOutlineBuilder] '{(meshSource != null ? meshSource.name : "null")}' 에 메시가 없어 외곽선을 붙이지 않습니다.");
            return;
        }

        GameObject outline = CreateOutlineObject(meshFilter.transform, meshFilter.sharedMesh, material, OutlineObjectName);
        ConfigurePrompt(promptRoot, outline);
    }

    /// <summary>
    /// source 메시의 서브메시 하나(submeshIndex)만 떼어 외곽선을 만들고, promptRoot 에 MissionPrompt 를 설정한다.
    /// 떼어낸 메시는 meshAssetName 으로 저장한다 (다시 실행하면 같은 에셋을 덮어써 GUID 가 유지된다).
    /// 외곽선 오브젝트는 source 의 자식이라 원래 모델과 정확히 겹친다.
    /// </summary>
    public static void AttachSubmesh(GameObject promptRoot, MeshFilter source, int submeshIndex, string meshAssetName)
    {
        Material material = GetOrCreateMaterial();

        if (material == null)
            return;

        if (source == null || source.sharedMesh == null || submeshIndex < 0 || submeshIndex >= source.sharedMesh.subMeshCount)
        {
            Debug.LogWarning($"[MissionOutlineBuilder] '{promptRoot.name}': 서브메시 {submeshIndex} 를 찾지 못해 외곽선 없이 둡니다.");
            AttachEmpty(promptRoot);
            return;
        }

        Mesh part = SaveSubmeshAsset(source.sharedMesh, submeshIndex, meshAssetName);
        GameObject outline = CreateOutlineObject(source.transform, part, material, $"{OutlineObjectName} {meshAssetName}");
        ConfigurePrompt(promptRoot, outline);
    }

    /// <summary>조준해도 아무 외곽선도 켜지지 않게 한다 (부모 스테이션의 외곽선이 대신 켜지는 것을 막음).</summary>
    public static void AttachEmpty(GameObject promptRoot)
    {
        ConfigurePrompt(promptRoot, null);
    }

    private static void ConfigurePrompt(GameObject promptRoot, GameObject outline)
    {
        MissionPrompt prompt = promptRoot.GetComponent<MissionPrompt>();

        if (prompt == null)
            prompt = promptRoot.AddComponent<MissionPrompt>();

        SerializedObject so = new SerializedObject(prompt);
        so.FindProperty("mode").intValue = (int)MissionPromptMode.HighlightOnly;
        so.FindProperty("highlightObject").objectReferenceValue = outline;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <summary>
    /// 서브메시 하나를 독립 메시로 떼어 에셋으로 저장한다.
    /// 쓰이는 정점만 남긴다 — 정점을 통째로 두면 bounds 가 원래 메시 전체 크기가 되어 외곽선 두께 · 중심 계산이 틀어진다.
    /// </summary>
    private static Mesh SaveSubmeshAsset(Mesh source, int submeshIndex, string meshAssetName)
    {
        int[] triangles = source.GetTriangles(submeshIndex);
        Vector3[] vertices = source.vertices;
        Vector3[] normals = source.normals;
        Vector2[] uvs = source.uv;

        Dictionary<int, int> remap = new Dictionary<int, int>();
        List<Vector3> newVertices = new List<Vector3>();
        List<Vector3> newNormals = new List<Vector3>();
        List<Vector2> newUvs = new List<Vector2>();
        int[] newTriangles = new int[triangles.Length];

        for (int i = 0; i < triangles.Length; i++)
        {
            int oldIndex = triangles[i];

            if (!remap.TryGetValue(oldIndex, out int newIndex))
            {
                newIndex = newVertices.Count;
                remap.Add(oldIndex, newIndex);
                newVertices.Add(vertices[oldIndex]);

                if (normals.Length == vertices.Length)
                    newNormals.Add(normals[oldIndex]);

                if (uvs.Length == vertices.Length)
                    newUvs.Add(uvs[oldIndex]);
            }

            newTriangles[i] = newIndex;
        }

        Mesh part = new Mesh { name = meshAssetName };

        if (newVertices.Count > 65535)
            part.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;

        part.SetVertices(newVertices);

        if (newNormals.Count == newVertices.Count)
            part.SetNormals(newNormals);

        if (newUvs.Count == newVertices.Count)
            part.SetUVs(0, newUvs);

        part.SetTriangles(newTriangles, 0);
        part.RecalculateBounds();

        EnsureMeshFolder();
        string path = $"{MeshFolder}/{meshAssetName}.asset";
        Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);

        if (existing == null)
        {
            AssetDatabase.CreateAsset(part, path);
            return part;
        }

        EditorUtility.CopySerialized(part, existing);
        Object.DestroyImmediate(part);
        return existing;
    }

    private static void EnsureMeshFolder()
    {
        if (!AssetDatabase.IsValidFolder(MeshFolder))
            AssetDatabase.CreateFolder("Assets/0. Member/SuHan/MissionRedesign_Draft", "Meshes");
    }

    private static GameObject CreateOutlineObject(Transform parent, Mesh mesh, Material material, string objectName)
    {
        Transform existing = parent.Find(objectName);

        if (existing != null)
            Object.DestroyImmediate(existing.gameObject);

        GameObject outline = new GameObject(objectName);
        outline.layer = parent.gameObject.layer;
        outline.transform.SetParent(parent, false);

        Vector3 center = mesh.bounds.center;
        Vector3 localSize = mesh.bounds.size;
        Vector3 worldScale = parent.lossyScale;

        // 축마다: 월드 크기 기준으로 양쪽에 thickness 만큼 커지는 비율
        Vector3 scale = new Vector3(
            ScaleFor(localSize.x * Mathf.Abs(worldScale.x)),
            ScaleFor(localSize.y * Mathf.Abs(worldScale.y)),
            ScaleFor(localSize.z * Mathf.Abs(worldScale.z)));

        outline.transform.localScale = scale;
        // 메시 중심을 기준으로 키우기: v' = s*v + p 가 s*(v - c) + c 가 되도록 p = c - s*c
        outline.transform.localPosition = center - Vector3.Scale(scale, center);

        outline.AddComponent<MeshFilter>().sharedMesh = mesh;

        MeshRenderer renderer = outline.AddComponent<MeshRenderer>();

        // 재질이 여러 개인 메시(예: 전선 패널)는 서브메시마다 재질이 있어야 전체 외곽선이 그려진다
        Material[] materials = new Material[Mathf.Max(1, mesh.subMeshCount)];

        for (int i = 0; i < materials.Length; i++)
            materials[i] = material;

        renderer.sharedMaterials = materials;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;

        // 평소에는 꺼져 있다 (MissionPrompt.Awake 도 끄지만, 프리팹 상태부터 꺼 둔다)
        outline.SetActive(false);
        return outline;
    }

    private static float ScaleFor(float worldSize)
    {
        return 1f + (2f * OutlineThickness) / Mathf.Max(worldSize, 0.001f);
    }

    private static Material GetOrCreateMaterial()
    {
        Material existing = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);

        if (existing != null)
            return existing;

        if (AssetDatabase.LoadAssetAtPath<Material>(SourceMaterialPath) == null)
        {
            Debug.LogWarning($"[MissionOutlineBuilder] 외곽선 원본 머티리얼이 없어 외곽선을 건너뜁니다: {SourceMaterialPath}");
            return null;
        }

        if (!AssetDatabase.IsValidFolder(MaterialFolder))
            AssetDatabase.CreateFolder("Assets/0. Member/SuHan/MissionRedesign_Draft", "Materials");

        AssetDatabase.CopyAsset(SourceMaterialPath, MaterialPath);
        return AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
    }
}
