using Fusion;

namespace TrustNoOne.Missions
{
    /// <summary>
    /// [역할] "이 플레이어가 지금 미션 상호작용을 할 수 있는 상태인가"를 판단하는 관문.
    ///
    /// [기획서 근거] 입력 가능 조건 표(생존 / 상호작용 가능 / 미션 오브젝트 범위 내 / 다른 행동 중 아님 / 탈출 상태 아님)와
    ///  상호작용 취소 조건(범위 이탈 · 입력 취소 · 피격 · 사망 · 다른 행동 · 살인 애니메이션).
    ///  이 중 "범위"와 "입력 취소"는 MissionStation 이 직접 처리하고, 나머지 "플레이어 상태" 판단이 여기 담긴다.
    ///
    /// [왜 분리했는가] 플레이어 상태(체력/탈출/행동 중)는 플레이어 시스템의 관심사다.
    ///  미션 오브젝트가 PlayerHealth 를 직접 알면, 플레이어 쪽이 바뀔 때마다 모든 미션 오브젝트가 깨진다.
    ///  기본 구현은 PlayerHealthActorGate 이고, 규칙이 바뀌면 이 인터페이스의 다른 구현으로 교체하면 된다.
    /// </summary>
    public interface IMissionActorGate
    {
        /// <summary>상호작용을 "시작"할 수 있는가 (다른 미션을 하는 중이면 불가).</summary>
        bool CanStart(NetworkRunner runner, PlayerRef actor);

        /// <summary>상호작용을 "계속"할 수 있는가 (피격/사망/탈출/게임 종료 시 false → 취소).</summary>
        bool CanContinue(NetworkRunner runner, PlayerRef actor);

        /// <summary>그 플레이어가 지금 미션 상호작용 중인지 표시/해제한다 ("다른 행동 중이 아님" 조건).</summary>
        void SetBusy(PlayerRef actor, bool busy);
    }
}
