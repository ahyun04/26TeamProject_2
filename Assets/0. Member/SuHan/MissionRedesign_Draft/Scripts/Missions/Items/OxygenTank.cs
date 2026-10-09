using Fusion;
using UnityEngine;

namespace TrustNoOne.Missions
{
    /// <summary>
    /// [역할] 산소통 아이템 (단체 미션 TG004 생명 유지 장치). 신규. 3b 명세 3-2.
    ///  - 좌클릭 줍기 · G 내려놓기 · 우클릭 사용은 팀원 아이템 구조(ItemBase · PlayerItemController) 그대로 (2c 청소기와 같다).
    ///  - 우클릭(호스트): 조준 대상이 생명 유지 장치 본체면 본체에 넣기를 요청한다. 받을지는 본체가 정한다 (범인 · 판정 중 · 범위 등).
    ///  - 압력계 램프 (LS1): 내 화면에서 revealDistance(2.5m) 안이면 정상 초록 / 위험 빨강, 멀면 원래 재질(회색).
    ///    기획서 "산소통을 하나씩 확인하며 안전 여부를 판단한다" — 다가가야 알 수 있어 확인하는 행동이 생긴다.
    /// [동기화 — LS14] 위험 여부(Dangerous)만. 호스트가 만들기 직전(Runner.Spawn 의 onBeforeSpawned)에 정한다.
    ///  램프는 각 PC 가 자기 화면 위치로 정하므로 동기화하지 않는다.
    /// [범인 — LS8] 줍기는 팀원 코드에 역할 제한이 없어 범인도 들고 옮길 수 있다. 넣기는 본체의 공통 자격 검사가 거부한다.
    /// [팀원 코드] ItemBase 는 상속만 한다 (Spawned · Render 는 base 를 먼저 부른다).
    /// </summary>
    public class OxygenTank : ItemBase, IItemUseHandler
    {
        /// <summary>MaterialSwapper 변형 번호: 정상 (초록).</summary>
        public const int SafeGauge = 0;

        /// <summary>MaterialSwapper 변형 번호: 위험 (빨강).</summary>
        public const int DangerGauge = 1;

        [Header("압력계 (LS1)")]
        [Tooltip("압력계 램프. 원래 재질 = 가까이 가기 전(회색)")]
        [SerializeField] private Renderer gaugeRenderer;

        [Tooltip("정상 (발광 초록)")]
        [SerializeField] private Material safeMaterial;

        [Tooltip("위험 (발광 빨강)")]
        [SerializeField] private Material dangerMaterial;

        [Tooltip("내 화면에서 이 거리(m) 안이면 압력계 색이 보인다")]
        [Min(0.1f)]
        [SerializeField] private float revealDistance = 2.5f;

        /// <summary>위험한 산소통인가 (넣으면 5초 뒤 폭발). 호스트가 만들 때 정한다.</summary>
        [Networked] public NetworkBool Dangerous { get; private set; }

        private MaterialSwapper gauge;

        // 내 화면 위치를 주는 조준 감지기 (입력 권한이 있는 것 하나). 모든 산소통이 같이 쓴다.
        // 내 플레이어 카메라는 태그가 없어 Camera.main 으로 찾을 수 없다 (명세 1장).
        private static PlayerTargetDetector localViewer;
        private static float nextViewerSearch;

        /// <summary>호스트: 만들기 직전(Runner.Spawn 의 onBeforeSpawned)에 위험 여부를 정한다.</summary>
        public void HostSetDangerous(bool dangerous)
        {
            Dangerous = dangerous;
        }

        public override void Spawned()
        {
            base.Spawned();
            gauge = new MaterialSwapper(gaugeRenderer, safeMaterial, dangerMaterial);
            UpdateGauge();
        }

        public override void Render()
        {
            base.Render();
            UpdateGauge();
        }

        /// <summary>호스트: 팀원 PlayerItemController 가 행동 가능 · 거리를 검사한 뒤 부른다 (우클릭).</summary>
        public void UseAsStateAuthority(NetworkObject target)
        {
            if (!HasStateAuthority || target == null || HolderObject == null)
                return;

            LifeSupportStation station = target.GetComponent<LifeSupportStation>();

            if (station == null)
                return;

            station.RequestInsert(HolderObject.InputAuthority, this);
        }

        /// <summary>가까우면 정상 · 위험 색, 멀면 원래 재질. 같은 값이면 MaterialSwapper 가 아무것도 하지 않는다.</summary>
        private void UpdateGauge()
        {
            if (gauge == null || Object == null || !Object.IsValid)
                return;

            Vector3 gaugePosition = gaugeRenderer != null ? gaugeRenderer.transform.position : transform.position;
            bool revealed = TryGetViewerPosition(out Vector3 viewer)
                && (viewer - gaugePosition).sqrMagnitude <= revealDistance * revealDistance;

            gauge.Show(revealed ? (Dangerous ? DangerGauge : SafeGauge) : MaterialSwapper.Original);
        }

        /// <summary>내 화면(조준 광선의 시작점) 위치. 내 플레이어가 아직 없으면 false.</summary>
        private static bool TryGetViewerPosition(out Vector3 position)
        {
            position = default;

            if (localViewer == null || !localViewer.isActiveAndEnabled)
            {
                localViewer = null;

                // 못 찾았으면 1초에 한 번만 다시 찾는다 (산소통마다 매 프레임 찾지 않게)
                if (Time.unscaledTime < nextViewerSearch)
                    return false;

                nextViewerSearch = Time.unscaledTime + 1f;

                foreach (PlayerTargetDetector detector in FindObjectsByType<PlayerTargetDetector>(FindObjectsSortMode.None))
                {
                    if (detector.isActiveAndEnabled && detector.Object != null && detector.Object.IsValid && detector.HasInputAuthority)
                    {
                        localViewer = detector;
                        break;
                    }
                }

                if (localViewer == null)
                    return false;
            }

            if (!localViewer.TryGetAimRay(out Ray ray))
                return false;

            position = ray.origin;
            return true;
        }
    }
}
