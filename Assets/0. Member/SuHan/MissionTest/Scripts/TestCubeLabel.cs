using TrustNoOne.Missions;
using UnityEngine;

/// <summary>
/// [테스트 전용] 에셋 없이 큐브를 구분하기 위한 이름표 + 색.
///  - 이름표: Unity 내장 폰트(LegacyRuntime.ttf)로 만든 TextMesh. 항상 화면 카메라를 바라본다.
///  - 색: MaterialPropertyBlock 으로 칠한다. 머티리얼 에셋을 새로 만들지 않기 위해서다.
///  - MissionStation 이 붙어 있으면 상태를 덧붙인다: HoldStation 진행률 / [완료] / (지금 할 미션 아님)
///
/// [이름표를 자식으로 두지 않는 이유] 큐브는 (1, 2, 1)처럼 늘려서 쓰는데, 자식으로 두면 글자까지 같이 늘어난다.
/// [색 되돌리기] 잠김(Lock)이 제한 시간 초기화로 풀리면 원래 색으로 돌아간다.
/// </summary>
public class TestCubeLabel : MonoBehaviour
{
    [SerializeField, TextArea] private string text;
    [SerializeField] private Color color = Color.white;

    [Tooltip("기존 프리팹 모델처럼 원래 색을 살려야 하면 끈다.")]
    [SerializeField] private bool tintRenderers = true;

    [SerializeField] private float heightOffset = 0.4f;

    private static readonly Color CompletedColor = new Color(0.35f, 0.35f, 0.35f);
    private static Camera viewCamera;
    private static float nextCameraSearchTime;

    /// <summary>이름표 첫 줄 (예: "밸브 잠그기"). 홀드 게이지 제목으로 쓴다.</summary>
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
    private MissionStation station;
    private Collider[] colliders;
    private Renderer[] tintTargets;
    private Vector3 labelOffset;
    private bool? lastCompleted;

    private void Start()
    {
        station = GetComponent<MissionStation>();
        colliders = GetComponentsInChildren<Collider>(true);
        // 켜져 있는 렌더러만: 평소에 꺼져 있는 조준 외곽선(MissionPrompt)은 칠하지 않는다 (흰색 발광이 유지돼야 함)
        tintTargets = GetComponentsInChildren<Renderer>(false);

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
        if (station == null || station.Object == null || !station.Object.IsValid)
            return string.Empty;

        bool completed = station.Completed;

        if (tintRenderers && lastCompleted != completed)
        {
            ApplyTint(completed ? CompletedColor : color);
            lastCompleted = completed;
        }

        if (completed)
            return "\n[완료]";

        if (station is HoldStation hold && hold.IsOperatedByLocalPlayer)
            return $"\n진행 {Mathf.RoundToInt(hold.HoldProgress01 * 100f)}%";

        // MissionStation 이 "지금 내가 할 미션이 아니면" 콜라이더를 끈다 (개인용은 내가 끝낸 뒤에도 해당).
        return AnyColliderEnabled() ? string.Empty : "\n(지금 할 미션 아님)";
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
        Renderer[] renderers = GetComponentsInChildren<Renderer>(false);

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
