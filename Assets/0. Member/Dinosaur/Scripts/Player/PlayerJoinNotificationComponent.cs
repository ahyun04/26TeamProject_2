using System.Collections.Generic;
using Fusion;
using LockdownProtocol.Lobby;
using LockdownProtocol.Networking;
using TMPro;
using UnityEngine;

public class PlayerJoinNotificationComponent : MonoBehaviour
{
    [SerializeField] private CanvasGroup notification; //화면 상단 입장 알림
    [SerializeField] private TMP_Text messageText; //닉네임을 포함한 알림 문구
    [SerializeField, Min(0.1f)] private float displaySeconds = 3f; //알림 표시 시간
    [SerializeField, Min(0.01f)] private float fadeSeconds = 0.4f; //알림이 사라지는 시간

    private readonly HashSet<PlayerRef> knownPlayers = new(); //현재 세션에서 이미 확인한 참가자
    private readonly Queue<PlayerRef> pendingPlayers = new(); //닉네임 준비를 기다리는 새 참가자
    private NetworkRunner session; //알림을 표시할 현재 세션
    private float hideTime; //현재 알림 종료 시각
    private float pendingDeadline; //퇴장하거나 생성되지 않은 참가자 대기 제한

    private void OnEnable() //기존 세션 입장 콜백만 구독
    {
        notification.alpha = 0f;
        notification.blocksRaycasts = false;
        notification.interactable = false;
        NetworkBootstrap.OnPlayerJoinedEvent += handleJoined;
        NetworkBootstrap.OnPlayerLeftEvent += handleLeft;
    }

    private void handleJoined(NetworkRunner runner, PlayerRef player) //새 접속만 대기열에 추가하고 최초 목록과 본인은 제외
    {
        if (session != runner)
        {
            clearSession();
            session = runner;
            foreach (PlayerRef active in runner.ActivePlayers) knownPlayers.Add(active);
            return;
        }
        if (!knownPlayers.Add(player) || player == runner.LocalPlayer) return;
        pendingPlayers.Enqueue(player);
        if (pendingPlayers.Count == 1) pendingDeadline = Time.unscaledTime + 8f;
    }

    private void handleLeft(NetworkRunner runner, PlayerRef player) //같은 플레이어가 재입장하면 다시 알림 허용
    {
        if (session == runner) knownPlayers.Remove(player);
    }

    private void Update() //플레이어 생성과 닉네임 수신 뒤 순서대로 알림 표시
    {
        if (session == null || !session.IsRunning)
        {
            if (!ReferenceEquals(session, null)) clearSession();
            return;
        }
        float remaining = hideTime - Time.unscaledTime; //현재 표시 남은 시간
        notification.alpha = Mathf.Clamp01(remaining / fadeSeconds);
        if (remaining > 0f || pendingPlayers.Count == 0) return;
        PlayerRef player = pendingPlayers.Peek(); //다음 입장 알림 대상
        NetworkObject actor = session.GetPlayerObject(player); //로비와 대기실의 기존 플레이어 오브젝트
        LobbyPlayerController identity = actor != null && actor.IsValid ? actor.GetComponent<LobbyPlayerController>() : null; //확정 닉네임 제공자
        if (!knownPlayers.Contains(player) || Time.unscaledTime >= pendingDeadline)
        {
            dequeue();
            return;
        }
        if (identity == null || identity.Nickname.Length == 0) return;
        messageText.text = $"{identity.Nickname}님이 입장했습니다";
        hideTime = Time.unscaledTime + displaySeconds;
        notification.alpha = 1f;
        dequeue();
    }

    private void dequeue() //다음 참가자의 닉네임 대기 시간 갱신
    {
        pendingPlayers.Dequeue();
        pendingDeadline = Time.unscaledTime + 8f;
    }

    private void clearSession() //방 종료와 다른 방 연결에서 이전 알림 정리
    {
        session = null;
        knownPlayers.Clear();
        pendingPlayers.Clear();
        hideTime = 0f;
        notification.alpha = 0f;
    }

    private void OnDisable() //중복 구독과 종료 후 알림 방지
    {
        NetworkBootstrap.OnPlayerJoinedEvent -= handleJoined;
        NetworkBootstrap.OnPlayerLeftEvent -= handleLeft;
        clearSession();
    }
}
