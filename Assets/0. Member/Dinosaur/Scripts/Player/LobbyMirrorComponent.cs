using System.Collections.Generic;
using UnityEngine;

[DefaultExecutionOrder(1000)]
public class LobbyMirrorComponent : MonoBehaviour
{
    [SerializeField] private Renderer surface; //거울 영상을 표시할 면
    [SerializeField, Min(1f)] private float visibleDistance = 7f; //가까운 곳에서 볼 때만 반사 영상 갱신
    [SerializeField, Min(64)] private int textureHeight = 384; //반사 영상의 세로 해상도
    [SerializeField, Range(1f, 60f)] private float framesPerSecond = 30f; //반사 영상 갱신 상한

    private static readonly int feedId = Shader.PropertyToID("_MirrorFeed"); //거울 영상 텍스처
    private static readonly int projectionId = Shader.PropertyToID("_MirrorViewProjection"); //거울 면을 영상에 투영할 행렬
    private readonly List<Renderer> localRenderers = new(); //반사에서만 숨기는 1인칭 팔과 아이템
    private readonly List<bool> rendererStates = new(); //렌더 직후 되돌릴 표시 여부
    private readonly Plane[] frustum = new Plane[6]; //거울이 본인 화면 안에 들어왔는지 확인
    private Camera reflectionCamera; //이 PC의 로컬 플레이어만 위한 반사 카메라
    private RenderTexture reflectionTexture; //씬 종료 때 해제할 반사 영상
    private MaterialPropertyBlock properties; //공유 재질을 유지한 채 현재 영상만 연결
    private float nextRenderTime; //가시 거리 안에서 다음으로 렌더할 시점

    private void LateUpdate() //로컬 카메라의 이동 및 시점 회전 이후 거울 갱신
    {
        Transform listener = PlayerCameraController.LocalListenerTransform;
        if (surface == null || listener == null || !listener.TryGetComponent(out Camera source) ||
            !source.isActiveAndEnabled || Vector3.Dot(listener.position - transform.position, transform.forward) <= 0.05f ||
            (listener.position - transform.position).sqrMagnitude > visibleDistance * visibleDistance ||
            Time.unscaledTime < nextRenderTime)
            return;

        GeometryUtility.CalculateFrustumPlanes(source, frustum);
        if (!GeometryUtility.TestPlanesAABB(frustum, surface.bounds)) return;
        renderReflection(source);
        nextRenderTime = Time.unscaledTime + 1f / framesPerSecond;
    }

    private void renderReflection(Camera source) //거울 뒤에 반사된 시점을 놓고 거울 앞쪽 공간만 촬영
    {
        if (reflectionCamera == null)
        {
            reflectionCamera = new GameObject("Lobby mirror camera", typeof(Camera)).GetComponent<Camera>();
            reflectionCamera.gameObject.hideFlags = HideFlags.DontSave;
            reflectionCamera.enabled = false;
            reflectionCamera.useOcclusionCulling = false;
            reflectionCamera.allowMSAA = false;
            reflectionCamera.stereoTargetEye = StereoTargetEyeMask.None;
            properties = new MaterialPropertyBlock();
        }
        int width = Mathf.Max(64, Mathf.RoundToInt(textureHeight * source.aspect));
        if (reflectionTexture == null || reflectionTexture.width != width || reflectionTexture.height != textureHeight)
        {
            releaseTexture();
            reflectionTexture = new RenderTexture(width, textureHeight, 24) { name = "Lobby mirror reflection" };
            reflectionCamera.targetTexture = reflectionTexture;
        }

        Vector3 normal = transform.forward;
        Transform view = source.transform;
        Vector3 position = view.position - 2f * Vector3.Dot(view.position - transform.position, normal) * normal;
        reflectionCamera.transform.SetPositionAndRotation(position,
            Quaternion.LookRotation(Vector3.Reflect(view.forward, normal), Vector3.Reflect(view.up, normal)));
        reflectionCamera.clearFlags = source.clearFlags;
        reflectionCamera.backgroundColor = source.backgroundColor;
        reflectionCamera.cullingMask = source.cullingMask & ~(1 << 2 | 1 << 5 | 1 << 29 | 1 << 31);
        reflectionCamera.nearClipPlane = source.nearClipPlane;
        reflectionCamera.farClipPlane = source.farClipPlane;
        reflectionCamera.aspect = source.aspect;
        reflectionCamera.fieldOfView = source.fieldOfView;
        reflectionCamera.orthographic = source.orthographic;
        reflectionCamera.orthographicSize = source.orthographicSize;
        Matrix4x4 projection = source.projectionMatrix;
        projection.m02 = -projection.m02; //비대칭 렌즈도 좌우를 뒤집어 투영
        reflectionCamera.projectionMatrix = projection;

        Matrix4x4 cameraView = reflectionCamera.worldToCameraMatrix;
        Vector3 clipPosition = cameraView.MultiplyPoint(transform.position + normal * 0.02f);
        Vector3 clipNormal = cameraView.MultiplyVector(normal).normalized;
        reflectionCamera.projectionMatrix = reflectionCamera.CalculateObliqueMatrix(
            new Vector4(clipNormal.x, clipNormal.y, clipNormal.z, -Vector3.Dot(clipPosition, clipNormal)));

        PlayerLookVisualComponent visuals = view.GetComponentInParent<PlayerLookVisualComponent>();
        localRenderers.Clear();
        rendererStates.Clear();
        view.GetComponentsInChildren(true, localRenderers);
        localRenderers.Add(surface);
        foreach (Renderer renderer in localRenderers)
        {
            rendererStates.Add(renderer.enabled);
            renderer.enabled = false;
        }
        try
        {
            visuals?.beginExternalView();
            reflectionCamera.Render();
        }
        finally
        {
            visuals?.endExternalView();
            for (int i = 0; i < localRenderers.Count; i++)
                if (localRenderers[i] != null) localRenderers[i].enabled = rendererStates[i];
        }

        surface.GetPropertyBlock(properties);
        properties.SetTexture(feedId, reflectionTexture);
        properties.SetMatrix(projectionId, GL.GetGPUProjectionMatrix(reflectionCamera.projectionMatrix, true) * cameraView);
        surface.SetPropertyBlock(properties);
    }

    private void releaseTexture() //해상도 변경 및 씬 종료 시 로컬 영상 해제
    {
        if (reflectionTexture == null) return;
        if (reflectionCamera != null) reflectionCamera.targetTexture = null;
        reflectionTexture.Release();
        Destroy(reflectionTexture);
        reflectionTexture = null;
    }

    private void OnDisable() //씬 종료 후 반사 카메라와 화면 참조 정리
    {
        if (surface != null && properties != null)
        {
            surface.GetPropertyBlock(properties);
            properties.SetTexture(feedId, Texture2D.blackTexture);
            surface.SetPropertyBlock(properties);
        }
        releaseTexture();
        if (reflectionCamera != null) Destroy(reflectionCamera.gameObject);
        reflectionCamera = null;
        nextRenderTime = 0f;
    }
}
