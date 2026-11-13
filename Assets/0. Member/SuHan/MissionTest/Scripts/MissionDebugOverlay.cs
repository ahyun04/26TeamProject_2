using System.Text;
using TrustNoOne.Gimmicks;
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
///  F8  : (테스트) 방화문을 뺀 단체 미션 즉시 완료 — 방화문 해금을 빨리 보려고 (3d 명세 FD17). 장치 모습은 바뀌지 않는다 (미션 상태만)
///  F4  : (테스트) 살인마 암전을 "지금부터 3초 뒤 시작"으로 다시 잡기 — 길이도 다시 뽑는다 (16a 명세 KB21)
///
/// [살인마 암전 한 줄 — 16a KB19] 대기(시작까지 · 길이 · 살인마 수) / 암전 중(남은 초 · 어두움) / 끝 / 이번 판 없음 + 내 화면 적용 여부.
///  씬에 KillerBlackoutManager 가 없으면 줄을 그리지 않는다.
/// [암전 화면 한 줄 — 16b KB40] 그 아래 회색으로: 팔(팀원 팔 · 복사본 · 없음) · 지도(막음 · 씬에 없음 · 대체 · 평소) · 조준점 아래 안내 글자.
/// </summary>
public class MissionDebugOverlay : MonoBehaviour
{
    [SerializeField] private KeyCode finalizeKey = KeyCode.F9;
    [SerializeField] private KeyCode revealKey = KeyCode.F10;
    [SerializeField] private KeyCode completeOtherTeamKey = KeyCode.F8;
    [SerializeField] private KeyCode blackoutRestartKey = KeyCode.F4;
    [SerializeField] private float panelWidth = 540f;

    private MissionManager manager;
    private KillerBlackoutManager blackout;
    private KillerBlackoutView blackoutView;
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

        if (Input.GetKeyDown(completeOtherTeamKey))
        {
            int completed = CompleteTeamMissionsExceptFireDoor();
            lastAction = $"{completeOtherTeamKey}: (테스트) 방화문 제외 단체 미션 {completed}개 완료";
            Debug.Log($"[MissionDebugOverlay] {lastAction}");
        }

        if (Input.GetKeyDown(blackoutRestartKey) && TryGetBlackout())
        {
            bool planned = blackout.RestartForTest();
            lastAction = planned
                ? $"{blackoutRestartKey}: (테스트) 살인마 암전 3초 뒤 다시 — 길이 {blackout.Duration:0.0}초"
                : $"{blackoutRestartKey}: (테스트) 살인마 암전 다시 잡기 실패 (판 시작 전이거나 길이 0)";
            Debug.Log($"[MissionDebugOverlay] {lastAction}");
        }
    }

    private bool TryGetBlackout()
    {
        if (blackout == null)
        {
            blackout = FindFirstObjectByType<KillerBlackoutManager>();
            blackoutView = blackout != null ? blackout.GetComponent<KillerBlackoutView>() : null;
        }

        return blackout != null && blackout.Object != null && blackout.Object.IsValid;
    }

    /// <summary>
    /// (테스트, 3d 명세 FD17) 방화문을 뺀 진행 중 단체 미션마다 남은 수만큼 완료 이벤트를 보낸다 — 내 플레이어 이름으로.
    /// 미션 상태만 바뀌고 장치(발전기 · 장비 등) 모습은 그대로다.
    /// </summary>
    private int CompleteTeamMissionsExceptFireDoor()
    {
        int count = 0;
        Fusion.PlayerRef actor = manager.Runner.LocalPlayer;

        // 복사해서 돈다 — Publish 하는 동안 공개 목록이 바뀔 수 있다
        System.Collections.Generic.List<ObjectiveView> views = new System.Collections.Generic.List<ObjectiveView>(manager.Client.Team);

        foreach (ObjectiveView view in views)
        {
            if (view.Definition == null || view.Definition.Trigger == MissionEventType.FireDoorOpened || view.Status != ObjectiveStatus.InProgress)
                continue;

            int remaining = Mathf.Max(1, view.Required - view.Progress);
            manager.Publish(new MissionEvent(view.Definition.Trigger, actor, 0, remaining));
            count++;
        }

        return count;
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
        AppendBlackout();

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
        builder.AppendLine($"<color=#AAAAAA>{finalizeKey}: 내 행동 확정(탈출·사망 흉내)   {revealKey}: 전체 공개   {completeOtherTeamKey}: 방화문 빼고 단체 미션 완료(테스트)   {blackoutRestartKey}: 살인마 암전 3초 뒤(테스트)</color>");

        if (!string.IsNullOrEmpty(lastAction))
            builder.AppendLine($"<color=#FFD37C>{lastAction}</color>");

        return builder.ToString();
    }

    /// <summary>
    /// 16a KB19: 살인마 암전 한 줄. 예) "암전: 대기 — 시작까지 0:07 · 길이 12.4초 · 살인마 1명   내 화면: 적용"
    ///  / "암전 중 — 9.8초 남음 (어두움 1.00)" / "암전: 끝" / "암전: 이번 판 없음" / "암전: 판 시작 전"
    /// </summary>
    private void AppendBlackout()
    {
        if (!TryGetBlackout())
            return;

        string status;

        if (!blackout.RoundStarted)
        {
            status = "암전: 판 시작 전";
        }
        else
        {
            BlackoutState state = blackout.Evaluate();

            switch (state.Phase)
            {
                case BlackoutPhase.Waiting:
                    status = $"암전: 대기 — 시작까지 {FormatTime(state.SecondsToStart)} · 길이 {blackout.Duration:0.0}초 · 살인마 {blackout.KillerCount}명";
                    break;
                case BlackoutPhase.FadingIn:
                case BlackoutPhase.Dark:
                    status = $"<color=#FF9C5C>암전 중 — {state.SecondsLeft:0.0}초 남음 (어두움 {state.Darkness:0.00})</color>";
                    break;
                case BlackoutPhase.Ended:
                    status = "암전: 끝";
                    break;
                default:
                    status = "암전: 이번 판 없음";
                    break;
            }
        }

        string mine = blackoutView == null
            ? string.Empty
            : blackoutView.AppliesToMe ? "   내 화면: 적용" : $"   내 화면: 미적용({blackoutView.NotAppliedReason})";

        builder.AppendLine($"{status}{mine}");

        if (blackoutView != null)
            builder.AppendLine($"<color=#AAAAAA>암전 화면: {blackoutView.ScreenStatus}</color>");
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
