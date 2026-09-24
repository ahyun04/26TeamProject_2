using Fusion;
using UnityEngine;

namespace TrustNoOne.Missions
{
    /// <summary>
    /// [역할] 필터 먼지 하나: 조준 "주소" + 모습. 네트워크 상태는 없다 (상태는 FilterStation 이 가짐 — 2c 명세 F2).
    ///  - 주소: 자기 오브젝트의 중첩 NetworkObject 를 TargetObject 로 알려 준다.
    ///    팀원의 아이템 코드(PlayerItemController)가 조준 대상의 NetworkObject id 만 호스트로 보내므로, 먼지마다 id 가 필요하다.
    ///  - 모습: FilterStation 이 매 Render 에 ApplyVisual 로 상태를 넣어 준다.
    ///
    /// [치운 먼지 — F5] 렌더러만 끈다. 옛 코드는 오브젝트를 SetActive(false) 했는데, 그 오브젝트에 중첩 NetworkObject 가 있어 위험하다.
    /// [흡입 연출 — F6] 본인 화면: 1인칭 흡입구로 날아가며 작아짐 / 다른 사람 화면: 제자리에서 작아짐 (1인칭 모델이 없으므로).
    /// [콜라이더 — F8] FilterStation 이 "내 미션 && 안 치움"으로 SetColliderEnabled 를 부른다. 여기서 따로 켜고 끄지 않는다.
    /// [외곽선 — F7] 청소기(VacuumTool)를 들고 조준했을 때만 SetHighlight(true). 치웠거나 흡입 중이면 여기서 끈다.
    /// </summary>
    public class FilterDust : MonoBehaviour, ITargetable, IItemUseTarget
    {
        [SerializeField] private FilterStation station;
        [SerializeField] private int index;
        [SerializeField] private Collider dustCollider;
        [SerializeField] private GameObject outlineObject;

        private NetworkObject networkObject;
        private Renderer[] renderers;
        private Vector3 startLocalPosition;
        private Vector3 startLocalScale;
        private bool? renderersVisible;

        public NetworkObject TargetObject => networkObject;
        public FilterStation Station => station;
        public int Index => index;

        private void Awake()
        {
            if (station == null)
                station = GetComponentInParent<FilterStation>();

            networkObject = GetComponent<NetworkObject>();
            renderers = CollectRenderers();
            startLocalPosition = transform.localPosition;
            startLocalScale = transform.localScale;
        }

        /// <param name="t">흡입 진행도 (0~1, 부드럽게 보정된 값)</param>
        /// <param name="mouth">본인이 든 청소기의 1인칭 흡입구. 다른 사람 화면이면 null</param>
        public void ApplyVisual(bool cleaned, bool sucking, float t, Transform mouth)
        {
            if (cleaned || sucking)
                SetHighlight(false);

            if (cleaned)
            {
                SetRenderersVisible(false);
                ResetPose();
                return;
            }

            SetRenderersVisible(true);

            if (!sucking)
            {
                ResetPose();
                return;
            }

            Vector3 start = transform.parent != null ? transform.parent.TransformPoint(startLocalPosition) : startLocalPosition;
            transform.position = mouth != null ? Vector3.Lerp(start, mouth.position, t) : start;
            transform.localScale = Vector3.Lerp(startLocalScale, Vector3.zero, t);
        }

        public void SetColliderEnabled(bool enabled)
        {
            if (dustCollider != null && dustCollider.enabled != enabled)
                dustCollider.enabled = enabled;
        }

        public void SetHighlight(bool on)
        {
            if (outlineObject != null && outlineObject.activeSelf != on)
                outlineObject.SetActive(on);
        }

        private void ResetPose()
        {
            transform.localPosition = startLocalPosition;
            transform.localScale = startLocalScale;
        }

        private void SetRenderersVisible(bool visible)
        {
            if (renderersVisible == visible)
                return;

            renderersVisible = visible;

            foreach (Renderer r in renderers)
            {
                if (r != null)
                    r.enabled = visible;
            }
        }

        /// <summary>자기 아래 렌더러 (외곽선 오브젝트의 렌더러는 제외 — 외곽선은 SetHighlight 가 따로 켜고 끈다).</summary>
        private Renderer[] CollectRenderers()
        {
            Renderer[] all = GetComponentsInChildren<Renderer>(true);
            System.Collections.Generic.List<Renderer> result = new System.Collections.Generic.List<Renderer>();

            foreach (Renderer r in all)
            {
                if (outlineObject != null && r.transform.IsChildOf(outlineObject.transform))
                    continue;

                result.Add(r);
            }

            return result.ToArray();
        }
    }
}
