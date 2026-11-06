using System.Collections.Generic;
using Fusion;
using UnityEngine;

namespace TrustNoOne.Missions
{
    /// <summary>
    /// [역할] 미션 장치가 플레이어 몸의 상호작용 동작(버튼 누르기 · 밸브 돌리기)을 켤 때 쓰는 도우미 (각 PC — 연출만). 14b 명세 IA6 · IA15.
    ///  플레이어 번호(InputAuthority — 모든 PC 가 안다)로 그 사람의 팀원 PlayerAnimation 을 찾아 playMissionPress · setMissionTurning 을 부른다.
    /// [찾기] 찾은 목록을 기억해 두고, 목록에 없으면 0.5초마다 다시 찾는다 (MissionItems 의 내 플레이어 찾기와 같은 방식 — 매 프레임 씬 전체를 뒤지지 않게).
    ///  못 찾으면 false — 부르는 쪽이 다음 프레임에 다시 시도하거나 그냥 건너뛴다 (동작은 연출이라 놓쳐도 판정에는 영향 없음).
    /// [근거] 14b 명세 IA6 (접근법 1 — 장치가 알린다), IA15
    /// </summary>
    public static class MissionPlayerAnimation
    {
        private const float SearchInterval = 0.5f;

        private static readonly List<PlayerAnimation> found = new List<PlayerAnimation>();
        private static float nextSearch;

        /// <summary>그 사람 몸에서 버튼 동작을 한 번 (재생 중이면 PlayerAnimation 이 무시 — IA4). 찾았으면 true.</summary>
        public static bool PlayPress(PlayerRef player)
        {
            PlayerAnimation animation = Find(player);

            if (animation == null)
                return false;

            animation.playMissionPress();
            return true;
        }

        /// <summary>그 사람 몸의 돌리기 동작을 켜거나 끈다 (IA3). 찾았으면 true.</summary>
        public static bool SetTurning(PlayerRef player, bool turning)
        {
            PlayerAnimation animation = Find(player);

            if (animation == null)
                return false;

            animation.setMissionTurning(turning);
            return true;
        }

        private static PlayerAnimation Find(PlayerRef player)
        {
            if (player.IsNone)
                return null;

            PlayerAnimation hit = Match(player);

            if (hit != null || Time.unscaledTime < nextSearch)
                return hit;

            nextSearch = Time.unscaledTime + SearchInterval;
            found.Clear();
            found.AddRange(Object.FindObjectsByType<PlayerAnimation>(FindObjectsSortMode.None));
            return Match(player);
        }

        private static PlayerAnimation Match(PlayerRef player)
        {
            foreach (PlayerAnimation animation in found)
            {
                if (animation != null && animation.Object != null && animation.Object.IsValid && animation.Object.InputAuthority == player)
                    return animation;
            }

            return null;
        }
    }
}
