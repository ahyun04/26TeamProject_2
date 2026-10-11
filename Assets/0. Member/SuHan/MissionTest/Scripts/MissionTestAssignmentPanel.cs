using System.Collections.Generic;
using System.Text;
using Fusion;
using TrustNoOne.Missions;
using UnityEngine;

/// <summary>
/// [테스트 전용] 화면 위 오른쪽 안내 상자: 지금 방식 · 접속 인원 · 방 이름, 그리고 멀티 배정(F5 · F6) · 판 다시 시작(F7).
///
/// [역할]
///  - 호스트: 배정 전엔 키 안내. F5 = 손님 전원 시민 / F6 = 손님 1명 범인 → MissionTestRoles 로 나눠 InitializeMissions(시민, 범인).
///    배정 뒤엔 사람마다 역할(시민 · 범인 · 배정 뒤 입장 = 미션 없음)을 보여 준다
///  - 손님: 배정을 기다림 → 내 역할 (참여면 "시민", 아니면 "시민 아님")
///  - 모두: F7 = 판 다시 시작 (호스트 = 모두 시작 화면으로, 손님 = 혼자 나가기, 혼자 모드 = 처음부터)
/// [이유] 플레이어가 스폰되면 카메라 코드가 커서를 고정하고, 커서를 풀어도 입력 코드가 마우스 움직임을 시점 회전으로 보낸다
///  → 버튼 대신 키 + 안내 문구 (MT4). "다시 배정"이 아니라 판 다시 시작인 이유: 미션 관리자의 초기화는 미션 목록만 비우고
///  장치 상태(잠긴 단체 장치 · 놓인 산소통 · 부품 · 레버)는 되돌리지 않는다 (MT5).
///  손님 쪽에서는 범인과 배정 뒤에 들어온 사람을 구분할 정보가 없다 → 누가 무엇인지는 호스트 화면에 (MT15)
/// [근거] 6a 명세 MT3 · MT4 · MT5 · MT15
/// </summary>
[RequireComponent(typeof(MissionTestBootstrap))]
public class MissionTestAssignmentPanel : MonoBehaviour
{
    [SerializeField] private KeyCode citizenGuestKey = KeyCode.F5;
    [SerializeField] private KeyCode killerGuestKey = KeyCode.F6;
    [SerializeField] private KeyCode restartKey = KeyCode.F7;
    [SerializeField] private float panelWidth = 400f;

    private MissionTestBootstrap bootstrap;
    private MissionManager manager;
    private readonly List<PlayerRef> citizens = new List<PlayerRef>();
    private readonly List<PlayerRef> killers = new List<PlayerRef>();
    private readonly StringBuilder builder = new StringBuilder();
    private string notice;
    private GUIStyle style;

    private void Awake()
    {
        bootstrap = GetComponent<MissionTestBootstrap>();
    }

    private void Update()
    {
        if (!bootstrap.IsRunning)
            return;

        if (Input.GetKeyDown(restartKey))
        {
            Debug.Log($"[MissionTestAssignmentPanel] {restartKey}: 판 다시 시작 ({MissionTestBootstrap.ModeName(bootstrap.Mode)})");
            bootstrap.Restart();
            return;
        }

        // 배정은 멀티 호스트만, 한 판에 한 번 (다시 하려면 판 다시 시작 — MT5)
        if (bootstrap.Mode != GameMode.Host || !TryGetManager() || manager.Initialized)
            return;

        if (Input.GetKeyDown(citizenGuestKey))
            Assign(firstGuestIsKiller: false);
        else if (Input.GetKeyDown(killerGuestKey))
            Assign(firstGuestIsKiller: true);
    }

    private void Assign(bool firstGuestIsKiller)
    {
        if (!manager.IsReady)
        {
            notice = "미션 관리자 준비 중 — 잠시 뒤 다시 누르세요";
            return;
        }

        NetworkRunner runner = bootstrap.Runner;

        if (!MissionTestRoles.Split(runner.LocalPlayer, runner.ActivePlayers, firstGuestIsKiller, citizens, killers))
        {
            notice = "손님이 아직 없습니다 — 다른 창(빌드)에서 [손님]으로 들어온 뒤 누르세요";
            return;
        }

        // 실제 게임의 역할 배정이 부르는 것과 같은 함수 (MT3). 화면 표시용 목록과 섞이지 않게 복사해서 넘긴다
        manager.InitializeMissions(new List<PlayerRef>(citizens), new List<PlayerRef>(killers));
        notice = null;

        Debug.Log($"[MissionTestAssignmentPanel] 미션 배정: 시민 {citizens.Count}명, 범인 {killers.Count}명 " +
                  $"(손님 = {(firstGuestIsKiller ? "1명 범인" : "전원 시민")})");
    }

