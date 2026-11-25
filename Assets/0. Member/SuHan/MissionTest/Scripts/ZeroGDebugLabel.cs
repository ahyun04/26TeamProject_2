using TrustNoOne.Gimmicks;
using UnityEngine;

/// <summary>
/// [테스트 전용] 무중력 테스트 글자 — 화면 왼쪽 위에 지금 속력 · 멈춤 · 닿은 면 · 설정 에셋 이름.
/// [역할] 본인 PC 의 무중력 이동 부품(ZeroGMovement.Local)을 읽어 IMGUI 로 그린다. 테스트 씬(ZeroGTest)에만 있다 — 메뉴가 놓는다.
/// [이유] 관성 · 최대 속도 · 벽 충돌 · 멈춤을 눈으로 숫자와 함께 확인하려고 (작업 규칙 "눈에 보이는 피드백").
///  오른쪽 위는 테스트 시작 도구의 배정 패널이 쓰므로 왼쪽 위 (이 씬에는 미션 디버그 패널을 두지 않는다).
/// [근거] 17a 명세 ZG21 (위치는 계획에서 왼쪽 위로 — 계획 보충 2)
///  (테스트 후 추가) 내 플레이어가 있는 동안 장면 카메라를 끈다 (ZG38). 프레임 끊김 진단 로그(ZG39)는 원인을 찾아 지움 (ZG42)
///  (17b) 사건 줄(단계 · 남은 초)과 F2 = 무중력 바로 시작 (호스트 — 17b 명세 ZG63). 속도 · 멈춤 · 닿음 줄은 무중력 이동 중에만
///  (17c) 무중력 이동 중에 뒤집힘 각도 · 발 높이 줄 (1 m 떠오름 · 180° 확인 — 17c 명세 ZG77)
///  (17d) 발밑 줄 (면까지 거리 · 닿은 시간 / 3초 · 밀기 속도) · 닿음은 내 화면 기준 (17d 명세 ZG86 · ZG87)
///  (17d 테스트 후) 뒤집힘 줄에 떠오를 높이 · 사건 줄 "되돌기 (중력)" (17d 명세 9장 ZG96)
/// </summary>
public class ZeroGDebugLabel : MonoBehaviour
{
    [SerializeField] private float width = 320f;

    [Tooltip("시작 화면용 장면 카메라 — 내 플레이어가 생기면 끈다 (메뉴가 채운다)")]
    [SerializeField] private Camera sceneCamera;

    [Tooltip("(17b) 무중력 바로 시작 키 — 호스트만 (ZG63). 비어 있던 F2")]
    [SerializeField] private KeyCode startNowKey = KeyCode.F2;

    private GUIStyle style;
    private string lastMessage;

    private void Update()
    {
        if (Input.GetKeyDown(startNowKey))
            StartNow();

        bool hasPlayer = ZeroGMovement.Local != null;

        if (sceneCamera != null && sceneCamera.enabled == hasPlayer)
            sceneCamera.enabled = !hasPlayer;
    }

    private void OnGUI()
    {
        ZeroGMovement local = ZeroGMovement.Local;

        if (local == null)
            return;

        if (style == null)
        {
            style = new GUIStyle(GUI.skin.label) { fontSize = 15, wordWrap = true };
            style.normal.textColor = Color.white;
        }

        string settingsName = local.Settings != null ? local.Settings.name : "없음 (기본값)";
        string moveLines = local.ZeroGActive
            ? $"무중력 {local.Speed:0.00} m/s\n멈춤: {(local.Halted ? "멈춤" : "아님")} · 닿음: {local.ContactLabel}\n" +
              $"뒤집힘 {local.RenderRollDegrees():0}° · 발 높이 {local.transform.position.y:0.00} m · 떠오를 높이 {local.RiseHeightUsed:0.00} m\n" +
              $"{DescribeFloor(local)}\n"
            : "평소 이동 (팀원 이동 부품)\n";
        GUIContent content = new GUIContent(
            moveLines +
            $"설정: {settingsName}\n" +
            $"{DescribeEvent()}\n" +
            $"{startNowKey} = 무중력 바로 시작 (호스트)" +
            (string.IsNullOrEmpty(lastMessage) ? "" : $"\n{lastMessage}"));

        float height = style.CalcHeight(content, width - 20f) + 20f;
        Rect panel = new Rect(10f, 10f, width, height);

        GUI.Box(panel, GUIContent.none);
        GUI.Box(panel, GUIContent.none);   // 두 번 그려서 배경을 더 진하게 (다른 테스트 패널과 같은 방식)
        GUI.Label(new Rect(panel.x + 10f, panel.y + 10f, panel.width - 20f, panel.height - 20f), content, style);
    }

    /// <summary>(17b) F2: 사건 관리자에 "지금 경고 시작"을 부탁한다 (호스트만 — 손님은 안내만)</summary>
    private void StartNow()
    {
        ZeroGEventManager manager = ZeroGEventManager.Instance;

        if (manager == null || manager.Object == null || !manager.Object.IsValid)
        {
            lastMessage = "씬에 무중력 사건 관리자가 없음 (메뉴 SuHan/Build Zero-G Test Scene 다시)";
            Debug.LogWarning($"[ZeroGDebugLabel] {lastMessage}");
            return;
        }

        manager.StartNowForTest(out lastMessage);

        if (!manager.HasStateAuthority)
            Debug.Log($"[ZeroGDebugLabel] {lastMessage}");
    }

    /// <summary>(17d) 발밑 줄: 발밑 면까지 거리 · 닿은 시간 / 3초 · 밀기 속도 (ZG87)</summary>
    private static string DescribeFloor(ZeroGMovement local)
    {
        string distance = float.IsInfinity(local.FloorDistance)
            ? "없음 (1 m 밖 · 무중력 단계만 · Ctrl · 멈춤이면 안 잼)"
            : $"{local.FloorDistance:0.00} m";
        return $"발밑 면 {distance} · 닿음 {local.FloorTouchSeconds:0.0}/{ZeroGFloat.TouchSeconds:0}초 · 밀기 {local.FloorPushSpeed:0.00} m/s";
    }

    /// <summary>(17b) 사건 줄: 단계 · 남은 초</summary>
    private static string DescribeEvent()
    {
        ZeroGEventManager manager = ZeroGEventManager.Instance;

        if (manager == null || manager.Object == null || !manager.Object.IsValid)
            return "사건: 관리자 없음";

        ZeroGState state = manager.Evaluate();

        switch (state.Phase)
        {
            case ZeroGPhase.Waiting: return $"사건: 대기 · 경고까지 {state.SecondsToWarning:0}초";
            case ZeroGPhase.Warning: return $"사건: 경고 · {state.Countdown}";
            case ZeroGPhase.Rising: return $"사건: 떠오르기 · 복구까지 {state.SecondsToRestore:0}초";
            case ZeroGPhase.Floating: return $"사건: 무중력 · 복구까지 {state.SecondsToRestore:0}초";
            case ZeroGPhase.Returning: return $"사건: 되돌기 (중력) · 복구까지 {state.SecondsToRestore:0}초";
            case ZeroGPhase.Ended: return "사건: 끝 · 중력 복구 (이번 판 다시 없음)";
            default: return manager.RoundStarted ? "사건: 이번 판 없음" : "사건: 판 시작 전";
        }
    }
}
