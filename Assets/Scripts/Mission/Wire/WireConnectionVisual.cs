using UnityEngine;

public enum WireMeshAxis
{
    X,
    Y,
    Z
}

public class WireConnectionVisual : MonoBehaviour
{
    [Header("전선 Mesh")]
    [SerializeField] private Transform wireMesh;
    [SerializeField] private MeshFilter wireMeshFilter;
    [SerializeField] private Renderer wireRenderer;
    [SerializeField] private WireMeshAxis lengthAxis = WireMeshAxis.Z;

    [Header("드래그 기준")]
    [SerializeField] private Transform wirePlane;

    [Header("색상")]
    [SerializeField] private string colorProperty = "_BaseColor";

    [Header("길이 보정")]
    [SerializeField] private float extraLength = 0.03f;

    [Header("부드러운 이동")]
    [SerializeField] private float followSpeed = 25f;


    private Vector3 startPosition;
    private Vector3 initialScale;
    private Quaternion initialRotation;
    private Vector3 initialAxisWorld;
    private Vector3 targetEndPosition;
    private Vector3 currentEndPosition;
    private float meshLength = 1f;

    private MaterialPropertyBlock propertyBlock;
    private int colorPropertyId;

    public bool IsPreviewing { get; private set; }


    private void Awake()
    {
        if (wireMesh == null)
            return;

        if (wireMeshFilter == null)
            wireMeshFilter = wireMesh.GetComponent<MeshFilter>();

        if (wireRenderer == null)
            wireRenderer = wireMesh.GetComponent<Renderer>();

        initialScale = wireMesh.localScale;
        initialRotation = wireMesh.rotation;
        initialAxisWorld = wireMesh.TransformDirection(GetLocalAxis()).normalized;

        meshLength = GetMeshLength();

        propertyBlock = new MaterialPropertyBlock();
        colorPropertyId = Shader.PropertyToID(colorProperty);

        wireMesh.gameObject.SetActive(false);
    }


    private void LateUpdate()
    {
        if (!IsPreviewing)
            return;

        float t = 1f - Mathf.Exp(-followSpeed * Time.deltaTime);

        currentEndPosition = Vector3.Lerp(currentEndPosition, targetEndPosition, t);

        UpdateWire(startPosition, currentEndPosition);
    }


    public void BeginPreview(Transform start, WireColor color)
    {
        if (wireMesh == null || start == null)
            return;

        startPosition = start.position;
        targetEndPosition = startPosition;
        currentEndPosition = startPosition;

        ApplyColor(color);

        wireMesh.gameObject.SetActive(true);

        IsPreviewing = true;
    }


    public void UpdatePreview(Ray aimRay)
    {
        if (!IsPreviewing || wireMesh == null)
            return;

        Transform planeTransform = wirePlane != null ? wirePlane : transform;

        Plane plane = new Plane(planeTransform.forward, startPosition);

        if (!plane.Raycast(aimRay, out float distance))
            return;

        targetEndPosition = aimRay.GetPoint(distance);
    }


    public void ShowConnection(Transform start, Transform end, WireColor color)
    {
        if (wireMesh == null || start == null || end == null)
            return;

        ApplyColor(color);

        wireMesh.gameObject.SetActive(true);

        UpdateWire(start.position, end.position);

        IsPreviewing = false;
    }


    public void CancelPreview()
    {
        if (!IsPreviewing)
            return;

        Hide();
    }


    public void Hide()
    {
        if (wireMesh != null)
            wireMesh.gameObject.SetActive(false);

        IsPreviewing = false;
    }


    private void UpdateWire(Vector3 start, Vector3 end)
    {
        Vector3 direction = end - start;

        if (direction.sqrMagnitude <= 0.0001f)
            return;

        Vector3 center = (start + end) * 0.5f + direction.normalized * (extraLength * 0.5f);

        wireMesh.position = center;

        wireMesh.rotation =
            Quaternion.FromToRotation(initialAxisWorld, direction.normalized) *
            initialRotation;

        Vector3 scale = initialScale;

        float distance = GetLocalDistance(start, end);
        float axisScale = (distance + extraLength) / meshLength;

        switch (lengthAxis)
        {
            case WireMeshAxis.X:
                scale.x = axisScale;
                break;

            case WireMeshAxis.Y:
                scale.y = axisScale;
                break;

            case WireMeshAxis.Z:
                scale.z = axisScale;
                break;
        }

        wireMesh.localScale = scale;

        // Mesh 자체의 Pivot이 중앙이 아니어도 실제 Mesh 중심을 두 점 가운데로 보정
        if (wireMeshFilter != null && wireMeshFilter.sharedMesh != null)
        {
            Vector3 meshCenter = wireMesh.TransformPoint(
                wireMeshFilter.sharedMesh.bounds.center
            );

            wireMesh.position += center - meshCenter;
        }
    }


    private float GetLocalDistance(Vector3 start, Vector3 end)
    {
        if (wireMesh.parent == null)
            return Vector3.Distance(start, end);

        Vector3 localStart = wireMesh.parent.InverseTransformPoint(start);
        Vector3 localEnd = wireMesh.parent.InverseTransformPoint(end);

        return Vector3.Distance(localStart, localEnd);
    }


    private float GetMeshLength()
    {
        if (wireMeshFilter == null || wireMeshFilter.sharedMesh == null)
            return 1f;

        Vector3 size = wireMeshFilter.sharedMesh.bounds.size;

        switch (lengthAxis)
        {
            case WireMeshAxis.X:
                return Mathf.Max(size.x, 0.0001f);

            case WireMeshAxis.Y:
                return Mathf.Max(size.y, 0.0001f);

            default:
                return Mathf.Max(size.z, 0.0001f);
        }
    }


    private Vector3 GetLocalAxis()
    {
        switch (lengthAxis)
        {
            case WireMeshAxis.X:
                return Vector3.right;

            case WireMeshAxis.Y:
                return Vector3.up;

            default:
                return Vector3.forward;
        }
    }


    private void ApplyColor(WireColor color)
    {
        if (wireRenderer == null)
            return;

        wireRenderer.GetPropertyBlock(propertyBlock);

        propertyBlock.SetColor(
            colorPropertyId,
            WireColorUtility.ToUnityColor(color)
        );

        wireRenderer.SetPropertyBlock(propertyBlock);
    }
}