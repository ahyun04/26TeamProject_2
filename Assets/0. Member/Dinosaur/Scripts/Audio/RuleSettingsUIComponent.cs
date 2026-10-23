using System;
using LockdownProtocol.Lobby;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class RuleSettingsUIComponent : MonoBehaviour
{
    [SerializeField] private TMP_InputField killerCountInput; //적용 전 살인마 수
    [SerializeField] private TMP_Dropdown mapDropdown; //다음 경기에 사용할 맵 선택
    [SerializeField] private Button decreaseButton; //살인마 수 감소
    [SerializeField] private Button increaseButton; //살인마 수 증가
    [SerializeField] private TMP_Text hintText; //권한과 현재 규칙 안내
    [SerializeField, Min(1f)] private float responseTimeout = 8f; //서버 응답 대기 한도

    private RoomManager room; //기존 방 상태 진입점
    private Action<bool, string> applyCompleted; //공용 적용 버튼의 완료 알림
    private bool editing; //설정창 편집 중 여부
    private bool dirty; //확정 값과 다른 임시 설정 여부
    private int draftCount = 1; //적용 전 살인마 수
    private string draftMap = string.Empty; //적용 전 맵 선택
    private float nextRefresh; //연결과 권한 확인 간격
    private float responseDeadline; //서버 응답 제한 시각
    internal bool hasPendingChange => editing && dirty && room != null; //공유 적용이 필요한 규칙

    private void Awake() //기존 설정창 버튼에 규칙 입력 연결
    {
        decreaseButton.onClick.AddListener(decreaseCount);
        increaseButton.onClick.AddListener(increaseCount);
        killerCountInput.onEndEdit.AddListener(setCount);
        mapDropdown.onValueChanged.AddListener(selectMap);
    }

    internal void beginEditing() //서버 확정 값부터 새 편집 시작
    {
        editing = true;
        dirty = false;
        bindRoom();
        draftCount = room != null ? room.KillerCount : 1;
        draftMap = room != null ? room.GameplayMap.ToString() : string.Empty;
        refresh();
    }

    internal void endEditing() //닫기와 씬 전환에서 임시 값과 구독 정리
    {
        editing = false;
        dirty = false;
        applyCompleted = null;
        unbindRoom();
    }

    private void Update() //복제 상태와 요청 제한 확인
    {
        if (!editing) return;
        if (applyCompleted != null && Time.unscaledTime >= responseDeadline)
            finishApply(false, "응답을 받지 못했습니다. 게임 설정을 확인한 뒤 다시 적용해 주세요.");
        if (Time.unscaledTime < nextRefresh) return;
        nextRefresh = Time.unscaledTime + 0.15f;
        bindRoom();
        refresh();
    }

    private void bindRoom() //현재 유효 방에만 이벤트 연결
    {
        RoomManager current = RoomManager.Instance; //현재 세션의 방
        if (current != null && (current.Object == null || !current.Object.IsValid)) current = null;
        if (current == room && (current != null || ReferenceEquals(room, null))) return;
        if (applyCompleted != null) finishApply(false, "방 연결이 변경되었습니다.");
        unbindRoom();
        room = current;
        dirty = false;
        if (room == null) return;
        draftCount = room.KillerCount;
        draftMap = room.GameplayMap.ToString();
        room.KillerCountChanged += handleCountChanged;
        room.KillerCountChangeRejected += handleCountRejected;
        room.GameSettingsChanged += handleSettingsChanged;
        room.GameSettingsChangeRejected += handleCountRejected;
    }

    private void unbindRoom() //파괴된 Unity 객체도 이벤트 구독 해제
    {
        if (!ReferenceEquals(room, null))
        {
            room.KillerCountChanged -= handleCountChanged;
            room.KillerCountChangeRejected -= handleCountRejected;
            room.GameSettingsChanged -= handleSettingsChanged;
            room.GameSettingsChangeRejected -= handleCountRejected;
        }
        room = null;
    }

    private bool canEdit() //대기 중인 방장만 다음 경기 규칙 편집
    {
        return editing && room != null && room.Object != null && room.Object.IsValid &&
            room.HostPlayerId == room.Runner.LocalPlayer && room.CurrentRoomState == RoomManager.RoomState.Waiting &&
            SceneManager.GetActiveScene().name == "StandBy" && applyCompleted == null;
    }

    private void refresh() //현재 인원 상한과 확정 상태 표시
    {
        if (room != null)
        {
            if (!dirty || room.HostPlayerId != room.Runner.LocalPlayer)
            {
                draftCount = room.KillerCount;
                draftMap = room.GameplayMap.ToString();
            }
            if (applyCompleted == null)
            {
                draftCount = Mathf.Clamp(draftCount, 1, room.getMaximumKillerCount());
                dirty = draftCount != room.KillerCount || draftMap != room.GameplayMap.ToString();
            }
        }
        else if (NetworkBootstrap.isGameplayScene(SceneManager.GetActiveScene().name))
        {
            RoleAssignment roles = FindFirstObjectByType<RoleAssignment>(); //진행 중 경기에서 실제로 배정한 역할 수
            if (roles != null && roles.Object != null && roles.Object.IsValid && roles.Initialized)
            {
                draftCount = 0;
                foreach (var entry in roles.Roles) if (entry.Value == PlayerRole.Killer) draftCount++;
            }
        }
        bool allowed = canEdit(); //버튼과 숫자 입력의 동일한 권한
        killerCountInput.interactable = allowed;
        mapDropdown.interactable = allowed;
        mapDropdown.SetValueWithoutNotify(draftMap == NetworkBootstrap.smallMapSceneName ? 1 : 0);
        if (!killerCountInput.isFocused) killerCountInput.SetTextWithoutNotify(draftCount.ToString());
        decreaseButton.interactable = allowed && draftCount > 1;
        increaseButton.interactable = allowed && draftCount < room.getMaximumKillerCount();
        hintText.text = room == null ? (NetworkBootstrap.isGameplayScene(SceneManager.GetActiveScene().name) ? $"현재 살인마 {draftCount}명\n게임 진행 중에는 규칙을 변경할 수 없습니다." : "방에 참가하면 규칙을 확인할 수 있습니다.") :
            allowed ? $"4~12인 맵 · 살인마 1~{room.getMaximumKillerCount()}명\n맵과 살인마 수는 적용 후 반영됩니다." :
            $"현재 살인마 {room.KillerCount}명\n방장만 게임 시작 전에 변경할 수 있습니다.";
    }

    private void decreaseCount() => changeCount(draftCount - 1); //임시 인원 감소
    private void increaseCount() => changeCount(draftCount + 1); //임시 인원 증가

    private void setCount(string value) //입력 숫자를 허용 범위에 반영
    {
        if (int.TryParse(value, out int count)) changeCount(count);
        killerCountInput.SetTextWithoutNotify(draftCount.ToString());
    }

    private void changeCount(int count) //적용 전 공유 상태를 바꾸지 않는 편집
    {
        if (!canEdit()) return;
        draftCount = Mathf.Clamp(count, 1, room.getMaximumKillerCount());
        dirty = draftCount != room.KillerCount || draftMap != room.GameplayMap.ToString();
        refresh();
    }

    private void selectMap(int index) //맵 선택은 적용 전까지 임시 값으로 보관
    {
        if (!canEdit()) return;
        draftMap = index == 1 ? NetworkBootstrap.smallMapSceneName : string.Empty;
        dirty = draftCount != room.KillerCount || draftMap != room.GameplayMap.ToString();
        refresh();
    }

    internal void finishInput() //적용과 탭 전환 전에 숫자 입력 확정
    {
        if (!killerCountInput.isFocused) return;
        setCount(killerCountInput.text);
        killerCountInput.DeactivateInputField();
    }

    internal void restoreDefaults() => changeCount(1); //살인마 기본 인원 미리보기

    internal void applyCount(Action<bool, string> completed) //호스트 승인 뒤 공용 설정 저장 계속
    {
        finishInput();
        if (!NetworkBootstrap.tryGetMapScene(draftMap, out _)) { completed(false, "맵을 골라주세요."); return; }
        if (!dirty) { completed(true, string.Empty); return; }
        if (!canEdit()) { completed(false, "방장만 대기 중에 규칙을 변경할 수 있습니다."); return; }
        applyCompleted = completed;
        responseDeadline = Time.unscaledTime + responseTimeout;
        room.RPC_RequestGameSettings(draftMap, draftCount);
    }

    private void handleCountChanged(int count) //확정 값과 대기 중 요청 결과 반영
    {
        if (applyCompleted != null && draftCount == count && draftMap == room.GameplayMap.ToString())
        {
            dirty = false;
            finishApply(true, string.Empty);
        }
        else if (!dirty) draftCount = count;
    }

    private void handleSettingsChanged() => handleCountChanged(room.KillerCount); //일괄 적용 결과를 기존 완료 경로로 전달

    private void handleCountRejected(string reason) => finishApply(false, reason); //요청 거부를 공용 적용 안내로 전달

    private void finishApply(bool accepted, string reason) //완료 알림의 중복 실행 방지
    {
        Action<bool, string> completed = applyCompleted; //이번 요청의 완료 함수
        applyCompleted = null;
        completed?.Invoke(accepted, reason);
    }

    private void OnDestroy() //입력과 방 이벤트 구독 정리
    {
        endEditing();
        decreaseButton.onClick.RemoveListener(decreaseCount);
        increaseButton.onClick.RemoveListener(increaseCount);
        killerCountInput.onEndEdit.RemoveListener(setCount);
        mapDropdown.onValueChanged.RemoveListener(selectMap);
    }
}
