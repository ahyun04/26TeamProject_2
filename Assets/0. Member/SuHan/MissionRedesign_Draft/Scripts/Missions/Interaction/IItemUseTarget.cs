namespace TrustNoOne.Missions
{
    /// <summary>
    /// [표시용] 플레이어가 든 아이템의 우클릭(IItemUseHandler)이 이 대상에 작용한다. 이 대상은 입력을 직접 받지 않는다. 2c 명세 F10.
    ///  예) 필터 먼지 — 청소기를 든 채 조준하고 우클릭하면 청소기가 스테이션에 흡입을 요청한다.
    /// [필요한 이유] 변환기의 조준 검증은 "조준했을 때 입력을 받는 대상이 잡히는가"를 본다. 먼지는 클릭 · 홀드 · 드래그를 받지 않아
    ///  이 표시가 없으면 오류로 걸린다.
    /// </summary>
    public interface IItemUseTarget
    {
    }
}
