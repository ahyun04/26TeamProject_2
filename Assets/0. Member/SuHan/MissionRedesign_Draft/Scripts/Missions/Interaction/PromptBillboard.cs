using UnityEngine;

namespace TrustNoOne.Missions
{
    /// <summary>
    /// [역할] 조준 문구(MissionPrompt 의 promptObject, 예: 밸브의 "[F]키를 눌러 상호작용")가 항상 보는 사람 쪽을 향하게 한다.
    ///  월드 캔버스의 회전을 카메라 회전과 같게 맞춘다 → 어느 쪽에서 봐도 글자가 뒤집히지 않는다.
    ///
    /// [필요한 이유] 옛 밸브 프리팹의 문구 캔버스는 방향이 고정이라 한쪽에서만 바르게 읽힌다.
    ///  배치 방향에 따라 뒤집혀 보였다 (2b 테스트에서 발견). 변환기가 문구 캔버스에 자동으로 붙인다.
    /// [Fusion 의 FusionBasicBillboard 를 쓰지 않는 이유] Camera.main 을 쓰는데, 플레이어 카메라에는 MainCamera 태그가 없어 찾지 못한다.
    ///  대신 켜져 있는 카메라 중 가장 위에 그려지는(depth 가 가장 큰) 카메라를 쓴다 — 로컬 플레이어 카메라만 켜져 있다.
    ///  카메라 검색은 비싸서 모든 인스턴스가 공유하고 0.5초마다만 다시 찾는다.
    /// [팀원 코드] MissionPrompt 는 수정하지 않는다. 문구가 켜져 있을 때만 LateUpdate 가 돈다 (꺼진 오브젝트는 Update 가 불리지 않음).
    /// </summary>
    public class PromptBillboard : MonoBehaviour
    {
        private static Camera viewCamera;
        private static float nextCameraSearchTime;

        private void LateUpdate()
        {
            Camera cam = GetViewCamera();

            if (cam != null)
                transform.rotation = cam.transform.rotation;
        }

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
}
