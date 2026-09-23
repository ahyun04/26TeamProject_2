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
/// </summary>
public static class MissionOutlineBuilder
{
    private const string SourceMaterialPath = "Assets/0. Member/Hyunwoo/HW_Material/obj_Outline.mat";
    private const string MaterialFolder = "Assets/0. Member/SuHan/MissionRedesign_Draft/Materials";
    private const string MaterialPath = MaterialFolder + "/MissionOutline.mat";
    private const float OutlineThickness = 0.02f;
    private const string OutlineObjectName = "Outline (조준 시 표시)";

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

        GameObject outline = CreateOutlineObject(meshFilter, material);

        MissionPrompt prompt = promptRoot.GetComponent<MissionPrompt>();

        if (prompt == null)
            prompt = promptRoot.AddComponent<MissionPrompt>();

        SerializedObject so = new SerializedObject(prompt);
        so.FindProperty("mode").intValue = (int)MissionPromptMode.HighlightOnly;
        so.FindProperty("highlightObject").objectReferenceValue = outline;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    private static GameObject CreateOutlineObject(MeshFilter source, Material material)
    {
        Transform parent = source.transform;
        Transform existing = parent.Find(OutlineObjectName);

        if (existing != null)
            Object.DestroyImmediate(existing.gameObject);

        GameObject outline = new GameObject(OutlineObjectName);
        outline.layer = parent.gameObject.layer;
        outline.transform.SetParent(parent, false);

        Mesh mesh = source.sharedMesh;
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
        renderer.sharedMaterial = material;
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
