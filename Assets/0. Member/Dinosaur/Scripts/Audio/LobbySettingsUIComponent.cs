using System;
using System.Collections.Generic;
using Fusion;
using LockdownProtocol.Lobby;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class LobbySettingsUIComponent : MonoBehaviour
{
    [SerializeField] private Transform playerListContainer; //스크롤 가능한 참가자 목록
    [SerializeField] private PlayerListEntryUI playerEntryPrefab; //설정창 전용 참가자 행
    [SerializeField] private TMP_Text playerCountText; //현재 인원과 확정 정원
    [SerializeField] private TMP_Text currentCountText; //현재 참가 인원
    [SerializeField] private TMP_Text readyCountText; //준비 완료 인원
    [SerializeField] private TMP_Text myReadyText; //본인의 확정 준비 상태
    [SerializeField] private Image myReadyBackground; //본인 준비 상태의 배경
    [SerializeField] private TMP_InputField maximumInput; //적용 전 정원 입력
    [SerializeField] private Button decreaseButton; //정원 감소
    [SerializeField] private Button increaseButton; //정원 증가
    [SerializeField] private Button readyButton; //본인의 준비 전환
    [SerializeField] private TMP_Text readyButtonText; //준비 또는 준비 취소
    [SerializeField] private Button startButton; //방장 게임 시작 요청
    [SerializeField] private TMP_Text startButtonText; //시작 또는 카운트다운 표시
    [SerializeField] private TMP_Text startHintText; //실제 시작 조건 안내
    [SerializeField] private TMP_Text feedbackText; //서버 거절과 미참가 안내
    [SerializeField] private TMP_Text roomNameText; //기존 방 이름 표시
    [SerializeField] private TMP_Text inviteCodeText; //기존 초대 코드 표시
    [SerializeField] private Button copyInviteButton; //초대 코드 복사
    [SerializeField] private Button inviteButton; //기존 친구 초대창 연결
    [SerializeField] private Button leaveButton; //기존 나가기 확인창 연결
    [SerializeField, Min(1f)] private float responseTimeout = 8f; //인원 변경 응답 대기 한도

    private readonly List<LobbyPlayerController> players = new(); //같은 Runner의 유효 참가자
    private readonly List<PlayerRef> gamePlayers = new(); //사망·관전 여부와 관계없이 접속 중인 참가자
    private readonly List<PlayerListEntryUI> entries = new(); //재사용할 목록 행
    private NetworkBootstrap bootstrap; //씬 전환 후 유지되는 세션 정보 진입점
    private RoomManager room; //기존 방 상태 진입점
    private LobbyGameStartManager startManager; //기존 시작 조건 진입점
    private LobbyPlayerController localPlayer; //본인의 준비 요청 대상
    private Action<bool, string> applyCompleted; //설정창의 적용 완료 알림
    private bool editing; //설정창 편집 중 여부
    private bool dirty; //확정 정원과 다른 임시 입력 여부
    private int draftMaximum; //적용 전 임시 정원
    private float nextRefresh; //목록 갱신 간격
    private float responseDeadline; //서버 응답 대기 종료 시각
    private float feedbackDeadline; //거절 안내 종료 시각

    internal bool hasPendingChange => dirty && room != null; //공용 적용 버튼이 처리할 방 설정

    private void Awake() //UI 입력을 기존 기능에 연결
    {
        decreaseButton.onClick.AddListener(decreaseMaximum);
        increaseButton.onClick.AddListener(increaseMaximum);
        readyButton.onClick.AddListener(toggleReady);
        startButton.onClick.AddListener(startGame);
        maximumInput.onEndEdit.AddListener(setMaximum);
        copyInviteButton.onClick.AddListener(copyInviteCode);
        inviteButton.onClick.AddListener(openInvites);
        leaveButton.onClick.AddListener(openLeaveConfirmation);
    }

    internal void beginEditing() //새 편집에서 서버의 확정 정원 읽기
    {
        editing = true;
        dirty = false;
        bindRoom();
        draftMaximum = room != null ? room.MaxPlayerCount : RoomManager.AbsoluteMinPlayers;
        feedbackDeadline = 0f;
        refresh();
    }

    internal void endEditing() //취소·닫기·씬 전환에서 임시 값과 구독 해제
    {
        editing = false;
        dirty = false;
        applyCompleted = null;
        unbindRoom();
    }

    private void Update() //열린 설정의 복제 상태와 응답 제한 확인
    {
        if (!editing) return;
        if (applyCompleted != null && Time.unscaledTime >= responseDeadline)
            finishApply(false, "응답을 받지 못했습니다. 방 인원을 확인한 뒤 다시 적용해 주세요.");
        if (Time.unscaledTime < nextRefresh) return;
        nextRefresh = Time.unscaledTime + 0.15f;
        bindRoom();
        refresh();
    }

    private void bindRoom() //씬·Runner가 바뀌면 이전 방 구독을 정리
    {
        RoomManager current = RoomManager.Instance; //현재 유효 방
        if (current != null && (current.Object == null || !current.Object.IsValid)) current = null;
        if (current == room && (current != null || ReferenceEquals(room, null))) return;
        if (applyCompleted != null) finishApply(false, "방 연결이 변경되었습니다.");
        unbindRoom();
        room = current;
        dirty = false;
        if (room == null) return;
        draftMaximum = room.MaxPlayerCount;
        room.MaxPlayerChanged += handleMaximumChanged;
        room.MaxPlayerChangeRejected += handleMaximumRejected;
        foreach (LobbyGameStartManager candidate in FindObjectsByType<LobbyGameStartManager>(FindObjectsSortMode.None))
        {
            if (candidate.Object == null || !candidate.Object.IsValid || candidate.Runner != room.Runner) continue;
            startManager = candidate;
            startManager.StartFailed += handleStartFailed;
            break;
        }
    }

    private void unbindRoom() //파괴된 Unity 참조도 이벤트 구독 해제
    {
        if (!ReferenceEquals(room, null))
        {
            room.MaxPlayerChanged -= handleMaximumChanged;
            room.MaxPlayerChangeRejected -= handleMaximumRejected;
        }
        if (!ReferenceEquals(startManager, null)) startManager.StartFailed -= handleStartFailed;
        room = null;
        startManager = null;
        localPlayer = null;
    }

    private bool canUseRoom() //게임 중·유효하지 않은 방의 변경 차단
    {
        return editing && room != null && room.Object != null && room.Object.IsValid &&
            SceneManager.GetActiveScene().name != "GamePlay" && room.CurrentRoomState == RoomManager.RoomState.Waiting;
    }

    private bool canEditMaximum() //방장만 공유 정원 변경 요청
    {
        return canUseRoom() && room.HostPlayerId == room.Runner.LocalPlayer && applyCompleted == null;
    }

    private void refresh() //서버 확정 상태로 목록·권한·시작 조건 표시
    {
        players.Clear();
        gamePlayers.Clear();
        localPlayer = null;
        if (bootstrap == null) bootstrap = FindFirstObjectByType<NetworkBootstrap>();
        NetworkRunner gameRunner = null; //게임 중 목록의 실제 접속 세션
        string gameRoomName = string.Empty; //보존된 방 이름
        int gameMaximum = 0; //보존된 확정 정원
        PlayerRef gameHost = PlayerRef.None; //보존된 방장
        bool inGame = SceneManager.GetActiveScene().name == "GamePlay" && bootstrap != null &&
            bootstrap.tryGetRoomSession(out gameRunner, out gameRoomName, out gameMaximum, out gameHost); //대기실 객체 없이 조회 가능한 게임 세션
        if (inGame)
        {
            foreach (PlayerRef player in gameRunner.ActivePlayers) gamePlayers.Add(player);
            gamePlayers.Sort((left, right) =>
            {
                int order = bootstrap.getLobbyIdentity(left).joinOrder.CompareTo(bootstrap.getLobbyIdentity(right).joinOrder); //기존 대기실의 입장 순서 유지
                return order != 0 ? order : left.PlayerId.CompareTo(right.PlayerId);
            });
        }
        if (room != null)
        {
            foreach (LobbyPlayerController player in FindObjectsByType<LobbyPlayerController>(FindObjectsSortMode.None))
            {
                if (player.Object == null || !player.Object.IsValid || player.Runner != room.Runner) continue;
                players.Add(player);
                if (player.Object.HasInputAuthority) localPlayer = player;
            }
            players.Sort((left, right) => left.JoinOrder.CompareTo(right.JoinOrder));
            if (!dirty) draftMaximum = room.MaxPlayerCount;
            if (room.HostPlayerId != room.Runner.LocalPlayer)
            {
                dirty = false;
                draftMaximum = room.MaxPlayerCount;
                if (applyCompleted != null) finishApply(false, "방장만 인원을 변경할 수 있습니다.");
            }
        }
        int readyCount = 0; //준비 완료 참가자
        int playerCount = inGame ? gamePlayers.Count : players.Count; //현재 화면에 표시할 실제 참가 인원
        while (entries.Count < playerCount) entries.Add(Instantiate(playerEntryPrefab, playerListContainer));
        for (int i = 0; i < entries.Count; i++)
        {
            entries[i].gameObject.SetActive(i < playerCount);
            if (i >= playerCount) continue;
            if (inGame)
            {
                PlayerRef actor = gamePlayers[i]; //표시할 접속 참가자
                NetworkObject actorObject = gameRunner.GetPlayerObject(actor); //사망 후에도 유지되는 원래 플레이어 객체
                PlayerAppearance gameAppearance = actorObject != null && actorObject.IsValid ? actorObject.GetComponent<PlayerAppearance>() : null; //기존 확정 캐릭터 색상
                Color gameColor = gameAppearance != null ? PlayerAppearance.getColor(gameAppearance.colorIndex) : Color.gray; //접속 참가자의 아바타 색상
                entries[i].bindForSettings(bootstrap.getLobbyIdentity(actor).nickname, false,
                    actor == gameHost, actor == gameRunner.LocalPlayer, gameColor, false);
                continue;
            }
            LobbyPlayerController player = players[i]; //표시할 참가자
            PlayerAppearance appearance = player.GetComponent<PlayerAppearance>(); //기존 확정 캐릭터 색상
            Color color = appearance != null ? PlayerAppearance.getColor(appearance.colorIndex) : Color.gray; //아바타 색상
            entries[i].bindForSettings(player.Nickname.ToString(), player.IsReady,
                room.HostPlayerId == player.Object.InputAuthority, player == localPlayer, color);
            if (player.IsReady) readyCount++;
        }
        int maximum = inGame ? gameMaximum : room != null ? room.MaxPlayerCount : 0; //서버 확정 정원
        playerCountText.text = $"{playerCount} / {maximum}명";
        currentCountText.text = $"{playerCount}명";
        readyCountText.text = inGame ? "—" : $"{readyCount} / {playerCount}명";
        bool ready = localPlayer != null && localPlayer.IsReady; //본인의 확정 준비 상태
        myReadyText.text = inGame ? "게임 진행 중" : ready ? "준비 완료" : "준비 안 됨";
        myReadyText.color = ready ? new Color(0.45f, 0.9f, 0.37f) : new Color(0.6f, 0.67f, 0.75f);
        if (myReadyBackground != null) myReadyBackground.color = ready ? new Color(0.18f, 0.63f, 0.22f) : new Color(0.55f, 0.67f, 0.82f);
        readyButtonText.text = ready ? "준비 취소" : "준비";
        readyButton.interactable = canUseRoom() && localPlayer != null && applyCompleted == null;
        bool canEdit = canEditMaximum(); //정원 UI 입력 권한
        maximumInput.interactable = canEdit;
        if (!maximumInput.isFocused) maximumInput.SetTextWithoutNotify(inGame ? gameMaximum.ToString() : room != null ? draftMaximum.ToString() : "—");
        decreaseButton.interactable = canEdit && draftMaximum > Mathf.Max(RoomManager.AbsoluteMinPlayers, players.Count);
        increaseButton.interactable = canEdit && draftMaximum < RoomManager.AbsoluteMaxPlayers;
        int minimum = startManager != null ? startManager.minimumPlayerCount : RoomManager.AbsoluteMinPlayers; //실제 최소 시작 인원
        bool canStart = canEdit && startManager != null && players.Count >= minimum &&
            readyCount == players.Count && !dirty && !GameAudio.hasPendingRuleSettings; //서버와 동일한 기본 조건과 미적용 규칙
        startButton.interactable = canStart;
        startButtonText.text = "게임 시작";
        startHintText.text = $"방장만 시작할 수 있습니다.\n{minimum}명 이상·전원 준비 완료 시 시작";
        if (room != null && room.CurrentRoomState == RoomManager.RoomState.Starting && startManager != null)
        {
            float? remaining = startManager.countdownRemaining; //복제된 실제 시작 타이머
            startButtonText.text = remaining.HasValue ? $"{Mathf.CeilToInt(remaining.Value)}초 후 시작" : "시작 중";
        }
        else if (dirty || GameAudio.hasPendingRuleSettings) startHintText.text = "변경한 방 인원과 규칙을 먼저 적용해 주세요.";
        if (inGame)
        {
            startHintText.text = "게임 진행 중에는 인원 변경·준비·시작을 할 수 없습니다.";
            feedbackText.text = "게임 진행 중에는 로비 설정을 변경할 수 없습니다.";
        }
        else if (room == null) feedbackText.text = "방에 참가하면 플레이어와 방 설정을 확인할 수 있습니다.";
        else if (Time.unscaledTime >= feedbackDeadline) feedbackText.text = string.Empty;
        roomNameText.text = inGame ? gameRoomName : room != null ? room.RoomName.ToString() : "방 미참가";
        inviteCodeText.text = inGame && gameRunner.SessionInfo.IsValid ? gameRunner.SessionInfo.Name.ToUpperInvariant()
            : room != null ? room.InviteCode.ToString().ToUpperInvariant() : "—";
        bool connected = room != null && localPlayer != null && applyCompleted == null; //연결된 본인의 보조 기능
        copyInviteButton.interactable = connected && room.IsInviteEnabled;
        inviteButton.interactable = connected && canUseRoom() && LobbyRoomUI.Instance != null;
        leaveButton.interactable = connected && LobbyRoomUI.Instance != null;
    }

    private void decreaseMaximum() //임시 정원 감소
    {
        changeMaximum(draftMaximum - 1);
    }

    private void increaseMaximum() //임시 정원 증가
    {
        changeMaximum(draftMaximum + 1);
    }

    private void setMaximum(string value) //정수 입력을 검증해 임시 정원 반영
    {
        if (!canEditMaximum()) return;
        if (!int.TryParse(value, out int maximum))
        {
            showFeedback("인원을 숫자로 입력해 주세요.");
            maximumInput.SetTextWithoutNotify(draftMaximum.ToString());
            return;
        }
        changeMaximum(maximum);
    }

    private void changeMaximum(int maximum) //공유 상태를 바꾸지 않고 선택 값만 변경
    {
        if (!canEditMaximum()) return;
        draftMaximum = Mathf.Clamp(maximum, Mathf.Max(RoomManager.AbsoluteMinPlayers, players.Count), RoomManager.AbsoluteMaxPlayers);
        dirty = draftMaximum != room.MaxPlayerCount;
        maximumInput.SetTextWithoutNotify(draftMaximum.ToString());
        refresh();
    }

    internal void finishInput() //적용 전에 입력 중인 숫자 확정
    {
        if (maximumInput.isFocused)
        {
            setMaximum(maximumInput.text);
            maximumInput.DeactivateInputField();
        }
    }

    internal void restoreDefaults() //현재 참가 인원을 보존한 기본 정원 미리보기
    {
        if (canEditMaximum()) changeMaximum(room.defaultMaximumPlayers);
    }

    internal void applyMaximum(Action<bool, string> completed) //기존 RPC와 서버 승인으로 공유 설정 확정
    {
        finishInput();
        if (!dirty) { completed(true, string.Empty); return; }
        if (!canEditMaximum()) { completed(false, "방장만 대기 중에 인원을 변경할 수 있습니다."); return; }
        if (draftMaximum < players.Count) { completed(false, "현재 참가 인원보다 적게 설정할 수 없습니다."); return; }
        applyCompleted = completed;
        responseDeadline = Time.unscaledTime + responseTimeout;
        room.RPC_RequestChangeMaxPlayer(draftMaximum);
        if (editing) refresh();
    }

    private void handleMaximumChanged(int maximum) //서버가 확정한 정원과 적용 결과 처리
    {
        if (applyCompleted != null && maximum == draftMaximum)
        {
            dirty = false;
            finishApply(true, string.Empty);
        }
        else if (!dirty) draftMaximum = maximum;
    }

    private void handleMaximumRejected(string reason) //서버 거절을 공용 적용 화면에 전달
    {
        showFeedback(reason);
        if (applyCompleted != null) finishApply(false, reason);
    }

    private void finishApply(bool accepted, string reason) //중복 응답을 막고 완료 한 번 전달
    {
        Action<bool, string> completed = applyCompleted; //이번 요청의 완료 함수
        applyCompleted = null;
        completed?.Invoke(accepted, reason);
    }

    private void toggleReady() //본인의 준비 권한과 기존 RPC 재사용
    {
        if (canUseRoom() && localPlayer != null && applyCompleted == null) localPlayer.ToggleReady();
    }

    private void startGame() //실제 서버 시작 검증으로 게임 시작 요청
    {
        refresh();
        if (startButton.interactable) startManager.RPC_RequestStartGame();
    }

    private void handleStartFailed(LobbyGameStartManager.StartFailReason reason) //서버의 시작 거절 안내
    {
        showFeedback(reason switch
        {
            LobbyGameStartManager.StartFailReason.NotHost => "방장만 게임을 시작할 수 있습니다.",
            LobbyGameStartManager.StartFailReason.NotEnoughPlayers => "게임을 시작할 최소 인원이 부족합니다.",
            LobbyGameStartManager.StartFailReason.NotAllReady => "모든 플레이어가 준비해야 시작할 수 있습니다.",
            _ => "이미 게임 시작 절차가 진행 중입니다."
        });
    }

    private void showFeedback(string message) //거절 안내를 일정 시간 표시
    {
        feedbackText.text = message;
        feedbackDeadline = Time.unscaledTime + 3f;
    }

    private void copyInviteCode() //공유된 실제 초대 코드만 클립보드에 복사
    {
        if (!copyInviteButton.interactable) return;
        GUIUtility.systemCopyBuffer = room.InviteCode.ToString().ToUpperInvariant();
        showFeedback("초대 코드가 복사되었습니다.");
    }

    private void openInvites() //편집 상태를 보존한 기존 초대창 호출
    {
        if (inviteButton.interactable) GameAudio.showLobbyModal(true);
    }

    private void openLeaveConfirmation() //편집 상태를 보존한 기존 나가기 확인창 호출
    {
        if (leaveButton.interactable) GameAudio.showLobbyModal(false);
    }

    private void OnDestroy() //UI 입력과 방 이벤트 구독 정리
    {
        endEditing();
        decreaseButton.onClick.RemoveListener(decreaseMaximum);
        increaseButton.onClick.RemoveListener(increaseMaximum);
        readyButton.onClick.RemoveListener(toggleReady);
        startButton.onClick.RemoveListener(startGame);
        maximumInput.onEndEdit.RemoveListener(setMaximum);
        copyInviteButton.onClick.RemoveListener(copyInviteCode);
        inviteButton.onClick.RemoveListener(openInvites);
        leaveButton.onClick.RemoveListener(openLeaveConfirmation);
    }
}
