using Fusion;

namespace TrustNoOne.Missions
{
    /// <summary>
    /// [역할] 방화문 레버 아이템 (단체 미션 TG005 대형 방화문 열기). 신규. 3d 명세 3-2.
    ///  좌클릭 줍기 · G 내려놓기는 팀원 아이템 구조(ItemBase) 그대로.
    ///  우클릭(호스트): 조준 대상이 방화문 패널이면 끼우기를 요청한다. 받을지는 방화문 장치가 정한다 (해금 · 빈 패널 · 범인 등 — FD2 · FD3 · FD7).
    /// [기획서] "떨어진 레버를 주워 대형 방화문 양쪽에 있는 레버 장착 위치까지 가져간다 / 양쪽 레버 위치에 레버를 모두 끼운다".
    /// </summary>
    public class FireDoorLever : ItemBase, IItemUseHandler
    {
        /// <summary>호스트: 팀원 PlayerItemController 가 행동 가능 · 거리(대상 NetworkObject 3m)를 검사한 뒤 부른다.</summary>
        public void UseAsStateAuthority(NetworkObject target)
        {
            if (!HasStateAuthority || target == null || HolderObject == null)
                return;

            // 패널마다 중첩 NetworkObject 가 있어 조준한 그 패널이 잡힌다 (FD10)
            FireDoorPanel panel = target.GetComponent<FireDoorPanel>();

            if (panel == null || panel.Station == null)
                return;

            panel.Station.RequestMount(HolderObject.InputAuthority, this, panel.Index);
        }
    }
}
