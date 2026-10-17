using Fusion;

namespace TrustNoOne.Missions
{
    /// <summary>
    /// [역할] 산소통 아이템 (단체 미션 TG004 생명 유지 장치). 신규. 3b 명세 3-2.
    ///  - 좌클릭 줍기 · G 내려놓기 · 우클릭 사용은 팀원 아이템 구조(ItemBase · PlayerItemController) 그대로 (2c 청소기와 같다).
    ///  - 우클릭(호스트): 조준 대상이 생명 유지 장치 본체면 본체에 넣기를 요청한다. 받을지는 본체가 정한다 (판정 중 · 성공함 · 범위 등).
    /// [겉모습 — 7d L3B14] 정상 · 위험이 겉모습으로 구분되지 않는다. 밸브 옆 압력계(3b 보강 명세 LM10)는 늘 회색 장식이다.
    ///  기획 의도 "무엇을 가져왔는지 몰라야 한다" — 결과는 본체에 넣고 5초 뒤에야 드러난다 (7d L3B3).
    ///  3b LS1 의 "가까이 가면 압력계가 초록 · 빨강" 은 7d 테스트 후 없앴다 (색으로 구분할 수 있어 의도와 어긋남).
    /// [동기화 — LS14] 위험 여부(Dangerous)만. 호스트가 만들기 직전(Runner.Spawn 의 onBeforeSpawned)에 정한다. 판정에만 쓴다.
    /// [살인마 — LS8 · 7b-2] 줍기는 팀원 코드에 역할 제한이 없다. 넣기는 본체의 공통 자격 검사가 정한다 (7b-2 로 살인마도 참여).
    /// [팀원 코드] ItemBase 는 상속만 한다 (덮어쓰는 함수 없음).
    /// </summary>
    public class OxygenTank : ItemBase, IItemUseHandler
    {
        /// <summary>위험한 산소통인가 (넣으면 5초 뒤 폭발). 호스트가 만들 때 정한다. 겉모습에는 드러나지 않는다 (7d L3B14).</summary>
        [Networked] public NetworkBool Dangerous { get; private set; }

        /// <summary>호스트: 만들기 직전(Runner.Spawn 의 onBeforeSpawned)에 위험 여부를 정한다.</summary>
        public void HostSetDangerous(bool dangerous)
        {
            Dangerous = dangerous;
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
    }
}
