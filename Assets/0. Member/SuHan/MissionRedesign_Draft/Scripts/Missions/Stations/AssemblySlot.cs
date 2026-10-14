using Fusion;
using UnityEngine;

namespace TrustNoOne.Missions
{
    /// <summary>
    /// [역할] 장비 조립 자리 하나 (색 하나): 조준 "주소" · 색 번호 · 조준 콜라이더. 상태(붙은 시각)는 AssemblyStation 이 가진다. 7c 명세 AR3.
    ///  - 주소: 자기 중첩 NetworkObject 를 TargetObject 로 알려 준다. 팀원 아이템 코드가 우클릭 대상의 NetworkObject 하나만 보내고 그 위치까지 3m 를 검사하므로,
    ///    어느 색 자리를 조준했는지 알고 3m 기준이 자리가 되려면 자리마다 NetworkObject 가 필요하다 (방화문 패널 FD10 과 같은 방식).
    ///  - 콜라이더: 장비가 매 Render 에 "내 미션 && 빈 자리"일 때만 켠다 (AR8 — 필터 먼지 F8 과 같은 방식).
    /// [IItemUseTarget] 아이템 우클릭 대상 표시 (2c F10 · 방화문 패널과 같음).
    /// [근거] 7c 명세 AR3 · AR4 · AR8 (사용자 결정 C — 맞는 색 자리를 조준하고 우클릭)
    /// </summary>
    public class AssemblySlot : MonoBehaviour, ITargetable, IItemUseTarget
    {
        [SerializeField] private AssemblyStation station;

        [Tooltip("자리 색 번호 — 0 파랑 · 1 회색 · 2 빨강 · 3 노랑 (EquipmentPart 의 색 번호와 같아야 끼워진다)")]
        [Range(0, AssemblyRules.PartCount - 1)]
        [SerializeField] private int colorIndex;

        [Tooltip("조준 콜라이더 (조준 레이어, 부품 크기 상자)")]
        [SerializeField] private Collider slotCollider;

        private NetworkObject networkObject;
        private bool? colliderShown;

        public NetworkObject TargetObject => networkObject;
        public AssemblyStation Station => station;
        public int ColorIndex => colorIndex;

        private void Awake()
        {
            if (station == null)
                station = GetComponentInParent<AssemblyStation>();

            networkObject = GetComponent<NetworkObject>();
        }

        /// <summary>조준 콜라이더 켜기/끄기 (바뀔 때만).</summary>
        public void SetColliderEnabled(bool enabled)
        {
            if (slotCollider == null || colliderShown == enabled)
                return;

            colliderShown = enabled;
            slotCollider.enabled = enabled;
        }
    }
}
