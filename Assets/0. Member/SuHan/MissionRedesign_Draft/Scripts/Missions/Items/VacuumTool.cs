using Fusion;
using UnityEngine;

namespace TrustNoOne.Missions
{
    /// <summary>
    /// [역할] 청소기 아이템. 옛 VacuumItem 이식. 2c 명세 3-6.
    ///  - 우클릭(호스트): 팀원의 PlayerItemController 가 행동 가능 · 거리를 검사한 뒤 UseAsStateAuthority(조준 대상) 를 부른다
    ///    → 대상이 필터 먼지면 FilterStation.RequestSuction. 판정은 모두 스테이션이 한다.
    ///  - 조준(내 화면): 빨아들일 수 있는 먼지를 조준하면 그 먼지 외곽선만 켠다 (옛 방식 — 결정 F7).
    ///  - 흡입구: 들고 있는 본인 화면이면 1인칭 모델의 흡입구 (먼지가 날아갈 곳).
    ///
    /// [팀원 코드] ItemBase(장착 · 해제 · 월드 표시)는 상속만 한다. 아이템 번호 · 1인칭 모델은 ItemData(VacuumToolData) 에서 온다.
    /// [흡입 시간을 청소기가 갖는 이유] 옛 구조와 같다 — 나중에 성능이 다른 청소기를 만들 여지.
    /// </summary>
    public class VacuumTool : ItemBase, IItemUseHandler, IItemTargetHandler
    {
        [Header("청소기")]
        [SerializeField] private Transform mouth;

        [Header("흡입")]
        [SerializeField] private float suctionDuration = 0.6f;

        private FilterDust highlightedDust;

        public Transform Mouth => mouth;

        public void UseAsStateAuthority(NetworkObject target)
        {
            if (!HasStateAuthority || target == null || HolderObject == null)
                return;

            // GetComponent(자기 오브젝트)만 본다: 먼지마다 중첩 NetworkObject 가 있어 조준한 그 먼지가 정확히 잡힌다
            FilterDust dust = target.GetComponent<FilterDust>();

            if (dust == null || dust.Station == null)
                return;

            dust.Station.RequestSuction(HolderObject.InputAuthority, dust.Index, this, suctionDuration);
        }

        public void SetLocalTarget(ITargetable target)
        {
            FilterDust dust = target as FilterDust;

            if (dust != null && (dust.Station == null || !dust.Station.CanSuckLocally(dust.Index)))
                dust = null;

            if (highlightedDust == dust)
                return;

            if (highlightedDust != null)
                highlightedDust.SetHighlight(false);

            highlightedDust = dust;

            if (highlightedDust != null)
                highlightedDust.SetHighlight(true);
        }

        public void ClearLocalTarget()
        {
            if (highlightedDust != null)
                highlightedDust.SetHighlight(false);

            highlightedDust = null;
        }

        /// <summary>들고 있는 사람이 이 PC 의 플레이어면 1인칭 흡입구, 아니면 null.</summary>
        public Transform GetLocalMouth()
        {
            if (HolderObject == null || !HolderObject.HasInputAuthority)
                return null;

            PlayerFirstPersonItemView view = HolderObject.GetComponent<PlayerFirstPersonItemView>();
            VacuumFirstPersonView firstPerson = view != null ? view.GetCurrentView<VacuumFirstPersonView>() : null;
            return firstPerson != null ? firstPerson.Mouth : null;
        }
    }
}
