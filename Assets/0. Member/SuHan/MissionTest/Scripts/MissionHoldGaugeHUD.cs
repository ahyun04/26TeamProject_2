using TrustNoOne.Missions;
using UnityEngine;

/// <summary>
/// [테스트 전용] F 홀드형 미션 오브젝트(HoldStation)를 누르고 있는 동안 화면 아래 가운데에 진행 게이지(채워지는 바 + %)를 띄운다.
///  에셋 없이 IMGUI 로 그린다. 옛 발전기/안테나 미션의 게이지(채워지는 바 + 퍼센트)와 같은 형태.
///
/// [읽는 값] HoldStation 의 공개 값만 쓴다.
///  - IsOperatedByLocalPlayer : 지금 내가 이 오브젝트를 쓰고 있는가 (호스트가 허용한 경우에만 true)
///  - HoldProgress01          : 홀드 진행률 0~1 (호스트가 계산해서 동기화한 값)
///  - HoldReached             : 목표 시간 도달 (안테나 "준비 완료" → 문구를 "고정 버튼을 클릭하세요"로)
///  그래서 게이지가 떴다 = 호스트가 상호작용을 허용했다는 뜻이다. F를 눌러도 게이지가 안 뜨면 거부된 것이다.
/// [발전기처럼 홀드가 아닌 스테이션] 자기 월드 UI(발전기 화면 게이지)가 진행을 보여 주므로 여기서 다루지 않는다.
/// </summary>
public class MissionHoldGaugeHUD : MonoBehaviour
{
    [SerializeField] private float width = 420f;
    [SerializeField] private float height = 26f;
    [SerializeField] private float bottomMargin = 140f;
    [SerializeField] private Color fillColor = new Color(0.35f, 0.8f, 1f, 0.95f);

    private HoldStation[] stations = new HoldStation[0];
    private float nextSearchTime;
    private GUIStyle textStyle;

    private void OnGUI()
    {
        HoldStation active = FindActive();

        if (active == null)
            return;

        float progress = active.HoldProgress01;
        TestCubeLabel label = active.GetComponent<TestCubeLabel>();
        string title = label != null ? label.Title : active.name.Replace("(Clone)", string.Empty).Trim();

        // 안테나: 목표 회전량에 도달하면 F 를 떼도 유지되고, 고정 버튼을 눌러야 완료 (2a 명세 2-4)
        string hint = active.HoldReached ? "고정 버튼을 클릭하세요" : "F 유지";

        if (textStyle == null)
        {
            textStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 16,
                fontStyle = FontStyle.Bold,
            };
            textStyle.normal.textColor = Color.white;
        }

        Rect bar = new Rect((Screen.width - width) * 0.5f, Screen.height - bottomMargin, width, height);
        Rect caption = new Rect(bar.x, bar.y - 26f, bar.width, 24f);

        GUI.Label(caption, $"{title}  —  {hint}", textStyle);

        Color previous = GUI.color;

        GUI.color = new Color(0f, 0f, 0f, 0.7f);
        GUI.DrawTexture(bar, Texture2D.whiteTexture);

        GUI.color = fillColor;
        GUI.DrawTexture(new Rect(bar.x + 2f, bar.y + 2f, (bar.width - 4f) * progress, bar.height - 4f), Texture2D.whiteTexture);

        GUI.color = previous;
        GUI.Label(bar, $"{Mathf.RoundToInt(progress * 100f)}%", textStyle);
    }

    /// <summary>내가 지금 사용 중인 홀드형 오브젝트. 목록은 1초마다 다시 찾는다 (스폰이 늦게 끝날 수 있어서).</summary>
    private HoldStation FindActive()
    {
        if (Time.unscaledTime >= nextSearchTime)
        {
            stations = FindObjectsByType<HoldStation>(FindObjectsSortMode.None);
            nextSearchTime = Time.unscaledTime + 1f;
        }

        foreach (HoldStation station in stations)
        {
            if (station == null || station.Object == null || !station.Object.IsValid)
                continue;

            if (!station.Completed && station.IsOperatedByLocalPlayer)
                return station;
        }

        return null;
    }
}
