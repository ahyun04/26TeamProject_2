using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

namespace TrustNoOne.Gimmicks
{
    /// <summary>
    /// [역할] 살인마 암전 — 내 카메라에 검은 판과 팔 전용 카메라를 붙이고, 완전히 어두워지면 카메라가 3D 를 그리지 않게 한다.
    ///  - 검은 판: 내 카메라 전용 Screen Space - Camera 캔버스를 카메라 앞 0.05m 에 둔다 → 모든 3D 앞, HUD(Overlay 캔버스) 뒤. 진하기 = 어두움.
    ///  - 팔 전용 카메라: 내 카메라의 자식 URP Overlay 카메라, 팔 레이어(29)만 그린다. 암전 동안만 내 카메라의 "카메라 쌓기" 목록에 올린다.
    ///    판 뒤가 아니라 판 위에 그려지므로 어두워지는 1초 동안에도 팔은 밝게 보인다.
    ///  - 어두움 1: 내 카메라가 그리는 레이어를 0 으로, 배경을 단색 검정으로 바꾼다. 끝나면 원래 값으로 되돌린다.
    /// [이유] 기획서 "암전 중 살인마는 자신의 손과 발만 보이고 지형 · 벽 · 다른 플레이어 · 광원 모두 안 보임".
    ///  검은 판은 깊이를 쓰지 않는 UI 라 불꽃 · 빛 번짐 같은 반투명 효과가 비칠 수 있다 → 완전히 어두운 동안은 아예 그리지 않는다.
    ///  팀원 카메라(Player.prefab)는 파일을 고치지 않고 실행 중에만 값을 바꿨다가 되돌린다.
    /// [근거] 16b 명세 KB28 · KB32 · KB33 · KB35, 조사 1-2 (near 0.03 · URP Base 카메라)
    /// </summary>
    public class BlackoutScreenRig : MonoBehaviour
    {
        [Tooltip("검은 판을 카메라 앞 몇 m 에 둘지. 플레이어 카메라 near 0.03 보다 조금 앞이어야 모든 3D 를 덮는다 (KB32)")]
        [SerializeField] private float planeDistance = 0.05f;

        private Camera target;                          // 지금 붙어 있는 내 카메라 (팀원 Player.prefab 의 카메라)
        private UniversalAdditionalCameraData targetData;
        private Camera armsCamera;                      // 팔 전용 Overlay 카메라 (내 카메라의 자식)
        private UniversalAdditionalCameraData armsData;
        private Canvas planeCanvas;
        private Image planeImage;

        private bool worldHidden;
        private int savedCullingMask;
        private CameraClearFlags savedClearFlags;
        private Color savedBackground;

        /// <summary>지금 내 카메라가 3D 를 그리지 않는 중인가 (디버그용)</summary>
        public bool WorldHidden => worldHidden;

        /// <summary>규칙의 결과대로 화면을 바꾼다. 꺼짐이거나 카메라가 없으면 원래대로 되돌린다.</summary>
        public void Apply(Camera cam, BlackoutVisualState state)
        {
            if (!state.Active || cam == null)
            {
                Restore();
                return;
            }

            // 카메라가 바뀌면 (관전 전환 · 다시 스폰) 옛 카메라를 먼저 원래대로
            if (cam != target)
            {
                Restore();
                target = cam;
                targetData = cam.GetUniversalAdditionalCameraData();
            }

            ApplyPlane(state.PlaneAlpha);
            ApplyArmsCamera(state.ArmsCamera);
            SetWorldHidden(state.HideWorld);
        }

        /// <summary>KB35: 카메라 값 · 쌓기 목록 · 판을 모두 원래대로</summary>
        public void Restore()
        {
            SetWorldHidden(false);

            if (targetData != null && armsCamera != null)
                targetData.cameraStack.Remove(armsCamera);

            if (armsCamera != null)
                armsCamera.enabled = false;

            if (planeCanvas != null)
                planeCanvas.enabled = false;

            target = null;
            targetData = null;
        }

        private void OnDisable()
        {
            Restore();
        }

        private void OnDestroy()
        {
            Restore();

            // 판은 씬 맨 위에 따로 만들었다 → 같이 지운다. 팔 카메라는 내 카메라의 자식이라 따로 지운다
            if (planeCanvas != null)
                Destroy(planeCanvas.gameObject);

            if (armsCamera != null)
                Destroy(armsCamera.gameObject);
        }

