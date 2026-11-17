using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

namespace TrustNoOne.Gimmicks
{
    /// <summary>
    /// [역할] 살인마 암전 — 내 카메라에 "가까운 곳만 보이기" 그리기 단계와 팔 전용 카메라를 붙인다.
    ///  - 가까운 시야 (16d): 암전 동안 내 카메라가 그리기 시작할 때마다 BlackoutNearVisionPass 를 그 카메라에만 넣는다 →
    ///    셰이더가 점마다 카메라에서의 거리만큼 검게 칠한다 (보이기 시작 1.5m 까지 그대로 → 완전 검정 3m 부터 검정 · 하늘 검정).
    ///    진하기에 어두움(0 → 1, 1초)을 곱한다. 3D 는 항상 그린다.
    ///  - 팔 전용 카메라: 내 카메라의 자식 URP Overlay 카메라, 팔 레이어(29)만 그린다. 암전 동안만 내 카메라의 "카메라 쌓기" 목록에 올린다.
    ///    가까운 시야 단계 · 후처리 다음에 그려지므로 어두워지는 1초 동안에도 팔은 밝게 보인다.
    ///  - 대체 검은 판 (16b): 시야 재질 칸이 비어 있으면 오류 로그 한 번 + 화면 전체 검은 UI 판(진하기 = 어두움)으로 물러난다.
    /// [이유] 기획 피드백 (2026-11-17) "너무 다 가려서 진짜 아예 안보임, 바로 앞까지만 보이도록" — 16b 의 "어두움 1 이면 3D 를 그리지 않음"(KB32)을 바꿨다.
    ///  기획서 3장 "점점 화면이 어두워진다". 공유 설정(URP 렌더러 · GraphicsSettings)은 고치지 않는다 (접근법 2).
    ///  팀원 카메라(Player.prefab)는 파일을 고치지 않고, 실행 중 카메라 쌓기 목록만 바꿨다가 되돌린다 (16b — 3D 끄기용 값 바꾸기는 없어졌다).
    /// [근거] 16b 명세 KB28 · KB33 · KB35, 16d 명세 KB55 · KB58 · KB60 · KB62 · KB66
    /// </summary>
    public class BlackoutScreenRig : MonoBehaviour
    {
        /// <summary>보이기 시작 거리 기본값(m) — 이 거리까지는 그대로 보인다 (16d 질문 1 예시 — Play 중 맞춰 바꿀 값, KB66)</summary>
        public const float DefaultNearVisibleStart = 1.5f;

        /// <summary>완전 검정 거리 기본값(m) — 이 거리부터 완전히 검다 (16d 질문 1 예시 — KB66)</summary>
        public const float DefaultNearVisibleEnd = 3f;

        // 셰이더 값 이름 — Shaders/KillerBlackoutNearVision.shader 의 Properties 와 같아야 한다
        private static readonly int DarknessId = Shader.PropertyToID("_Darkness");
        private static readonly int NearStartId = Shader.PropertyToID("_NearStart");
        private static readonly int NearEndId = Shader.PropertyToID("_NearEnd");

        [Header("가까운 시야 (16d)")]
        [Tooltip("이 거리(m)까지는 그대로 보인다. Play 중에 바꿔 보고 정한 숫자를 알려 주세요 (KB66)")]
        [SerializeField] private float nearVisibleStart = DefaultNearVisibleStart;
        [Tooltip("이 거리(m)부터 완전히 검다. 사이는 부드럽게 어두워진다 (KB59)")]
        [SerializeField] private float nearVisibleEnd = DefaultNearVisibleEnd;
        [Tooltip("셰이더 TrustNoOne/KillerBlackoutNearVision 을 쓰는 재질 — 메뉴(설치 공통 단계)가 연결한다 (KB63). 비면 화면 전체 검은 판으로 물러남")]
        [SerializeField] private Material nearVisionMaterial;

        [Header("대체 검은 판 (시야 재질이 없을 때 — 16b)")]
        [Tooltip("검은 판을 카메라 앞 몇 m 에 둘지. 플레이어 카메라 near 0.03 보다 조금 앞이어야 모든 3D 를 덮는다 (16b KB32)")]
        [SerializeField] private float planeDistance = 0.05f;

        private Camera target;                          // 지금 붙어 있는 내 카메라 (팀원 Player.prefab 의 카메라)
        private UniversalAdditionalCameraData targetData;
        private Camera armsCamera;                      // 팔 전용 Overlay 카메라 (내 카메라의 자식)
        private UniversalAdditionalCameraData armsData;
        private Canvas planeCanvas;
        private Image planeImage;

