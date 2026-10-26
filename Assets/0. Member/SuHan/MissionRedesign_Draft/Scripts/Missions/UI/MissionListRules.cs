using System;
using System.Collections.Generic;
using System.Text;

namespace TrustNoOne.Missions
{
    /// <summary>
    /// [역할] 미션 목록 UI 에 쓸 글 전체를 만든다 (순수 규칙 — 10 명세). 테스트 패널(MissionDebugOverlay)과 같은 형식이다.
    ///  - 1줄: <b>단체 미션 n / m 완료</b> (m = 공개된 단체 미션 수 · 방화문 포함, n = 그중 완료)
    ///  - 2줄: <b>[단체 미션]</b>, 그 아래 단체 미션 한 줄씩 (배정 순서 그대로 — 방화문 · 쉬움 · 보통 · 어려움)
    ///  - 내 몫에 개인 미션 · 살인마 미션이 있을 때만 <b>[개인 미션]</b> · <b>[살인마 미션]</b> 칸
    ///  - 줄: "  이름   진행/필요   상태" + 진행 중이고 마감이 있으면 "   남은 시간 m:ss" (주황)
    ///  - 행동 목표 · 정의가 없는 줄은 넣지 않는다. 공개된 단체 미션이 아직 없으면(배정 전) "미션 준비 중…" 한 줄
    /// [이유] 무엇을 어떤 순서 · 색으로 쓸지와 남은 시간 숫자가 언제 바뀌는지를 화면 코드(MissionListUI)와 떼어 엔진 밖에서 테스트한다.
    ///  화면 코드는 NextRefreshTime 이 알려 주는 때에만 글을 다시 만들면 된다 (매 프레임 글 만들기를 피함).
    /// [근거] 10 명세 ML1 (질문 1 — "우리 테스트 씬에서 하는 것처럼"), ML2 (질문 2 A — 완료 개수), ML5 ~ ML9 · ML14 · ML15.
    ///  6c 의 "[단체] 이름 ( p / r )" + 줄 전체 초록은 이 형식으로 바뀌었다 (ML9)
    /// </summary>
    public static class MissionListRules
    {
        // 테스트 패널과 같은 색 (ML5)
        public const string CompletedColor = "#7CFC7C";
        public const string FailedColor = "#FF7C7C";
        public const string TimerColor = "#FFB35C";

        public const string TeamTitle = "[단체 미션]";
        public const string PersonalTitle = "[개인 미션]";
        public const string KillerTitle = "[살인마 미션]";
        public const string WaitingText = "미션 준비 중…";

        // 줄 앞 들여쓰기 2칸 · 칸 사이 3칸 (테스트 패널과 같은 간격)
        private const string Indent = "  ";
        private const string Gap = "   ";

        /// <param name="team">공개된 단체 미션 (MissionClientState.Team)</param>
        /// <param name="mine">이 PC 의 내 몫 (MissionClientState.Mine — 개인 미션 · 행동 목표 · 살인마 미션)</param>
        /// <param name="now">지금 시뮬레이션 시각 (초) — 마감과 같은 기준</param>
        /// <returns>줄을 '\n' 으로 이은 글 (끝 줄바꿈 없음). TMP 색 · 굵게 태그 포함</returns>
        public static string BuildText(IReadOnlyList<ObjectiveView> team, IReadOnlyList<ObjectiveView> mine, double now)
        {
            CountTeam(team, out int completed, out int total);

            if (total == 0)
                return WaitingText;

            StringBuilder builder = new StringBuilder();
            builder.Append("<b>단체 미션 ").Append(completed).Append(" / ").Append(total).Append(" 완료</b>");
            builder.Append("\n<b>").Append(TeamTitle).Append("</b>");

            foreach (ObjectiveView view in team)
            {
                if (IsTeamLine(view))
                    builder.Append('\n').Append(FormatLine(view, now));
            }

            AppendSection(builder, mine, MissionCategory.Personal, PersonalTitle, now);
            AppendSection(builder, mine, MissionCategory.Killer, KillerTitle, now);
            return builder.ToString();
        }

        /// <summary>공개된 단체 미션 수와 그중 완료 수 (정의가 없거나 단체가 아닌 줄은 세지 않는다).</summary>
        public static void CountTeam(IReadOnlyList<ObjectiveView> team, out int completed, out int total)
        {
            completed = 0;
            total = 0;

            foreach (ObjectiveView view in team)
            {
                if (!IsTeamLine(view))
                    continue;

                total++;

                if (view.Status == ObjectiveStatus.Completed)
                    completed++;
            }
        }