        private void ApplyPlane(float alpha)
        {
            if (planeCanvas == null)
                CreatePlane();

            planeCanvas.worldCamera = target;
            planeCanvas.planeDistance = planeDistance;
            planeCanvas.enabled = true;
            planeImage.color = new Color(0f, 0f, 0f, alpha);
        }

        private void ApplyArmsCamera(bool on)
        {
            // 팔 카메라는 내 카메라의 자식이어야 같은 자리 · 방향으로 그린다 — 카메라가 바뀌었으면 새로 만든다
            if (armsCamera != null && armsCamera.transform.parent != target.transform)
            {
                Destroy(armsCamera.gameObject);
                armsCamera = null;
            }

            if (armsCamera == null)
                CreateArmsCamera();

            // 시야각 · near · far · 후처리는 매 프레임 내 카메라와 맞춘다 (팀원이 값을 바꿔도 따라감)
            armsCamera.fieldOfView = target.fieldOfView;
            armsCamera.nearClipPlane = target.nearClipPlane;
            armsCamera.farClipPlane = target.farClipPlane;
            armsData.renderPostProcessing = targetData.renderPostProcessing;

            if (on)
            {
                if (!targetData.cameraStack.Contains(armsCamera))
                    targetData.cameraStack.Add(armsCamera);

                armsCamera.enabled = true;
            }
            else
            {
                targetData.cameraStack.Remove(armsCamera);
                armsCamera.enabled = false;
            }
        }

        /// <summary>KB32: 어두움 1 동안 내 카메라가 아무것도 그리지 않고 배경은 검정. 처음 바꿀 때 원래 값을 기억해 둔다</summary>
        private void SetWorldHidden(bool hide)
        {
            if (hide == worldHidden)
                return;

            if (target == null)
            {
                // 카메라가 이미 사라졌으면 되돌릴 곳이 없다 — 상태만 맞춘다
                worldHidden = false;
                return;
            }

            if (hide)
            {
                savedCullingMask = target.cullingMask;
                savedClearFlags = target.clearFlags;
                savedBackground = target.backgroundColor;
                target.cullingMask = 0;
                target.clearFlags = CameraClearFlags.SolidColor;
                target.backgroundColor = Color.black;
            }
            else
            {
                target.cullingMask = savedCullingMask;
                target.clearFlags = savedClearFlags;
                target.backgroundColor = savedBackground;
            }

            worldHidden = hide;
        }

        /// <summary>팔 전용 카메라: URP Overlay, 팔 레이어만, 깊이만 지우고 그린다 (KB33)</summary>
        private void CreateArmsCamera()
        {
            GameObject go = new GameObject("KillerBlackoutArmsCamera");
            go.transform.SetParent(target.transform, false);

            armsCamera = go.AddComponent<Camera>();
            armsCamera.enabled = false;
            armsCamera.cullingMask = 1 << BlackoutArms.ArmsLayer;
            armsCamera.clearFlags = CameraClearFlags.Depth;

            armsData = armsCamera.GetUniversalAdditionalCameraData();
            armsData.renderType = CameraRenderType.Overlay;
            armsData.renderShadows = false;
            // 깊이 지우기(Clear Depth)는 URP 14 에서 읽기 전용 · 기본 켬 → 3D 깊이를 지우고 그려 벽에 팔이 묻히지 않는다
        }

        /// <summary>검은 판: 화면 전체 검은 Image 를 가진 Screen Space - Camera 캔버스. 씬 맨 위에 둔다 (네트워크 오브젝트 아래에 끼우지 않게)</summary>
        private void CreatePlane()
        {
            GameObject canvasObject = new GameObject("KillerBlackoutPlane");
            planeCanvas = canvasObject.AddComponent<Canvas>();
            planeCanvas.renderMode = RenderMode.ScreenSpaceCamera;

            GameObject imageObject = new GameObject("Black");
            imageObject.transform.SetParent(canvasObject.transform, false);
            planeImage = imageObject.AddComponent<Image>();
            planeImage.color = new Color(0f, 0f, 0f, 0f);
            planeImage.raycastTarget = false;   // 클릭을 막지 않는다 (상호작용 정상 — 기획서)

            RectTransform rect = planeImage.rectTransform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }
    }
}
