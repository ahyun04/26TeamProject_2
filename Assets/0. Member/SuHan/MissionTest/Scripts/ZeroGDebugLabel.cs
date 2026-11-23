using TrustNoOne.Gimmicks;
using UnityEngine;

/// <summary>
/// [테스트 전용] 무중력 테스트 글자 — 화면 왼쪽 위에 지금 속력 · 멈춤 · 닿은 면 · 설정 에셋 이름.
/// [역할] 본인 PC 의 무중력 이동 부품(ZeroGMovement.Local)을 읽어 IMGUI 로 그린다. 테스트 씬(ZeroGTest)에만 있다 — 메뉴가 놓는다.
/// [이유] 관성 · 최대 속도 · 벽 충돌 · 멈춤을 눈으로 숫자와 함께 확인하려고 (작업 규칙 "눈에 보이는 피드백").
///  오른쪽 위는 테스트 시작 도구의 배정 패널이 쓰므로 왼쪽 위 (이 씬에는 미션 디버그 패널을 두지 않는다).
/// [근거] 17a 명세 ZG21 (위치는 계획에서 왼쪽 위로 — 계획 보충 2)
/// </summary>
public class ZeroGDebugLabel : MonoBehaviour
{
    [SerializeField] private float width = 320f;

    private GUIStyle style;

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
        GUIContent content = new GUIContent(
            $"무중력 {local.Speed:0.00} m/s\n" +
            $"멈춤: {(local.Halted ? "멈춤" : "아님")} · 닿음: {local.ContactLabel}\n" +
            $"설정: {settingsName}");

        float height = style.CalcHeight(content, width - 20f) + 20f;
        Rect panel = new Rect(10f, 10f, width, height);

        GUI.Box(panel, GUIContent.none);
        GUI.Box(panel, GUIContent.none);   // 두 번 그려서 배경을 더 진하게 (다른 테스트 패널과 같은 방식)
        GUI.Label(new Rect(panel.x + 10f, panel.y + 10f, panel.width - 20f, panel.height - 20f), content, style);
    }
}
