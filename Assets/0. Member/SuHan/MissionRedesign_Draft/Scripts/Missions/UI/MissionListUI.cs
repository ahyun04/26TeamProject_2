using TMPro;
using UnityEngine;

namespace TrustNoOne.Missions
{
    /// <summary>
    /// [역할] 메인 게임 화면 왼쪽 위 미션 목록: 완료 개수 요약 · 단체 미션(모두) · 내 개인 미션을 글자 칸 하나에 쓴다 (10 명세). 옛 MissionUI 를 대신한다 (6c).
    /// [동작] 미션 관리자의 화면용 정보(Client — 이 PC 가 봐도 되는 것만)가 바뀌었다는 알림(Changed)이 오거나,
    ///  남은 시간 숫자가 바뀔 때(MissionListRules.NextRefreshTime)에만 글을 다시 만든다. 글이 같으면 글자 칸에 다시 넣지 않는다.
    ///  무엇을 어떤 형식 · 색으로 쓸지는 순수 규칙 MissionListRules (테스트 T35).
    /// [지금 시각] 미션 관리자 러너의 시뮬레이션 시각 — 마감(호스트가 동기화)과 같은 기준이다 (stage1 명세 2-4, 테스트 패널과 같음).
    /// [미션 관리자를 기다리는 이유] 씬 오브젝트라 러너가 등록한 뒤에야 Client 가 생긴다 → 생길 때까지 매 프레임 확인하고 한 번만 구독한다.
    /// [글자 칸] 메뉴 SuHan/Apply Missions To 4~12 · GamePlay 가 패널 안에 만들어 연결한다 (MissionMapInstaller.SetupMissionList — ML12).
    ///  예전 줄 프리팹(팀원 MissionUIItem_Text) · 단체 칸 · 개인 칸은 더 쓰지 않는다 (씬에서는 꺼 둔다 — ML11).
    /// [근거] 10 명세 ML1 · ML4 (접근 1 — 글자 칸 하나) · ML14, 6c UI2 (옛 MissionUI 바꿔 끼우기)
    /// </summary>
    public class MissionListUI : MonoBehaviour
    {
        [Tooltip("비워 두면 씬에서 찾는다")]
        [SerializeField] private MissionManager manager;

        [Tooltip("목록 전체를 쓰는 글자 칸 (메뉴 SuHan/Apply Missions To … 가 만들어 연결한다)")]
        [SerializeField] private TMP_Text listText;

        private MissionClientState client;
        private bool dirty;
        private double nextRefreshTime = double.MaxValue;
        private bool warnedMissingText;

        private void Update()
        {
            if (client == null && !TryAttach())
                return;

            if (!dirty && Now() < nextRefreshTime)
                return;

            Rebuild();
        }

        private void OnDestroy()
        {
            if (client != null)
                client.Changed -= MarkDirty;
        }

        private bool TryAttach()
        {
            if (manager == null)
                manager = FindFirstObjectByType<MissionManager>();

            if (manager == null || manager.Client == null)
                return false;

            client = manager.Client;
            client.Changed += MarkDirty;
            dirty = true;
            return true;
        }

        private void MarkDirty()
        {
            dirty = true;
        }

        /// <summary>지금 시뮬레이션 시각. 러너가 없으면(게임 종료 중) 0 — 남은 시간 갱신만 멈추고 상태 알림 갱신은 그대로.</summary>
        private double Now()
        {
            return manager != null && manager.Runner != null ? manager.Runner.SimulationTime : 0d;
        }

        private void Rebuild()
        {
            dirty = false;
            double now = Now();
            nextRefreshTime = MissionListRules.NextRefreshTime(client.Team, client.Mine, now);

            if (listText == null)
            {
                if (!warnedMissingText)
                {
                    warnedMissingText = true;
                    Debug.LogWarning("[MissionListUI] 목록 글자 칸이 연결되지 않았습니다 — SuHan/Apply Missions To 4~12 (또는 GamePlay) 를 실행하세요.", this);
                }

                return;
            }

            string text = MissionListRules.BuildText(client.Team, client.Mine, now);

            if (listText.text != text)
                listText.text = text;
        }
    }
}
