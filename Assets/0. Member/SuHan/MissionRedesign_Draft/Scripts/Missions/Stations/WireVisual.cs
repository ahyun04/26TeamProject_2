using UnityEngine;

namespace TrustNoOne.Missions
{
    /// <summary>전선 메시가 어느 로컬 축으로 길쭉한가 (옛 WireMeshAxis 와 같은 값).</summary>
    public enum WireAxis
    {
        X = 0,
        Y = 1,
        Z = 2,
    }

    /// <summary>
    /// [역할] 전선 한 가닥의 모습: 꽂이(시작점) 색 + 끄는 동안 따라오는 미리보기 + 연결된 선. 2b 명세 2-8.
    ///  옛 WireConnectionVisual(전선 메시 늘리기 · 드래그 평면)과 WireStartPoint 의 색 표시(패널 렌더러 + 재질 번호)를 합쳤다.
    ///  상태는 WiringStation 이 준다 (SetColor / ShowConnection / Hide). 미리보기는 StationDragPart 가 부른다 (DragPreview).
    ///
    /// [메시 늘리기] 옛 UpdateWire 계산 그대로: 두 점 가운데에 두고, 길이 축을 두 점 방향으로 돌리고, 길이 축 스케일로 늘린 뒤
    ///  메시 피벗이 가운데가 아니어도 실제 메시 중심이 두 점 가운데에 오게 보정한다.
    /// [옛 코드와 다른 점 — 명세 W6] 처음 회전 · 길이 축을 "부모 기준"으로 저장한다.
    ///  Fusion 기본 스폰은 Instantiate(prefab) 후 위치 · 회전을 나중에 넣기 때문에(NetworkObjectProviderDefault.cs:75),
    ///  옛 코드처럼 Awake 에서 월드 회전을 저장하면 돌려서 배치한 스테이션에서 전선 방향이 어긋날 수 있다.
    /// [미리보기 색] 마지막 SetColor 값을 쓴다 → 끄는 부품은 색을 몰라도 된다.
    /// </summary>
    public class WireVisual : DragPreview
    {
        [Header("꽂이 (시작점) 색 — 옛 WireStartPoint")]
        [SerializeField] private Transform startAnchor;
        [SerializeField] private Renderer plugRenderer;
        [SerializeField] private int plugMaterialIndex;
        [SerializeField] private string plugColorProperty = "_BaseColor";

        [Header("전선 메시 — 옛 WireConnectionVisual")]
        [SerializeField] private Transform wireMesh;
        [SerializeField] private MeshFilter wireMeshFilter;
        [SerializeField] private Renderer wireRenderer;
        [SerializeField] private WireAxis lengthAxis = WireAxis.Z;
        [SerializeField] private string wireColorProperty = "_BaseColor";

        [Tooltip("드래그 평면. 이 Transform 의 forward 가 평면의 법선이다. 비우면 이 오브젝트.")]
        [SerializeField] private Transform wirePlane;

        [SerializeField] private float extraLength = 0.03f;

        [Tooltip("미리보기 끝이 조준점을 따라가는 빠르기.")]
        [SerializeField] private float followSpeed = 25f;

        private Quaternion initialLocalRotation;
        private Vector3 initialLocalAxis;
        private Vector3 initialScale;
        private float meshLength = 1f;
        private bool initialized;

        private Vector3 previewStart;
        private Vector3 targetEnd;
        private Vector3 currentEnd;

        private MaterialPropertyBlock propertyBlock;
        private Color currentColor = Color.white;

        public bool IsPreviewing { get; private set; }

        private Transform StartAnchor => startAnchor != null ? startAnchor : transform;

        private void Awake()
        {
            EnsureInitialized();
        }

        private void EnsureInitialized()
        {
            if (initialized || wireMesh == null)
                return;

            initialized = true;

            if (wireMeshFilter == null)
                wireMeshFilter = wireMesh.GetComponent<MeshFilter>();

            if (wireRenderer == null)
                wireRenderer = wireMesh.GetComponent<Renderer>();

            // 부모 기준으로 저장 (W6)
            initialScale = wireMesh.localScale;
            initialLocalRotation = wireMesh.localRotation;
            initialLocalAxis = (wireMesh.localRotation * LocalAxis()).normalized;
            meshLength = MeshLength();

            wireMesh.gameObject.SetActive(false);
        }

        private void LateUpdate()
        {
            if (!IsPreviewing)
                return;

            float t = 1f - Mathf.Exp(-followSpeed * Time.deltaTime);
            currentEnd = Vector3.Lerp(currentEnd, targetEnd, t);
            UpdateWire(previewStart, currentEnd);
        }

        // ─── 상태 (WiringStation 이 부름) ───

        public void SetColor(WiringColor color)
        {
            currentColor = ToUnityColor(color);
            ApplyPlugColor();
            ApplyWireColor();
        }

        public void ShowConnection(Transform end)
        {
            EnsureInitialized();

            if (wireMesh == null || end == null)
                return;

            IsPreviewing = false;
            ApplyWireColor();
            wireMesh.gameObject.SetActive(true);
            UpdateWire(StartAnchor.position, end.position);
        }

