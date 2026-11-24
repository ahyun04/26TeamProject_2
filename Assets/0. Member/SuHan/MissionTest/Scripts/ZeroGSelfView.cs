using TrustNoOne.Gimmicks;
using UnityEngine;

/// <summary>
/// [테스트 전용] 무중력 "내 몸 보기" 작은 화면 — F3 으로 켜고 끈다 (화면 오른쪽 아래).
/// [역할] 내 플레이어 앞쪽 비스듬히에 카메라 하나를 두고 작은 그림(RenderTexture)으로 그려 보여 준다. 그릴 때만 팀원 몸 방향 코드의
///  beginExternalView · endExternalView 로 내 몸을 보이게 한다 (팀원 CCTV · 로비 거울과 같은 방식 — 팀원 파일은 바꾸지 않음).
///  1인칭 팔 · 든 물건(플레이어 카메라 아래)은 그동안 잠깐 끈다 (로비 거울과 같음).
/// [이유] 혼자 모드에서는 내 몸이 1인칭이라 그림자만 남아, 날기 동작 · 앞으로 숙이기를 볼 수 없다 (QA — "애니메이션이 안보여서 확인이 안돼").
/// [근거] 17a 명세 ZG40 (테스트 후 추가)
/// </summary>
public class ZeroGSelfView : MonoBehaviour
{
    [SerializeField] private KeyCode toggleKey = KeyCode.F3;
    [Tooltip("내 몸 기준 카메라 자리 (오른쪽 · 위 · 앞, m)")]
    [SerializeField] private Vector3 cameraOffset = new Vector3(1.8f, 0.6f, 3f);
    [SerializeField] private Vector2 viewSize = new Vector2(400f, 225f);

    private bool showing;
    private Camera viewCamera;
    private RenderTexture texture;
    private GUIStyle style;
    private readonly System.Collections.Generic.List<Renderer> firstPersonRenderers = new System.Collections.Generic.List<Renderer>();
    private readonly System.Collections.Generic.List<bool> rendererStates = new System.Collections.Generic.List<bool>();

    private void Update()
    {
        if (Input.GetKeyDown(toggleKey))
            showing = !showing;
    }

    // 팀원 몸 방향 코드(−50) · 몸 숙이기(−45)가 이번 프레임 몸을 놓은 뒤에 그린다 (기본 순서 0)
    private void LateUpdate()
    {
        ZeroGMovement local = ZeroGMovement.Local;

        if (!showing || local == null)
            return;

        EnsureCamera();

        Transform body = local.transform;
        Quaternion yaw = Quaternion.Euler(0f, body.eulerAngles.y, 0f);
        Vector3 focus = body.position + Vector3.up * 1f;
        viewCamera.transform.position = body.position + yaw * cameraOffset;
        viewCamera.transform.rotation = Quaternion.LookRotation(focus - viewCamera.transform.position, Vector3.up);

        PlayerLookVisualComponent visuals = local.GetComponent<PlayerLookVisualComponent>();
        Camera playerCamera = local.GetComponentInChildren<Camera>();

        firstPersonRenderers.Clear();
        rendererStates.Clear();

        if (playerCamera != null)
            playerCamera.GetComponentsInChildren(true, firstPersonRenderers);

        foreach (Renderer r in firstPersonRenderers)
        {
            rendererStates.Add(r.enabled);
            r.enabled = false;
        }

        try
        {
            visuals?.beginExternalView();
            viewCamera.Render();
        }
        finally
        {
            visuals?.endExternalView();

            for (int i = 0; i < firstPersonRenderers.Count; i++)
            {
                if (firstPersonRenderers[i] != null)
                    firstPersonRenderers[i].enabled = rendererStates[i];
            }
        }
    }

    private void OnGUI()
    {
        if (ZeroGMovement.Local == null)
            return;

        if (style == null)
        {
            style = new GUIStyle(GUI.skin.label) { fontSize = 13 };
            style.normal.textColor = Color.white;
        }

        float x = Screen.width - viewSize.x - 10f;
        float y = Screen.height - viewSize.y - 30f;

        if (showing && texture != null)
        {
            GUI.Box(new Rect(x - 2f, y - 2f, viewSize.x + 4f, viewSize.y + 4f), GUIContent.none);
            GUI.DrawTexture(new Rect(x, y, viewSize.x, viewSize.y), texture, ScaleMode.ScaleToFit, false);
        }

        GUI.Label(new Rect(x, Screen.height - 26f, viewSize.x, 22f), $"{toggleKey}: 내 몸 보기 {(showing ? "끄기" : "켜기")}", style);
    }

    private void EnsureCamera()
    {
        if (viewCamera != null)
            return;

        texture = new RenderTexture(640, 360, 24) { name = "ZeroGSelfView" };
        GameObject go = new GameObject("ZeroGSelfView Camera") { hideFlags = HideFlags.DontSave };
        viewCamera = go.AddComponent<Camera>();
        viewCamera.enabled = false;   // 직접 Render() 로만 그린다 (로비 거울과 같음)
        viewCamera.targetTexture = texture;
        viewCamera.fieldOfView = 50f;
        viewCamera.nearClipPlane = 0.05f;
    }

    private void OnDestroy()
    {
        if (viewCamera != null)
            Destroy(viewCamera.gameObject);

        if (texture != null)
            texture.Release();
    }
}
