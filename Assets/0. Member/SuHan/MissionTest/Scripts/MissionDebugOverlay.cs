using System.Text;
using TrustNoOne.Missions;
using UnityEngine;

/// <summary>
/// [테스트 전용] 화면 왼쪽 위에 미션 상태를 글자로 보여주는 디버그 패널 (에셋 없이 IMGUI 로 그린다).
///
/// [보여주는 것] MissionClientState(= 이 PC가 볼 수 있는 정보만)를 그대로 출력한다.
///  시민 전체 진행도 %, 단체 완료/탈출 해금 여부, 단체 미션(+ 제한 시간이 있으면 남은 시간) · 내 개인 미션 · 내 행동 목표, 전체 공개 결과.
///  남은 시간 = 마감 시각(호스트가 동기화) - 현재 시뮬레이션 시각 (stage1 명세 2-4).
///
/// [단축키] 호스트에서만 동작한다.
///  F9  : 내 행동 확정 — 탈출/사망을 흉내낸다. "뛰지 않는다" 같은 종료 판정 목표가 이때 결과로 확정된다.
///  F10 : 게임 종료 전체 공개 (MissionManager.RevealAllMissions)
/// </summary>
public class MissionDebugOverlay : MonoBehaviour
{
    [SerializeField] private KeyCode finalizeKey = KeyCode.F9;
    [SerializeField] private KeyCode revealKey = KeyCode.F10;
    [SerializeField] private float panelWidth = 540f;

    private MissionManager manager;
    private GUIStyle style;
    private string lastAction = string.Empty;
    private readonly StringBuilder builder = new StringBuilder();

    private void Update()
    {
        if (!TryGetManager() || !manager.Object.HasStateAuthority || !manager.Initialized)
            return;

        if (Input.GetKeyDown(finalizeKey))
        {
            manager.FinalizePlayer(manager.Runner.LocalPlayer);
            lastAction = $"{finalizeKey}: 내 행동 확정 완료 (종료 판정 목표 확정)";
            Debug.Log($"[MissionDebugOverlay] {lastAction}");
        }

        if (Input.GetKeyDown(revealKey))
        {
            manager.RevealAllMissions();
            lastAction = $"{revealKey}: 전체 공개 완료";
            Debug.Log($"[MissionDebugOverlay] {lastAction}");
        }
    }

    private bool TryGetManager()
    {
        if (manager == null)
            manager = FindFirstObjectByType<MissionManager>();

        return manager != null && manager.Object != null && manager.Object.IsValid && manager.Client != null;
    }

    private void OnGUI()
    {
        if (style == null)
        {
            style = new GUIStyle(GUI.skin.label)
            {
                richText = true,
                fontSize = 14,
                wordWrap = true,
            };
            style.normal.textColor = Color.white;
        }

        string body = TryGetManager()
            ? BuildBody(manager.Client, (float)manager.Runner.SimulationTime)
            : "MissionManager 대기 중...";

        GUIContent content = new GUIContent(body);
        float height = style.CalcHeight(content, panelWidth - 20f) + 20f;
        Rect panel = new Rect(10f, 10f, panelWidth, height);

        GUI.Box(panel, GUIContent.none);
        GUI.Box(panel, GUIContent.none);   // 두 번 그려서 배경을 더 진하게
        GUI.Label(new Rect(panel.x + 10f, panel.y + 10f, panel.width - 20f, panel.height - 20f), content, style);
    }

    private string BuildBody(MissionClientState client, float now)
    {
        builder.Clear();

        builder.AppendLine($"<b>시민 전체 진행도</b>  {client.CitizenProgress01 * 100f:0}%  ({client.CitizenProgressCurrent}/{client.CitizenProgressRequired})");
        builder.AppendLine($"단체 미션 전체 완료: {YesNo(client.TeamCompleted)}    탈출: {(client.EscapeUnlocked ? "<color=#7CFC7C>해금</color>" : "잠김")}");

        builder.AppendLine();
        builder.AppendLine(client.IsTeamParticipant ? "<b>[단체 미션]</b>" : "<b>[단체 미션]</b> (나는 참여 대상 아님)");
        AppendList(client, null, now);

        builder.AppendLine("<b>[개인 미션]</b>");
        AppendList(client, MissionCategory.Personal, now);

        builder.AppendLine("<b>[개인 행동 목표]</b>");
        AppendList(client, MissionCategory.ActionGoal, now);

        if (HasCategory(client, MissionCategory.Killer))
        {
            builder.AppendLine("<b>[살인마 미션]</b>");
            AppendList(client, MissionCategory.Killer, now);
        }

        if (client.Revealed.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("<b>[게임 종료 · 전체 공개]</b>");

            foreach (ObjectiveView view in client.Revealed)
                builder.AppendLine($"  {view.Owner}  {Format(view, now)}");
        }

        builder.AppendLine();
        builder.AppendLine($"<color=#AAAAAA>{finalizeKey}: 내 행동 확정(탈출·사망 흉내)   {revealKey}: 전체 공개</color>");

        if (!string.IsNullOrEmpty(lastAction))
            builder.AppendLine($"<color=#FFD37C>{lastAction}</color>");

        return builder.ToString();
    }

    /// <summary>category 가 null 이면 단체 미션 목록, 아니면 내 목록 중 그 카테고리만.</summary>
    private void AppendList(MissionClientState client, MissionCategory? category, float now)
    {
        int count = 0;

        if (category == null)
        {
            foreach (ObjectiveView view in client.Team)
            {
                builder.AppendLine($"  {Format(view, now)}");
                count++;
            }
        }
        else
        {
            foreach (ObjectiveView view in client.Mine)
            {
                if (view.Definition == null || view.Definition.Category != category.Value)
                    continue;

                builder.AppendLine($"  {Format(view, now)}");
                count++;
            }
        }

        if (count == 0)
            builder.AppendLine("  (없음)");
    }

    private static bool HasCategory(MissionClientState client, MissionCategory category)
    {
        foreach (ObjectiveView view in client.Mine)
        {
            if (view.Definition != null && view.Definition.Category == category)
                return true;
        }

        return false;
    }

    private static string Format(ObjectiveView view, float now)
    {
        string title = view.Definition != null
            ? $"{view.Definition.Id} {view.Definition.DisplayName}"
            : "(알 수 없는 미션)";

        bool isAvoid = view.Definition != null && view.Definition.Kind == ObjectiveKind.Avoid;

        string progress = isAvoid
            ? (view.Status == ObjectiveStatus.InProgress ? "위반 없음 · 종료 시 판정" : string.Empty)
            : $"{view.Progress}/{view.Required}";

        string status = view.Status switch
        {
            ObjectiveStatus.Completed => "<color=#7CFC7C>완료</color>",
            ObjectiveStatus.Failed => "<color=#FF7C7C>실패</color>",
            _ => "진행 중",
        };

        string timer = view.Deadline > 0f && view.Status == ObjectiveStatus.InProgress
            ? $"   <color=#FFB35C>남은 시간 {FormatTime(view.Deadline - now)}</color>"
            : string.Empty;

        return $"{title}   {progress}   {status}{timer}";
    }

    private static string FormatTime(float seconds)
    {
        int total = Mathf.Max(0, Mathf.CeilToInt(seconds));
        return $"{total / 60}:{total % 60:00}";
    }

    private static string YesNo(bool value)
    {
        return value ? "<color=#7CFC7C>예</color>" : "아니오";
    }
}
