using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class SessionDisconnectUIComponent : MonoBehaviour
{
    [SerializeField] private GameObject panel; //연결 종료 안내 패널
    [SerializeField] private TMP_Text messageText; //종료 사유
    [SerializeField] private Button lobbyButton; //로비 이동 버튼

    private NetworkBootstrap bootstrap; //세션 종료 진입점
    private bool isShowing; //현재 안내창 표시 여부
    internal static bool IsOpen { get; private set; } //플레이어 입력 차단 여부

    private void Awake() //안내창과 버튼 초기화
    {
        panel.SetActive(false);
        lobbyButton.onClick.AddListener(returnToLobby);
    }

    internal void initialize(NetworkBootstrap networkBootstrap) //세션 진입점 연결
    {
        bootstrap = networkBootstrap;
    }

    internal void show(string message) //종료 안내 표시
    {
        messageText.text = message;
        panel.SetActive(true);
        isShowing = true;
        IsOpen = true;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    private void returnToLobby() //버튼 클릭을 세션 종료로 전달
    {
        if (bootstrap != null)
            bootstrap.LeaveRoom();
    }

    private void OnDestroy() //버튼 구독과 입력 차단 해제
    {
        if (lobbyButton != null)
            lobbyButton.onClick.RemoveListener(returnToLobby);
        if (isShowing)
            IsOpen = false;
    }
}
