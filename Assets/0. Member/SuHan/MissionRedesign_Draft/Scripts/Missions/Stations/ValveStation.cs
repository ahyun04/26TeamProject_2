using UnityEngine;

namespace TrustNoOne.Missions
{
    /// <summary>
    /// [역할] 밸브 (단체 미션 TG006 "밸브 잠그기" · ValveClosed — 7a 전에는 개인 PS001). 옛 ValveMission 이식. 2a 명세 2-3.
    ///  F 를 누르고 있는 동안 밸브가 돌고, 이번 판 목표 회전량(360~720°)에 도달하면 완료 → 잠김, 돌린 모습 그대로(Lock — 7a 명세 TC9).
    ///  손을 떼거나 범위를 벗어나면 처음부터 (명세 P1).
    ///
    /// [코드가 거의 없는 이유] 회전·목표 각도·완료는 전부 RotationHoldStation 과 HoldStation 에 있다.
    ///  이 클래스는 "밸브"라는 이름(인스펙터·로그에서 구분)과 옛 ValveMission 의 기본 회전축만 가진다.
    /// </summary>
    public class ValveStation : RotationHoldStation
    {
        // 옛 ValveMission 의 기본 회전축: 밸브 손잡이는 정면(forward) 축으로 돈다
        protected override Vector3 FallbackAxis => Vector3.forward;
    }
}
