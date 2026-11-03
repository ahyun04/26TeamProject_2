namespace TrustNoOne.Missions
{
    /// <summary>
    /// [역할] "번호 붙은 누르고 있기"를 받는 장치의 약속. 장치 안의 누르고 있는 부품(StationHoldPart)이 F 누름 · 뗌을 부품 번호와 함께 넘긴다.
    ///  쓰는 곳: 배관(HoldStation — 13a 새는 곳 0 ~ 5), 좌표 설정(CoordinateStation — 13b 초록 0 · 노랑 1)
    /// [이유] 13a 의 부품은 HoldStation(한 명이 진행 게이지를 채우는 장치) 전용이었다. 좌표 장치는 두 사람이 각자 자리를 잡아 진행 게이지 없이
    ///  누르는 동안만 움직이므로 HoldStation 을 상속하지 않는다 → 부품이 장치 종류 대신 이 약속만 보게 넓혔다.
    /// [근거] 13b 명세 CO27
    /// </summary>
    public interface IHoldPartReceiver
    {
        /// <summary>부품 part 를 F 로 누르기 시작했다 (클라이언트 → 호스트 요청은 장치가 RPC 로 보낸다).</summary>
        void BeginHoldPart(int part);

        /// <summary>부품 part 에서 F 를 뗐다 (조준이 바뀌어 팀원 입력 코드가 보내는 뗌 포함).</summary>
        void EndHoldPart(int part);
    }
}
