using Fusion;

namespace TrustNoOne.Missions
{
    /// <summary>
    /// [역할] 미션 아이템(산소통 · 장비 부품 · 이후 방화문 레버)을 호스트가 지울 때 쓰는 공용 도우미. 3c 명세 3-6 (3b LS13 에서 옮김).
    /// [이유] 플레이어가 들고 있는 아이템을 그대로 지우면 팀원 PlayerItemController 의 손 상태(CurrentItemObject)가 남는다.
    ///  그래서 먼저 손에서 내려놓게 하고(팀원 internal 함수 DropCurrentItem 호출만 — 팀원 코드는 수정하지 않는다) 지운다.
    /// </summary>
    public static class MissionItems
    {
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
