using Fusion;
using UnityEngine;

namespace TrustNoOne.Missions
{
    /// <summary>
    /// [역할] 장비 부품 아이템 (단체 미션 TG003 고장난 장비 조립). 신규. 3c 명세 3-2.
    ///  좌클릭 줍기 · G 내려놓기는 팀원 아이템 구조(ItemBase) 그대로.
    ///  우클릭(호스트 — 7c): 조준 대상이 장비 자리(AssemblySlot)면 끼우기를 요청한다. 받을지는 장비가 정한다 (같은 색 빈 자리 · 자격 — AR5).
    /// [색] 프리팹마다 정해진 직렬화 값. 색마다 모델이 달라 프리팹이 따로라서 동기화하지 않는다 (EA10).
    /// [근거] 7c 명세 AR1 · AR4 (사용자 결정 C — 자동 배치 대신 맞는 색 자리 조준 + 우클릭). 방화문 레버(FireDoorLever)와 같은 방식
    /// </summary>
    public class EquipmentPart : ItemBase, IItemUseHandler
    {
        [Tooltip("색 번호 — 0 파랑 · 1 회색 · 2 빨강 · 3 노랑 (장비 자리 순서와 같다)")]
        [Range(0, AssemblyRules.PartCount - 1)]
        [SerializeField] private int colorIndex;

        public int ColorIndex => colorIndex;

        /// <summary>호스트: 팀원 PlayerItemController 가 행동 가능 · 거리(대상 NetworkObject 3m)를 검사한 뒤 부른다. 대상이 없으면 null.</summary>
        public void UseAsStateAuthority(NetworkObject target)
        {
            if (!HasStateAuthority || target == null || HolderObject == null)
                return;

            // 자리마다 중첩 NetworkObject 가 있어 조준한 그 자리가 잡힌다 (AR3)
            AssemblySlot slot = target.GetComponent<AssemblySlot>();

            if (slot == null || slot.Station == null)
                return;

            slot.Station.RequestAttach(HolderObject.InputAuthority, this, slot.ColorIndex);
        }
    }
}
