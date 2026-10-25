using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 인벤토리 슬롯에 보여 줄 아이콘을 돌려준다.
/// - ItemData에 Icon 스프라이트가 있으면 그것을 그대로 쓴다.
/// - 없으면 아이템의 월드 모델(렌더러의 메쉬와 머티리얼)을 작게 한 번 찍어서 스프라이트로 만들고, 아이템 종류별로 캐시한다.
/// 모델은 화면 밖(-5000)에 복사본을 만들어 전용 카메라로 찍으므로 실제 아이템 오브젝트와 게임 화면에는 영향이 없다.
/// </summary>
public static class ItemIconProvider
{
    private const int IconSize = 128;                        //생성하는 아이콘 한 변 크기(px)
    private const int PreviewLayer = 2;                      //미리보기 전용 레이어 (Ignore Raycast). 미리보기 카메라만 이 레이어를 본다
    private static readonly Vector3 PreviewOrigin = new Vector3(0f, -5000f, 0f); //게임 화면 바깥
    private static readonly Quaternion PreviewRotation = Quaternion.Euler(20f, -35f, 0f); //비스듬히 보이는 각도

    private static readonly Dictionary<ItemData, Sprite> cache = new Dictionary<ItemData, Sprite>();
    private static readonly HashSet<ItemData> failed = new HashSet<ItemData>(); //모델이 없어 만들 수 없는 아이템 (매 프레임 재시도 방지)

    public static Sprite Get(ItemData data, GameObject model)
    {
        if (data == null) return null;
        if (data.Icon != null) return data.Icon;

        if (cache.TryGetValue(data, out Sprite cached) && cached != null) return cached;
        if (failed.Contains(data) || model == null) return null;

        Sprite generated = generate(model);
        if (generated == null)
        {
            failed.Add(data);
            Debug.LogWarning($"[ItemIconProvider] '{data.ItemName}'의 미리보기 아이콘을 만들 수 없습니다. ItemData의 Icon에 스프라이트를 넣어 주세요.");
            return null;
        }

        cache[data] = generated;
        return generated;
    }

    private static Sprite generate(GameObject source)
    {
        GameObject root = new GameObject("ItemIconPreview") { layer = PreviewLayer };
        root.transform.position = PreviewOrigin;

        List<Mesh> bakedMeshes = new List<Mesh>();
        List<Renderer> previewRenderers = new List<Renderer>();

        foreach (Renderer sourceRenderer in source.GetComponentsInChildren<Renderer>(true))
        {
            Mesh mesh = null;
            if (sourceRenderer is MeshRenderer)
            {
                MeshFilter filter = sourceRenderer.GetComponent<MeshFilter>();
                mesh = filter != null ? filter.sharedMesh : null;
            }
            else if (sourceRenderer is SkinnedMeshRenderer skinned)
            {
                mesh = new Mesh();
                skinned.BakeMesh(mesh);
                bakedMeshes.Add(mesh);
            }

            if (mesh == null) continue;

            //아이템 루트 기준의 상대 위치·회전·크기를 그대로 복사한다 (현재 손에 들린 자세와 무관)
            Matrix4x4 local = source.transform.worldToLocalMatrix * sourceRenderer.transform.localToWorldMatrix;
            GameObject part = new GameObject(sourceRenderer.name) { layer = PreviewLayer };
            part.transform.SetParent(root.transform, false);
            part.transform.localPosition = local.GetColumn(3);
            part.transform.localRotation = local.rotation;
            part.transform.localScale = local.lossyScale;
            part.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer partRenderer = part.AddComponent<MeshRenderer>();
            partRenderer.sharedMaterials = sourceRenderer.sharedMaterials;
            previewRenderers.Add(partRenderer);
        }

        if (previewRenderers.Count == 0)
        {
            Object.Destroy(root);
            return null;
        }

        root.transform.rotation = PreviewRotation;

        Bounds bounds = previewRenderers[0].bounds;
        for (int i = 1; i < previewRenderers.Count; i++)
            bounds.Encapsulate(previewRenderers[i].bounds);

        float radius = Mathf.Max(bounds.extents.magnitude, 0.01f);

        GameObject cameraObject = new GameObject("ItemIconCamera");
        Camera camera = cameraObject.AddComponent<Camera>();
        camera.enabled = false;
        camera.orthographic = true;
        camera.orthographicSize = radius * 1.05f;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0f, 0f, 0f, 0f);
        camera.cullingMask = 1 << PreviewLayer;
        camera.nearClipPlane = 0.01f;
        camera.farClipPlane = radius * 4f + 1f;
        cameraObject.transform.position = bounds.center + Vector3.back * (radius * 2f);
        cameraObject.transform.rotation = Quaternion.identity;

        RenderTexture target = RenderTexture.GetTemporary(IconSize, IconSize, 16, RenderTextureFormat.ARGB32);
        camera.targetTexture = target;
        camera.Render();

        RenderTexture previous = RenderTexture.active;
        RenderTexture.active = target;
        Texture2D texture = new Texture2D(IconSize, IconSize, TextureFormat.RGBA32, false);
        texture.ReadPixels(new Rect(0f, 0f, IconSize, IconSize), 0, 0);
        texture.Apply();
        RenderTexture.active = previous;

        camera.targetTexture = null;
        RenderTexture.ReleaseTemporary(target);
        Object.Destroy(cameraObject);
        Object.Destroy(root);
        foreach (Mesh baked in bakedMeshes) Object.Destroy(baked);

        //씬을 바꿔도 사라지지 않도록 표시 (사용하지 않는 에셋 정리 대상에서 제외)
        texture.hideFlags = HideFlags.DontSave;
        Sprite sprite = Sprite.Create(texture, new Rect(0f, 0f, IconSize, IconSize), new Vector2(0.5f, 0.5f), 100f);
        sprite.hideFlags = HideFlags.DontSave;
        return sprite;
    }
}