        private BlackoutNearVisionPass nearVisionPass;
        private Material runtimeMaterial;               // 재질 에셋의 실행 중 복사본 (에셋 파일은 바뀌지 않게)
        private bool nearVisionActive;                  // 이번 프레임 내 카메라에 가까운 시야 단계를 넣는가
        private bool missingMaterialLogged;

        /// <summary>디버그 패널 (16d KB64) — 예) "시야 = 1.5 ~ 3m" / "시야 = 재질 없음 → 검은 판"</summary>
        public string VisionStatus => nearVisionMaterial != null
            ? $"시야 = {nearVisibleStart:0.##} ~ {nearVisibleEnd:0.##}m"
            : "시야 = 재질 없음 → 검은 판";

        private void OnEnable()
        {
            RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
        }

        private void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
            Restore();
        }

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

            ApplyArmsCamera(state.ArmsCamera);

            if (PrepareNearVision())
            {
                SetNearVision(state.PlaneAlpha);
                HidePlane();
            }
            else
            {
                // KB62: 재질이 없으면 16b 식 화면 전체 검은 판 (3D 는 그대로 그림)
                nearVisionActive = false;
                ApplyPlane(state.PlaneAlpha);
            }
        }

        /// <summary>KB35: 가까운 시야 단계 · 카메라 쌓기 목록 · 판을 모두 원래대로</summary>
        public void Restore()
        {
            nearVisionActive = false;

            if (targetData != null && armsCamera != null)
                targetData.cameraStack.Remove(armsCamera);

            if (armsCamera != null)
                armsCamera.enabled = false;

            HidePlane();

            target = null;
            targetData = null;
        }

        private void OnDestroy()
        {
            Restore();

            // 판은 씬 맨 위에 따로 만들었다 → 같이 지운다. 팔 카메라는 내 카메라의 자식이라 따로 지운다
            if (planeCanvas != null)
                Destroy(planeCanvas.gameObject);

            if (armsCamera != null)
                Destroy(armsCamera.gameObject);

            if (runtimeMaterial != null)
                Destroy(runtimeMaterial);
        }

        /// <summary>
        /// KB58: 카메라가 그리기 시작할 때마다 불린다 (모든 카메라). 암전이 내 화면에 적용되는 동안 내 카메라에만 가까운 시야 단계를 넣는다.
        /// 넣은 단계는 그 카메라를 다 그린 뒤 URP 가 스스로 뺀다 — 다음 프레임에 다시 넣는다.
        /// </summary>
        private void OnBeginCameraRendering(ScriptableRenderContext context, Camera cam)
        {
            if (!nearVisionActive || cam != target || targetData == null)
                return;

            ScriptableRenderer renderer = targetData.scriptableRenderer;

            if (renderer != null)
                renderer.EnqueuePass(nearVisionPass);
        }

        /// <summary>시야 재질이 있으면 실행 중 복사본 · 그리기 단계를 준비하고 true. 없으면 오류 로그 한 번 + false (KB62)</summary>
        private bool PrepareNearVision()
        {
            if (nearVisionMaterial == null)
            {
                if (!missingMaterialLogged)
                {
                    missingMaterialLogged = true;
                    Debug.LogError("[BlackoutScreenRig] 시야 재질(Near Vision Material) 칸이 비어 있어 화면 전체 검은 판으로 물러납니다 — " +
                                   "메뉴 SuHan/Setup Solo Mission Test 또는 SuHan/Install Killer Blackout To 4~12 를 다시 실행하세요 (16d KB63).");
                }

                return false;
            }

            if (runtimeMaterial == null)
                runtimeMaterial = new Material(nearVisionMaterial) { name = nearVisionMaterial.name + " (실행 중)" };

            if (nearVisionPass == null)
                nearVisionPass = new BlackoutNearVisionPass();

            return true;
        }

        /// <summary>셰이더에 이번 프레임 값을 넣고 단계를 켠다. 거리는 0 이상으로만 (시작 ≥ 끝이면 셰이더 · 규칙 모두 끝에서 딱 끊김 — KB61)</summary>
        private void SetNearVision(float darkness)
        {
            runtimeMaterial.SetFloat(DarknessId, darkness);
            runtimeMaterial.SetFloat(NearStartId, Mathf.Max(0f, nearVisibleStart));
            runtimeMaterial.SetFloat(NearEndId, Mathf.Max(0f, nearVisibleEnd));
            nearVisionPass.Setup(runtimeMaterial);
            nearVisionActive = true;
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

        private void HidePlane()
        {
            if (planeCanvas != null)
                planeCanvas.enabled = false;
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

        /// <summary>대체 검은 판: 화면 전체 검은 Image 를 가진 Screen Space - Camera 캔버스. 씬 맨 위에 둔다 (네트워크 오브젝트 아래에 끼우지 않게)</summary>
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
