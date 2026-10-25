using Fusion;

namespace TrustNoOne.Missions
{
    /// <summary>
    /// [역할] 미션 아이템(산소통 · 장비 부품 · 방화문 레버 · 청소기) 공용 도우미 (호스트). 3c 명세 3-6 (3b LS13 에서 옮김) · 9c.
    ///  - 지우기: 플레이어가 들고 있는 아이템을 그대로 지우면 팀원 PlayerItemController 의 손 상태(CurrentItemObject)가 남는다.
    ///    그래서 먼저 손에서 내려놓게 하고(팀원 internal 함수 DropCurrentItem 호출만 — 팀원 코드는 수정하지 않는다) 지운다.
    ///  - 손에 들었나 (9c): 팀원 인벤토리(2칸)가 생긴 뒤로 주머니 칸 아이템도 HolderObject 가 그 사람으로 남는다 → 장치 4곳의 "든 사람" 검사를 한 곳에 모은다.
    /// </summary>
    public static class MissionItems
    {
        /// <summary>
        /// actor 가 이 아이템을 손에 들고 있는가 (호스트 판정 — 9c). HolderObject 가 그 사람 &amp;&amp; 손 칸(IsInHand — 팀원 값, 읽기만).
        /// 주머니 칸에 넣은 아이템은 들고 있지 않은 것으로 본다 → 흡입 중 청소기를 주머니에 넣으면 그 먼지만 취소된다 (2c F4 와 같은 처리).
        /// </summary>
        public static bool IsInHandOf(NetworkRunner runner, ItemBase item, PlayerRef actor)
        {
            if (runner == null || item == null || actor.IsNone)
                return false;

            return runner.TryGetPlayerObject(actor, out NetworkObject playerObject)
                && playerObject != null
                && item.HolderObject == playerObject
                && item.IsInHand;
        }

        /// <summary>들고 있는 사람의 손에서 내려놓게 한다 (호스트). 손 상태가 이 아이템이 아니면 아이템만 내려놓는다.</summary>
        public static void TakeFromHand(ItemBase item)
        {
            if (item == null)
                return;

            NetworkObject holder = item.HolderObject;

            if (holder == null)
                return;

            PlayerItemController controller = holder.GetComponent<PlayerItemController>();

            if (controller != null && controller.CurrentItemObject == item.Object)
                controller.DropCurrentItem();
            else
                item.Unequip();
        }

        /// <summary>손에서 내려놓게 한 뒤 지운다 (호스트). 이미 지워졌으면 아무것도 하지 않는다.</summary>
        public static void Despawn(NetworkRunner runner, ItemBase item)
        {
            if (runner == null || item == null || item.Object == null || !item.Object.IsValid)
                return;

            TakeFromHand(item);
            runner.Despawn(item.Object);
        }
    }
}
