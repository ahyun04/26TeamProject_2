using TMPro;
using UnityEngine;

namespace TrustNoOne.Missions
{
    /// <summary>
    /// [역할] 코드 자판 화면(패널의 검은 화면) 위 글자. CodeStation 이 매 Render 에 무엇을 띄울지 알려 준다. 3a 명세 C13.
    ///  대기 READY / 점등 중 빈 화면 / 입력 중 "3 8 _ _" / 실패 ERROR / 완료 SUCCESS
    /// [색] 우주 시설 LCD 느낌으로 검은 화면 위에 녹색 글자, 틀렸을 때만 빨간 글자.
    /// [글꼴] 지금은 TextMeshPro 기본 글꼴(LiberationSans). 도트 글꼴이 생기면 글자 오브젝트(Display)의 TextMeshPro 글꼴만 바꾸면 된다.
    /// [성능] 글자 · 색이 바뀔 때만 TextMeshPro 에 넣는다. 같은 값이면 아무것도 하지 않으므로 매 Render 에 불러도 메시를 다시 만들지 않는다.
    /// </summary>
    public class CodeDisplay : MonoBehaviour
    {
        [SerializeField] private TMP_Text text;

        [Tooltip("평소 글자색 (녹색 LCD)")]
        [SerializeField] private Color normalColor = new Color(0.35f, 1f, 0.45f);

        [Tooltip("틀렸을 때 글자색")]
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
