using System.Collections.Generic;
using Fusion;
using UnityEngine;

namespace TrustNoOne.Missions
{
    /// <summary>
    /// [역할] IMissionActorGate 의 기본 구현: 기존 PlayerHealth 를 기준으로 플레이어 상태를 판단한다.
    ///        씬에 1개만 배치한다 (모든 MissionStation 이 같은 인스턴스를 공유해야 "다른 미션 중" 판단이 맞다).
    ///
    /// [판단 기준]
    ///  - PlayerHealth.CanAct : 살아 있고, 탈출하지 않았고, 게임이 끝나지 않았을 때 true
    ///    (기획서의 "생존 상태 / 탈출 상태 아님 / 피격·사망 시 취소"를 이 한 값이 포괄한다)
    ///  - busy 집합 : 이미 다른 미션 오브젝트를 사용 중인가 ("다른 행동 중이 아님")
    ///
    /// [한계] "피격" 은 별도 상태가 없어 CanAct 로는 잡히지 않는다. 피격 시 상호작용을 끊으려면
    ///  플레이어 쪽에서 피격 상태를 노출한 뒤 이 클래스의 CanContinue 에 조건을 추가해야 한다 (플레이어 시스템 협의 필요).
    ///  "살인 애니메이션"도 마찬가지.
    ///
    /// [CanAct 접근] PlayerHealth.CanAct 는 internal 이라 같은 어셈블리(Assembly-CSharp)에서만 접근 가능하다.
    ///  이 파일을 별도 asmdef 로 분리하면 컴파일 에러가 나므로 주의.
    /// </summary>
    public class PlayerHealthActorGate : MonoBehaviour, IMissionActorGate
    {
        private readonly HashSet<PlayerRef> busyActors = new HashSet<PlayerRef>();

        public bool CanStart(NetworkRunner runner, PlayerRef actor)
        {
            return CanContinue(runner, actor) && !busyActors.Contains(actor);
        }

        public bool CanContinue(NetworkRunner runner, PlayerRef actor)
        {
            if (runner == null || !runner.TryGetPlayerObject(actor, out NetworkObject playerObject) || playerObject == null)
                return false;

            PlayerHealth health = playerObject.GetComponent<PlayerHealth>();

            return health != null && health.CanAct;
        }

        public void SetBusy(PlayerRef actor, bool busy)
        {
            if (busy)
                busyActors.Add(actor);
            else
                busyActors.Remove(actor);
        }
    }
}
