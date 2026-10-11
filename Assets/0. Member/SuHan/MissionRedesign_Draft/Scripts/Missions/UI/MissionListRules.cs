using System.Collections.Generic;

namespace TrustNoOne.Missions
{
    /// <summary>미션 목록 UI 의 한 줄 (읽기 전용 값). Personal 이 true 면 개인 칸, false 면 단체 칸에 들어간다.</summary>
    public readonly struct MissionListLine
    {
        public readonly bool Personal;
        public readonly string Label;
        public readonly string Name;
        public readonly int Progress;
        public readonly int Required;
        public readonly bool Completed;

        public MissionListLine(bool personal, string label, string name, int progress, int required, bool completed)
        {
            Personal = personal;
            Label = label;
            Name = name;
            Progress = progress;
            Required = required;
            Completed = completed;
        }
    }

    /// <summary>
    /// [역할] 미션 목록 UI 의 줄을 정한다 (순수 규칙 — 6c).
    ///  - 단체 칸: 공개된 단체 미션 전부 (살인마도 본다 — 옛 MissionUI 와 같음), 꼬리표 "단체"
    ///  - 개인 칸: 내 개인 미션 "개인", 내 살인마 미션 "살인마" (지금은 0개 — 기획되면 바로 보이게)
    ///  - 행동 목표는 넣지 않는다 (숨겨진 목표, 4단계 진행 안 함). 정의가 없는 줄은 건너뛴다
    ///  - 이름: 표시 이름, 비어 있으면 미션 ID. 완료 여부: 상태가 Completed
    /// [이유] 어떤 미션을 어느 칸에 어떤 꼬리표로 보일지를 화면 코드(MissionListUI)와 떼어 엔진 밖에서 테스트한다.
    /// [근거] 6c 결정 — 사용자 결정 A (질문 1: 옛 형식 "[단체] 이름 ( p / r )" + 완료 표시), 옛 MissionUI 의 단체 · 개인 칸
    /// </summary>
    public static class MissionListRules
    {
        public const string TeamLabel = "단체";
        public const string PersonalLabel = "개인";
        public const string KillerLabel = "살인마";

        /// <param name="team">공개된 단체 미션 (MissionClientState.Team)</param>
        /// <param name="mine">이 PC 의 내 몫 (MissionClientState.Mine — 개인 미션 · 행동 목표 · 살인마 미션)</param>
        /// <param name="lines">결과 (앞 내용은 지운다). 단체 줄이 먼저, 그다음 내 줄</param>
        public static void Build(IReadOnlyList<ObjectiveView> team, IReadOnlyList<ObjectiveView> mine, List<MissionListLine> lines)
        {
            lines.Clear();

            foreach (ObjectiveView view in team)
            {
                if (view.Definition == null || view.Definition.Category != MissionCategory.Team)
                    continue;

                lines.Add(ToLine(view, false, TeamLabel));
            }

            foreach (ObjectiveView view in mine)
            {
                if (view.Definition == null)
                    continue;

                MissionCategory category = view.Definition.Category;

                if (category == MissionCategory.Personal)
                    lines.Add(ToLine(view, true, PersonalLabel));
                else if (category == MissionCategory.Killer)
                    lines.Add(ToLine(view, true, KillerLabel));
            }
        }

        private static MissionListLine ToLine(ObjectiveView view, bool personal, string label)
        {
            string name = string.IsNullOrEmpty(view.Definition.DisplayName) ? view.Definition.Id : view.Definition.DisplayName;
            return new MissionListLine(personal, label, name, view.Progress, view.Required, view.Status == ObjectiveStatus.Completed);
        }
    }
}