    private bool TryGetManager()
    {
        if (manager == null)
            manager = FindFirstObjectByType<MissionManager>();

        // 씬에 놓인 미션 관리자는 러너가 등록하기 전엔 네트워크 값을 읽을 수 없다
        return manager != null && manager.Object != null && manager.Object.IsValid;
    }

    // ═════════════════════════════════════════════════════════════
    //  화면 (위 오른쪽 — 왼쪽 위는 디버그 패널)
    // ═════════════════════════════════════════════════════════════

    private void OnGUI()
    {
        if (!bootstrap.IsRunning)
            return;

        if (style == null)
        {
            style = new GUIStyle(GUI.skin.label) { richText = true, fontSize = 14, wordWrap = true };
            style.normal.textColor = Color.white;
        }

        GUIContent content = new GUIContent(BuildText());
        float height = style.CalcHeight(content, panelWidth - 20f) + 20f;
        Rect panel = new Rect(Screen.width - panelWidth - 10f, 10f, panelWidth, height);

        GUI.Box(panel, GUIContent.none);
        GUI.Box(panel, GUIContent.none);   // 두 번 그려서 배경을 더 진하게 (디버그 패널과 같은 방식)
        GUI.Label(new Rect(panel.x + 10f, panel.y + 10f, panel.width - 20f, panel.height - 20f), content, style);
    }

    private string BuildText()
    {
        builder.Clear();
        NetworkRunner runner = bootstrap.Runner;

        switch (bootstrap.Mode)
        {
            case GameMode.Host:
                builder.AppendLine($"<b>호스트</b> · 접속 {CountPlayers(runner)}명 · 방 {MissionTestBootstrap.MultiSessionName}");
                AppendHost(runner);
                break;

            case GameMode.Client:
                builder.AppendLine($"<b>손님</b> {Name(runner.LocalPlayer, runner)} · 접속 {CountPlayers(runner)}명 · 방 {MissionTestBootstrap.MultiSessionName}");
                AppendGuest();
                builder.Append($"[{restartKey}] 나가기 (시작 화면으로)");
                break;

            default:
                builder.AppendLine("<b>혼자</b> (오프라인 · 나 = 시민)");
                builder.Append($"[{restartKey}] 처음부터 다시 (시작 화면으로)");
                break;
        }

        if (!string.IsNullOrEmpty(notice))
            builder.Append($"\n<color=#ffb050>{notice}</color>");

        return builder.ToString();
    }

    private void AppendHost(NetworkRunner runner)
    {
        if (!TryGetManager() || !manager.Initialized)
        {
            builder.AppendLine("나 = 항상 시민");
            builder.AppendLine($"[{citizenGuestKey}] 미션 시작 — 손님 = 시민");
            builder.AppendLine($"[{killerGuestKey}] 미션 시작 — 손님 1명 = 범인");
        }
        else
        {
            builder.AppendLine("배정됨:");

            foreach (PlayerRef player in Sorted(runner.ActivePlayers))
                builder.AppendLine($"  {Name(player, runner)} = {RoleOf(player)}");
        }

        builder.Append($"[{restartKey}] 판 다시 시작 (모두 시작 화면으로)");
    }

    private void AppendGuest()
    {
        if (!TryGetManager() || !manager.Initialized)
            builder.AppendLine("호스트가 미션을 배정하기를 기다리는 중…");
        else if (manager.Client != null && manager.Client.IsTeamParticipant)
            builder.AppendLine("내 역할: 시민");
        else
            // 범인과 배정 뒤에 들어온 사람은 둘 다 "참여 아님 · 개인 미션 없음"이라 손님 쪽에서 구분할 수 없다 (MT15).
            // 배정 직후 참여 알림이 도착하기 전에도 잠깐 이 문구가 보일 수 있다
            builder.AppendLine("내 역할: <color=#ffb050>시민 아님</color> (범인 또는 배정 뒤 입장 — 호스트 화면 참고)");
    }

    private string RoleOf(PlayerRef player)
    {
        if (killers.Contains(player))
            return "<color=#ff7070>범인</color>";

        if (citizens.Contains(player))
            return "시민";

        return "<color=#ffb050>미션 없음 (배정 뒤 입장)</color>";
    }

    private static string Name(PlayerRef player, NetworkRunner runner)
    {
        return player == runner.LocalPlayer ? $"P{player.PlayerId} (나)" : $"P{player.PlayerId}";
    }

    private static int CountPlayers(NetworkRunner runner)
    {
        int count = 0;

        foreach (PlayerRef _ in runner.ActivePlayers)
            count++;

        return count;
    }

    private static List<PlayerRef> Sorted(IEnumerable<PlayerRef> players)
    {
        List<PlayerRef> list = new List<PlayerRef>(players);
        list.Sort((a, b) => a.AsIndex.CompareTo(b.AsIndex));
        return list;
    }
}
