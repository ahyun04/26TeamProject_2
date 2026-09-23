using System.Collections.Generic;
using Fusion;

namespace TrustNoOne.Missions
{
    /// <summary>
    /// [역할] 플레이어별 "행동 종류별 횟수" 기록. 호스트에서만 존재한다.
    ///
    /// [왜 따로 두는가]
    ///  게임 종료 판정, Custom 목표, 그리고 나중의 거짓말탐지기 단서가 모두
    ///  "누가 언제 무슨 행동을 했나"라는 같은 데이터를 필요로 한다.
    ///  한 곳에서 기록하면 뒤의 시스템은 이 클래스를 읽기만 하면 된다 (미션 코어 수정 불필요).
    ///
    /// [YAGNI] 지금은 횟수만 기록한다. 시각/대상이 필요한 단서가 생기면 그때 항목을 늘린다.
    /// </summary>
    public class PlayerActionLog
    {
        private static readonly IReadOnlyDictionary<MissionEventType, int> Empty =
            new Dictionary<MissionEventType, int>();

        private readonly Dictionary<PlayerRef, Dictionary<MissionEventType, int>> counts =
            new Dictionary<PlayerRef, Dictionary<MissionEventType, int>>();

        public void Record(in MissionEvent e)
        {
            if (!counts.TryGetValue(e.Actor, out Dictionary<MissionEventType, int> perPlayer))
            {
                perPlayer = new Dictionary<MissionEventType, int>();
                counts.Add(e.Actor, perPlayer);
            }

            perPlayer.TryGetValue(e.Type, out int current);
            perPlayer[e.Type] = current + e.Amount;
        }

        public int GetCount(PlayerRef player, MissionEventType type)
        {
            if (counts.TryGetValue(player, out Dictionary<MissionEventType, int> perPlayer) &&
                perPlayer.TryGetValue(type, out int count))
            {
                return count;
            }

            return 0;
        }

        public bool HasDone(PlayerRef player, MissionEventType type)
        {
            return GetCount(player, type) > 0;
        }

        /// <summary>플레이어의 전체 기록(읽기 전용). 탐지기 같은 후속 시스템이 사용.</summary>
        public IReadOnlyDictionary<MissionEventType, int> GetCounts(PlayerRef player)
        {
            return counts.TryGetValue(player, out Dictionary<MissionEventType, int> perPlayer) ? perPlayer : Empty;
        }

        public void Clear()
        {
            counts.Clear();
        }
    }
}
