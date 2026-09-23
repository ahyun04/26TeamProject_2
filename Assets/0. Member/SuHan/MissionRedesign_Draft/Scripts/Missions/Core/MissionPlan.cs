using System.Collections.Generic;

namespace TrustNoOne.Missions
{
    /// <summary>
    /// [역할] MissionGenerator 의 결과물: "이번 판에 누구에게 어떤 목표가 가는가".
    ///
    /// [Objectives 의 순서가 곧 슬롯 번호]
    ///  MissionManager 는 이 리스트의 인덱스를 네트워크 동기화의 "슬롯"으로 사용한다.
    ///  순서는 생성 순서(단체 → 개인 → 살인마 → 행동 목표)이며 이 판이 끝날 때까지 바뀌지 않는다.
    ///
    /// [Warnings] 풀 부족, 충돌로 인한 미배정 등 "게임은 진행되지만 기획자가 알아야 할 일".
    ///  생성기는 Unity 로그를 직접 쓰지 않고(순수 C# 유지) 여기에 모아서 매니저가 출력한다.
    /// </summary>
    public class MissionPlan
    {
        public List<IMissionObjective> Objectives { get; } = new List<IMissionObjective>();
        public List<string> Warnings { get; } = new List<string>();
    }
}
