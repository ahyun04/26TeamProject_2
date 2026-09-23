using TrustNoOne.Missions;
using UnityEngine;

/// <summary>
/// [테스트 전용] 에셋 없이 큐브를 구분하기 위한 이름표 + 색.
///  - 이름표: Unity 내장 폰트(LegacyRuntime.ttf)로 만든 TextMesh. 항상 화면 카메라를 바라본다.
///  - 색: MaterialPropertyBlock 으로 칠한다. 머티리얼 에셋을 새로 만들지 않기 위해서다.
///  - MissionInteractable 이 붙어 있으면 상태를 이름표에 덧붙인다: 홀드 진행률 / [완료] / (내 미션 아님).
///
/// [이름표를 자식으로 두지 않는 이유] 큐브는 (1, 2, 1)처럼 늘려서 쓰는데, 자식으로 두면 글자까지 같이 늘어난다.
///  그래서 별도 오브젝트로 만들고 위치만 따라가게 했다.
/// </summary>
public class TestCubeLabel : MonoBehaviour
{
    [SerializeField, TextArea] private string text;
    [SerializeField] private Color color = Color.white;

    [Tooltip("기존 프리팹 모델처럼 원래 색을 살려야 하면 끈다.")]
    [SerializeField] private bool tintRenderers = true;

    [SerializeField] private float heightOffset = 0.4f;

    private static readonly Color ConsumedColor = new Color(0.35f, 0.35f, 0.35f);
    private static Camera viewCamera;
    private static float nextCameraSearchTime;

    /// <summary>이름표 첫 줄 (예: "발전기"). 홀드 게이지 제목으로 쓴다.</summary>
    public string Title
    {
        get
        {
            if (string.IsNullOrEmpty(text))
                return name;

            int lineBreak = text.IndexOf('\n');
            return lineBreak < 0 ? text : text.Substring(0, lineBreak);
        }
    }

    private GameObject labelObject;
    private TextMesh textMesh;
    private MissionInteractable interactable;
    private Collider[] colliders;
    private Renderer[] tintTargets;
    private Vector3 labelOffset;
    private bool consumedTintApplied;

    private void Start()
    {
        interactable = GetComponent<MissionInteractable>();
        colliders = GetComponentsInChildren<Collider>(true);
        tintTargets = GetComponentsInChildren<Renderer>(true);

        labelOffset = ComputeLabelOffset();
        CreateLabel();

        if (tintRenderers)
            ApplyTint(color);
    }

    private void OnDestroy()
    {
        if (labelObject != null)
            Destroy(labelObject);
    }

    private void LateUpdate()
    {
        if (labelObject == null)
            return;

        labelObject.transform.position = transform.position + labelOffset;
        textMesh.text = text + BuildStatus();

        Camera cam = GetViewCamera();

        if (cam != null)
            labelObject.transform.rotation = Quaternion.LookRotation(labelObject.transform.position - cam.transform.position);
    }

    private string BuildStatus()
    {
        if (interactable == null || interactable.Object == null || !interactable.Object.IsValid)
            return string.Empty;

        if (interactable.Consumed)
        {
            if (tintRenderers && !consumedTintApplied)
            {
                ApplyTint(ConsumedColor);
                consumedTintApplied = true;
            }

            return "\n[완료]";
        }

        if (interactable.IsUsedByLocalPlayer)
            return $"\n진행 {Mathf.RoundToInt(interactable.HoldProgress01 * 100f)}%";

        // MissionInteractable 이 "내 목표가 아니면" 콜라이더를 끈다. 그 상태를 그대로 보여준다.
        return AnyColliderEnabled() ? string.Empty : "\n(내 미션 아님)";
    }

    private bool AnyColliderEnabled()
    {
        if (colliders == null || colliders.Length == 0)
            return true;

        foreach (Collider c in colliders)
        {
            if (c != null && c.enabled)
                return true;
        }

        return false;
    }

    private Vector3 ComputeLabelOffset()
    {
        Renderer[] renderers = GetComponentsInChildren<Renderer>(true);

        if (renderers.Length == 0)
            return Vector3.up * (1f + heightOffset);

        Bounds bounds = renderers[0].bounds;

        for (int i = 1; i < renderers.Length; i++)
            bounds.Encapsulate(renderers[i].bounds);

        return new Vector3(0f, bounds.max.y - transform.position.y + heightOffset, 0f);
    }

    private void CreateLabel()
    {
        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        labelObject = new GameObject($"{name} Label");
        textMesh = labelObject.AddComponent<TextMesh>();
        textMesh.font = font;
        textMesh.fontSize = 64;
        textMesh.characterSize = 0.025f;
        textMesh.anchor = TextAnchor.LowerCenter;
        textMesh.alignment = TextAlignment.Center;
        textMesh.color = Color.white;
        textMesh.richText = false;
        textMesh.text = text;

        labelObject.GetComponent<MeshRenderer>().sharedMaterial = font.material;
        labelObject.transform.position = transform.position + labelOffset;
    }

    private void ApplyTint(Color tint)
    {
        MaterialPropertyBlock block = new MaterialPropertyBlock();

        foreach (Renderer r in tintTargets)
        {
            if (r == null)
                continue;

            r.GetPropertyBlock(block);
            block.SetColor("_BaseColor", tint);   // URP Lit
            block.SetColor("_Color", tint);       // Built-in Standard
            r.SetPropertyBlock(block);
        }
    }

    /// <summary>
    /// 플레이어 카메라는 MainCamera 태그가 없어서 Camera.main 을 쓸 수 없다.
    /// 대신 켜져 있는 카메라 중 가장 위에 그려지는(depth 가 가장 큰) 카메라를 쓴다. 0.5초마다 다시 찾는다.
    /// </summary>
    private static Camera GetViewCamera()
    {
        if (viewCamera != null && viewCamera.isActiveAndEnabled && Time.unscaledTime < nextCameraSearchTime)
            return viewCamera;

        nextCameraSearchTime = Time.unscaledTime + 0.5f;
        viewCamera = null;

        foreach (Camera cam in Camera.allCameras)
        {
            if (viewCamera == null || cam.depth > viewCamera.depth)
                viewCamera = cam;
        }

        return viewCamera;
    }
}
