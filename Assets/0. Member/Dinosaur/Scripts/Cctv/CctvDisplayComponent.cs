using System.Collections.Generic;
using TMPro;
using UnityEngine;

[DefaultExecutionOrder(1000)]
public class CctvDisplayComponent : MonoBehaviour
{
    [SerializeField] private Renderer screen; //기계 모니터의 영상 표면
    [SerializeField] private Camera[] cameras; //구역 순서대로 연결할 고정 카메라
    [SerializeField] private string[] areaNames; //CAM 번호와 함께 표시할 구역 이름
    [SerializeField] private Light[] indicators; //해당 카메라 이용 중 점멸할 빨간 표시등
    [SerializeField] private Renderer[] indicatorBulbs; //표시등이 꺼졌을 때 어둡게 표시할 전구 메시
    [SerializeField] private TMP_Text caption; //모니터의 현재 구역과 조작 안내
    [SerializeField, Min(64)] private int textureWidth = 768; //클라이언트별 영상 가로 해상도
    [SerializeField, Min(64)] private int textureHeight = 432; //클라이언트별 영상 세로 해상도
    [SerializeField, Range(1f, 30f)] private float framesPerSecond = 12f; //항상 켜진 모니터의 갱신 빈도

    private Cctv owner; //공유 화면의 진입점
    private RenderTexture texture; //각 PC에서만 생성하는 영상
    private Material material; //다른 기계 재질에 영향을 주지 않을 화면 재질
    private Material originalMaterial; //종료 시 복구할 원래 화면 재질
    private float nextRenderTime; //다음 영상 갱신 시각
    private int displayedIndex = -1; //현재 모니터에 연결된 카메라
    private bool[] indicatorStates; //반짝임 상태가 바뀔 때만 표시등 재질 갱신
    private MaterialPropertyBlock indicatorProperties; //표시등별 재질 복제 없는 색상 변경
    private readonly List<Renderer> hiddenRenderers = new(); //촬영 중에만 숨길 로컬 팔·모니터·카메라 메시
    private readonly List<Renderer> cameraRenderers = new(); //촬영 중인 카메라 자체가 렌즈를 가리지 않도록 제외
    private readonly List<bool> rendererStates = new(); //렌더 후 되돌릴 본인 전용 메시 표시 상태

    internal int cameraCount => cameras != null ? cameras.Length : 0; //관제 진입점의 유효 화면 수
    internal RenderTexture videoTexture => texture; //모니터 영상 확인용 읽기 전용 참조

    internal void initialize(Cctv cctv) //게임 화면과 독립적인 모니터 렌더링 준비
    {
        owner = cctv;
        if (screen == null || cameraCount == 0 || texture != null) return;
        texture = new RenderTexture(textureWidth, textureHeight, 24) { name = "CCTV monitor video" };
        texture.Create();
        indicatorStates = new bool[cameraCount];
        indicatorProperties = new MaterialPropertyBlock();
        originalMaterial = screen.sharedMaterial;
        material = new Material(originalMaterial);
        if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", texture);
        if (material.HasProperty("_MainTex")) material.SetTexture("_MainTex", texture);
        screen.sharedMaterial = material;
        foreach (Camera camera in cameras)
        {
            if (camera == null) continue;
            camera.enabled = false;
            camera.cullingMask &= ~(1 << 2 | 1 << 31); //관전자와 미리보기 촬영 제외
            camera.targetTexture = texture;
        }
    }

    private void LateUpdate() //영상은 로컬에서 만들고 선택 번호와 이용 표시만 공유 상태를 사용
    {
        if (owner == null || owner.Object == null || !owner.Object.IsValid || texture == null) return;
        int index = Mathf.Clamp(owner.selectedCamera, 0, cameraCount - 1); //현재 공유 화면
        for (int i = 0; i < cameraCount; i++)
        {
            bool lit = i == index && Mathf.Repeat(Time.unscaledTime, 1f) < 0.5f; //모니터에 표시하는 카메라만 점멸
            if (indicators != null && i < indicators.Length && indicators[i] != null) indicators[i].enabled = lit;
            if (indicatorStates[i] == lit) continue;
            indicatorStates[i] = lit;
            if (indicatorBulbs == null || i >= indicatorBulbs.Length || indicatorBulbs[i] == null) continue;
            Color color = lit ? Color.red : new Color(0.03f, 0.01f, 0.01f); //사용하지 않는 카메라의 표시등은 소등
            indicatorBulbs[i].GetPropertyBlock(indicatorProperties);
            indicatorProperties.SetColor("_BaseColor", color);
            indicatorProperties.SetColor("_Color", color);
            indicatorBulbs[i].SetPropertyBlock(indicatorProperties);
        }
        bool changed = displayedIndex != index; //전환은 다음 갱신 시간까지 기다리지 않음
        if (changed && caption != null)
        {
            string area = areaNames != null && index < areaNames.Length ? areaNames[index] : string.Empty; //기획서 구역 이름
            caption.text = $"CAM {index + 1:00}  {area}";
        }
        if (!changed && Time.unscaledTime < nextRenderTime) return;
        displayedIndex = index;
        nextRenderTime = Time.unscaledTime + 1f / Mathf.Max(1f, framesPerSecond);
        Camera selected = cameras[index]; //이번 프레임에만 렌더할 고정 카메라
        if (selected == null) return;
        renderVideo(selected);
    }

    private void renderVideo(Camera camera) //감시 영상에는 본인도 정상 표시하고 1인칭 복제 팔은 제외
    {
        Transform listener = PlayerCameraController.LocalListenerTransform; //본인 전용 표시의 기준 시점
        PlayerLookVisualComponent visuals = listener != null ? listener.GetComponentInParent<PlayerLookVisualComponent>() : null;
        hiddenRenderers.Clear();
        rendererStates.Clear();
        if (listener != null) listener.GetComponentsInChildren(true, hiddenRenderers);
        cameraRenderers.Clear();
        camera.GetComponentsInChildren(true, cameraRenderers);
        hiddenRenderers.AddRange(cameraRenderers);
        hiddenRenderers.Add(screen);
        if (caption != null) hiddenRenderers.Add(caption.GetComponent<Renderer>());
        foreach (Renderer renderer in hiddenRenderers)
        {
            rendererStates.Add(renderer != null && renderer.enabled);
            if (renderer != null) renderer.enabled = false;
        }
        try
        {
            visuals?.beginExternalView();
            camera.Render();
        }
        finally
        {
            visuals?.endExternalView();
            for (int i = 0; i < hiddenRenderers.Count; i++)
                if (hiddenRenderers[i] != null) hiddenRenderers[i].enabled = rendererStates[i];
        }
    }

    internal void release() //씬 전환과 종료 시 렌더 텍스처와 임시 재질 해제
    {
        if (screen != null && material != null) screen.sharedMaterial = originalMaterial;
        if (cameras != null)
            foreach (Camera camera in cameras) if (camera != null) camera.targetTexture = null;
        if (indicators != null)
            foreach (Light indicator in indicators) if (indicator != null) indicator.enabled = false;
        if (material != null) Destroy(material);
        if (texture != null) { texture.Release(); Destroy(texture); }
        material = null;
        texture = null;
        owner = null;
    }

    private void OnDestroy() //일반 파괴 경로에서도 로컬 렌더 리소스 정리
    {
        release();
    }
}