        public void Hide()
        {
            if (wireMesh != null)
                wireMesh.gameObject.SetActive(false);

            IsPreviewing = false;
        }

        // ─── 미리보기 (StationDragPart 가 부름) ───

        public override void BeginPreview()
        {
            EnsureInitialized();

            if (wireMesh == null)
                return;

            previewStart = StartAnchor.position;
            targetEnd = previewStart;
            currentEnd = previewStart;

            ApplyWireColor();
            wireMesh.gameObject.SetActive(true);
            IsPreviewing = true;
        }

        public override void UpdatePreview(Ray aimRay)
        {
            if (!IsPreviewing)
                return;

            Transform planeTransform = wirePlane != null ? wirePlane : transform;
            Plane plane = new Plane(planeTransform.forward, previewStart);

            if (plane.Raycast(aimRay, out float distance))
                targetEnd = aimRay.GetPoint(distance);
        }

        public override void CancelPreview()
        {
            if (!IsPreviewing)
                return;

            Hide();
        }

        // ─── 메시 늘리기 (옛 WireConnectionVisual.UpdateWire) ───

        private void UpdateWire(Vector3 start, Vector3 end)
        {
            Vector3 direction = end - start;

            if (direction.sqrMagnitude <= 0.0001f)
                return;

            Vector3 directionNormalized = direction.normalized;
            Vector3 center = (start + end) * 0.5f + directionNormalized * (extraLength * 0.5f);

            // 부모의 "지금" 회전으로 처음 회전 · 축을 월드로 바꾼다 (W6)
            Quaternion parentRotation = wireMesh.parent != null ? wireMesh.parent.rotation : Quaternion.identity;
            Vector3 axisWorld = parentRotation * initialLocalAxis;

            wireMesh.position = center;
            wireMesh.rotation = Quaternion.FromToRotation(axisWorld, directionNormalized) * (parentRotation * initialLocalRotation);

            Vector3 scale = initialScale;
            float axisScale = (LocalDistance(start, end) + extraLength) / meshLength;

            switch (lengthAxis)
            {
                case WireAxis.X: scale.x = axisScale; break;
                case WireAxis.Y: scale.y = axisScale; break;
                default: scale.z = axisScale; break;
            }

            wireMesh.localScale = scale;

            // 메시 피벗이 가운데가 아니어도 실제 메시 중심을 두 점 가운데로
            if (wireMeshFilter != null && wireMeshFilter.sharedMesh != null)
            {
                Vector3 meshCenter = wireMesh.TransformPoint(wireMeshFilter.sharedMesh.bounds.center);
                wireMesh.position += center - meshCenter;
            }
        }

        private float LocalDistance(Vector3 start, Vector3 end)
        {
            if (wireMesh.parent == null)
                return Vector3.Distance(start, end);

            return Vector3.Distance(wireMesh.parent.InverseTransformPoint(start), wireMesh.parent.InverseTransformPoint(end));
        }

        private float MeshLength()
        {
            if (wireMeshFilter == null || wireMeshFilter.sharedMesh == null)
                return 1f;

            Vector3 size = wireMeshFilter.sharedMesh.bounds.size;

            switch (lengthAxis)
            {
                case WireAxis.X: return Mathf.Max(size.x, 0.0001f);
                case WireAxis.Y: return Mathf.Max(size.y, 0.0001f);
                default: return Mathf.Max(size.z, 0.0001f);
            }
        }

        private Vector3 LocalAxis()
        {
            switch (lengthAxis)
            {
                case WireAxis.X: return Vector3.right;
                case WireAxis.Y: return Vector3.up;
                default: return Vector3.forward;
            }
        }

        // ─── 색 ───

        private void ApplyPlugColor()
        {
            if (plugRenderer == null)
                return;

            if (propertyBlock == null)
                propertyBlock = new MaterialPropertyBlock();

            // 시작점 넷이 패널 렌더러 하나를 공유하므로 "내 재질 번호"에만 칠한다 (옛 WireStartPoint 와 같음)
            plugRenderer.GetPropertyBlock(propertyBlock, plugMaterialIndex);
            propertyBlock.SetColor(plugColorProperty, currentColor);
            plugRenderer.SetPropertyBlock(propertyBlock, plugMaterialIndex);
        }

        private void ApplyWireColor()
        {
            if (wireRenderer == null)
                return;

            if (propertyBlock == null)
                propertyBlock = new MaterialPropertyBlock();

            wireRenderer.GetPropertyBlock(propertyBlock);
            propertyBlock.SetColor(wireColorProperty, currentColor);
            wireRenderer.SetPropertyBlock(propertyBlock);
        }

        private static Color ToUnityColor(WiringColor color)
        {
            switch (color)
            {
                case WiringColor.Red: return Color.red;
                case WiringColor.Blue: return Color.blue;
                case WiringColor.Green: return Color.green;
                case WiringColor.Yellow: return Color.yellow;
                default: return Color.white;
            }
        }
    }
}