        /// <summary>한 줄: "  이름   진행/필요   상태" (+ 진행 중이고 마감이 있으면 남은 시간). 이름은 표시 이름, 비면 ID.</summary>
        public static string FormatLine(ObjectiveView view, double now)
        {
            string name = view.Definition == null
                ? string.Empty
                : string.IsNullOrEmpty(view.Definition.DisplayName) ? view.Definition.Id : view.Definition.DisplayName;

            //완료된 줄은 "완료" 글자 없이 줄 전체를 초록색으로 표시
            string status = view.Status switch
            {
                ObjectiveStatus.Completed => string.Empty,
                ObjectiveStatus.Failed => $"<color={FailedColor}>실패</color>",
                _ => "진행 중",
            };

            string line = $"{Indent}{name}{Gap}{view.Progress}/{view.Required}";

            if (status.Length > 0)
                line += $"{Gap}{status}";

            if (HasTimer(view))
                line += $"{Gap}<color={TimerColor}>남은 시간 {FormatTime(view.Deadline - now)}</color>";

            if (view.Status == ObjectiveStatus.Completed)
                line = $"<color={CompletedColor}>{line}</color>";

            return line;
        }

        /// <summary>초 → "m:ss". 올림한다 (61.2초 → 1:02 — 0:00 이 되는 순간이 실제 마감과 맞게). 0 아래는 0:00.</summary>
        public static string FormatTime(double seconds)
        {
            int total = Math.Max(0, (int)Math.Ceiling(seconds));
            return $"{total / 60}:{total % 60:00}";
        }

        /// <summary>
        /// 보이는 줄 중 남은 시간 숫자가 다음에 바뀌는 가장 이른 시각. 그런 줄이 없으면 double.MaxValue (상태 알림 때만 다시 쓰면 된다).
        /// 남은 r = 마감 − now 를 올림한 d 가 보이는 숫자 → r 이 d − 1 이 되는 때(마감 − (d − 1))에 d − 1 로 바뀐다.
        /// d 가 0 이하(이미 0:00)면 더 바뀌지 않는다 — 호스트가 진행을 0 으로 되돌리면 상태 알림이 온다.
        /// </summary>
        public static double NextRefreshTime(IReadOnlyList<ObjectiveView> team, IReadOnlyList<ObjectiveView> mine, double now)
        {
            double next = double.MaxValue;

            foreach (ObjectiveView view in team)
            {
                if (IsTeamLine(view))
                    next = Math.Min(next, NextChange(view, now));
            }

            foreach (ObjectiveView view in mine)
            {
                if (IsMineLine(view))
                    next = Math.Min(next, NextChange(view, now));
            }

            return next;
        }

        private static double NextChange(ObjectiveView view, double now)
        {
            if (!HasTimer(view))
                return double.MaxValue;

            double shown = Math.Ceiling(view.Deadline - now);
            return shown >= 1d ? view.Deadline - (shown - 1d) : double.MaxValue;
        }

        /// <summary>남은 시간을 붙이는 줄: 진행 중이고 마감이 있다 (마감 0 = 없음 — TeamMissionState.Deadline).</summary>
        private static bool HasTimer(ObjectiveView view)
        {
            return view.Status == ObjectiveStatus.InProgress && view.Deadline > 0f;
        }

        private static bool IsTeamLine(ObjectiveView view)
        {
            return view.Definition != null && view.Definition.Category == MissionCategory.Team;
        }

        /// <summary>내 몫 중 목록에 쓰는 줄: 개인 · 살인마 미션 (행동 목표는 숨겨진 목표 — 9d 폐기, ML7).</summary>
        private static bool IsMineLine(ObjectiveView view)
        {
            return view.Definition != null
                && (view.Definition.Category == MissionCategory.Personal || view.Definition.Category == MissionCategory.Killer);
        }

        private static void AppendSection(StringBuilder builder, IReadOnlyList<ObjectiveView> mine, MissionCategory category, string title, double now)
        {
            bool titled = false;

            foreach (ObjectiveView view in mine)
            {
                if (view.Definition == null || view.Definition.Category != category)
                    continue;

                if (!titled)
                {
                    builder.Append("\n<b>").Append(title).Append("</b>");
                    titled = true;
                }

                builder.Append('\n').Append(FormatLine(view, now));
            }
        }
    }
}