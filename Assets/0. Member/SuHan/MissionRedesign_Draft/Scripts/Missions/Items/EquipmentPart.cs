using UnityEngine;

namespace TrustNoOne.Missions
{
    /// <summary>
    /// [역할] 장비 부품 아이템 (단체 미션 TG003 고장난 장비 조립). 신규. 3c 명세 3-2.
    ///  좌클릭 줍기 · G 내려놓기는 팀원 아이템 구조(ItemBase) 그대로. 우클릭 기능은 없다 —
    ///  부품을 든 채 조립 위치(장비 앞 바닥 원)에 들어가면 장비가 알아서 붙인다 (EA1 · AssemblyStation).
    /// [색] 프리팹마다 정해진 직렬화 값. 색마다 모델이 달라 프리팹이 따로라서 동기화하지 않는다 (EA10).
    /// [범인 — EA5] 줍기는 팀원 코드에 역할 제한이 없어 범인도 들 수 있다. 붙이기는 장비의 공통 자격 검사가 거부한다.
    /// </summary>
    public class EquipmentPart : ItemBase
    {
        [Tooltip("색 번호 — 0 파랑 · 1 회색 · 2 빨강 · 3 노랑 (장비 자리 순서와 같다)")]
        [Range(0, AssemblyRules.PartCount - 1)]
        [SerializeField] private int colorIndex;

        public int ColorIndex => colorIndex;
    }
}
