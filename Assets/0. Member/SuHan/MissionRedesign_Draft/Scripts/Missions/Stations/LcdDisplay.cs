using TMPro;
using UnityEngine;

namespace TrustNoOne.Missions
{
    /// <summary>
    /// [역할] 장치 화면(검은 판) 위 LCD 글자. 장치가 매 Render 에 무엇을 띄울지 알려 준다.
    ///  쓰는 곳: 코드 자판 (3a 명세 C13 — READY / 점등 중 빈 화면 / "3 8 _ _" / ERROR / SUCCESS),
    ///          생명 유지 장치 본체 (3b 명세 LS16 — O2 0/3 / CHECK 5 / WARNING 5 / DAMAGED / SUCCESS)
    /// [색] 우주 시설 LCD 느낌으로 검은 화면 위에 녹색 글자, 잘못됐을 때만 빨간 글자.
    /// [글꼴] 지금은 TextMeshPro 기본 글꼴(LiberationSans). 도트 글꼴이 생기면 글자 오브젝트(Display)의 TextMeshPro 글꼴만 바꾸면 된다.
    /// [성능] 글자 · 색이 바뀔 때만 TextMeshPro 에 넣는다. 같은 값이면 아무것도 하지 않으므로 매 Render 에 불러도 메시를 다시 만들지 않는다.
    /// [이름] 3a 에서 CodeDisplay 로 만들었다가 3b 에서 함께 쓰려고 바꿨다 (파일 · 메타를 같이 바꿔 프리팹 연결 유지).
    /// </summary>
    public class LcdDisplay : MonoBehaviour
    {
        [SerializeField] private TMP_Text text;

        [Tooltip("평소 글자색 (녹색 LCD)")]
        [SerializeField] private Color normalColor = new Color(0.35f, 1f, 0.45f);

        [Tooltip("잘못됐을 때 글자색")]
        [SerializeField] private Color errorColor = new Color(1f, 0.25f, 0.2f);

        private string shownMessage;
        private bool shownError;

        /// <param name="message">띄울 글자. 빈 문자열이면 빈 화면</param>
        /// <param name="error">true 면 빨간 글자</param>
        public void Show(string message, bool error = false)
        {
            if (text == null || (shownMessage == message && shownError == error))
                return;

            shownMessage = message;
            shownError = error;
            text.text = message;
            text.color = error ? errorColor : normalColor;
        }
    }
}
