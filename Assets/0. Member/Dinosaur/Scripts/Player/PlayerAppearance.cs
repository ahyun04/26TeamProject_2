using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Fusion;
using LockdownProtocol.Lobby;
using UnityEngine;
using UnityEngine.SceneManagement;

[RequireComponent(typeof(PlayerAppearanceVisualComponent))]
public class PlayerAppearance : NetworkBehaviour
{
    private enum ApplyResult { Accepted, Unavailable, Invalid, InUse }

    [Networked] private PlayerAppearanceSelection appearance { get; set; } //호스트가 확정한 전체 외형
    [Networked] private NetworkBool awaitingPreference { get; set; } //입장한 참가자의 저장 외형을 기다리는 상태

    private sealed class Reservation
    {
        internal PlayerAppearanceSelection selection; //씬 전환 후 재사용할 외형
        internal bool awaiting; //세션에서 초기 외형 요청을 받았는지 여부
    }

    private static readonly ConditionalWeakTable<NetworkRunner, Dictionary<PlayerRef, Reservation>> sessionAppearances = new(); //방 단위 외형 예약
    private static PlayerAppearance localPlayer; //본인의 외형 요청 진입점
    private static PlayerAppearance readyLocal => localPlayer != null && localPlayer.isSpawned ? localPlayer : null; //유효한 본인 네트워크 오브젝트
    private bool isSpawned => spawned && Object != null && Object.IsValid && Runner.IsRunning; //복제 상태를 읽을 수 있는지 여부
    private PlayerAppearanceVisualComponent visuals; //캐릭터 재질과 음성 표시 담당
    private PlayerAppearanceSelection? displayed; //마지막으로 표시한 외형
    private bool spawned; //네트워크 초기화 여부
    private bool sentPreference; //저장 외형 전송 중복 방지
    private int requestSequence; //이전 적용 응답과 구분할 요청 번호
    private float responseDeadline; //서버의 승인 응답 대기 만료 시각
    private Action<bool, string> applyCompleted; //현재 적용 요청의 완료 처리

    internal int colorIndex => isSpawned ? appearance.color : 0; //기존 로비 및 관전자 색상 조회
    internal static PlayerAppearanceSettingsComponent settings => PlayerAppearanceSettingsComponent.instance; //개인 설정 구성 요소
    internal static bool isApplying => readyLocal != null && readyLocal.applyCompleted != null; //승인 대기 중인 편집 잠금
    internal static bool hasFallbackColor => canCustomize && readyLocal != null && settings != null &&
        readyLocal.colorIndex != settings.preferred.color; //입장 시 저장한 색상 대신 다른 색상 배정
    internal static bool canCustomize //대기실과 메인·로비에서만 선택 허용
    {
        get
        {
            string scene = SceneManager.GetActiveScene().name; //현재 설정을 연 씬
            PlayerAppearance player = readyLocal; //대기실 전환 중이라면 본인이 준비될 때까지 잠금
            return scene == "Main" || scene == "Lobby" ||
                   (scene == "StandBy" && player != null && !player.awaitingPreference && player.isWaitingInRoom());
        }
    }

    public override void Spawned() //호스트 색상 예약과 로컬 표시 연결
    {
        displayed = null;
        sentPreference = false;
        visuals = GetComponent<PlayerAppearanceVisualComponent>();
        visuals.initialize(GetComponent<Photon.Voice.Fusion.VoiceNetworkObject>(), GetComponent<PlayerHealth>(), HasInputAuthority);
        visuals.initializeCosmetics(settings != null ? settings.catalog : null, GetComponent<PlayerLookVisualComponent>()?.modelRoot);
        spawned = true;

        if (HasStateAuthority)
        {
            Dictionary<PlayerRef, Reservation> session = getReservations(); //이 방에서 유지할 외형
            if (!session.TryGetValue(Object.InputAuthority, out Reservation reservation))
            {
                PlayerAppearanceSelection selection = HasInputAuthority && settings != null
                    ? settings.preferred : settings != null ? settings.defaults : default; //초기 저장값 또는 임시 예약
                selection.color = findAvailableColor(selection.color, session);
                reservation = new Reservation { selection = selection, awaiting = !HasInputAuthority };
                session.Add(Object.InputAuthority, reservation);
            }
            appearance = reservation.selection;
            awaitingPreference = reservation.awaiting;
        }
        if (HasInputAuthority) localPlayer = this;
    }

    public override void Render() //복제 외형 표시와 본인의 서버 요청 진행
    {
        if (!isSpawned || settings == null) return;
        PlayerAppearanceSelection actual = appearance; //이 프레임에서 사용할 확정 외형
        if (!displayed.HasValue || !displayed.Value.Equals(actual))
        {
            displayed = actual;
            visuals.setAppearance(actual, settings.getColor(actual.color));
        }
        if (!HasInputAuthority) return;
        if (!awaitingPreference && applyCompleted == null) settings.updateCurrent(actual);
        if (awaitingPreference && !sentPreference && isWaitingInRoom())
        {
            sentPreference = true;
            RPC_RequestInitialAppearance(settings.preferred);
        }
        if (applyCompleted != null && Time.unscaledTime >= responseDeadline)
            finishApply(false, "외형 적용 응답을 받지 못했습니다. 다시 시도해 주세요.");
    }

    private Dictionary<PlayerRef, Reservation> getReservations() //퇴장한 플레이어의 색상 재사용 허용
    {
        Dictionary<PlayerRef, Reservation> session = sessionAppearances.GetOrCreateValue(Runner); //씬에 독립적인 외형 예약
        List<PlayerRef> departed = new(); //현재 세션에서 사라진 예약
        foreach (PlayerRef actor in session.Keys)
        {
            bool active = false; //예약자가 현재 세션 참가자인지 여부
            foreach (PlayerRef player in Runner.ActivePlayers)
                if (actor == player) { active = true; break; }
            if (!active) departed.Add(actor);
        }
        foreach (PlayerRef actor in departed) session.Remove(actor);
        return session;
    }

    private int findAvailableColor(int preferred, Dictionary<PlayerRef, Reservation> session) //신규 참가자에게 겹치지 않는 색상 배정
    {
        if (!isReserved(preferred, session)) return preferred;
        for (int index = 0; settings != null && index < settings.colorCount; index++)
            if (!isReserved(index, session)) return index;
        return preferred;
    }

    private bool isReserved(int color, Dictionary<PlayerRef, Reservation> session) //다른 참가자가 사용할 때만 색상 점유
    {
        foreach (KeyValuePair<PlayerRef, Reservation> pair in session)
            if (pair.Key != Object.InputAuthority && pair.Value.selection.color == color) return true;
        return false;
    }

    private bool isWaitingInRoom() //같은 대기실의 시작 전 상태 확인
    {
        RoomManager room = RoomManager.Instance; //현재 룸 설정의 권한과 진행 상태
        return isSpawned && Runner.SceneManager != null && Runner.SceneManager.MainRunnerScene.name == "StandBy" &&
               room != null && room.Object != null && room.Object.IsValid && room.Runner == Runner &&
               room.CurrentRoomState == RoomManager.RoomState.Waiting;
    }

    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    private void RPC_RequestInitialAppearance(PlayerAppearanceSelection preferred) //입장 시 저장한 외형 적용
    {
        if (!HasStateAuthority || !awaitingPreference || !isWaitingInRoom()) return;
        Dictionary<PlayerRef, Reservation> session = getReservations(); //입장 순서에 따른 호스트 색상 예약
        if (settings == null || !settings.isValid(preferred)) preferred = appearance;
        preferred.color = isReserved(preferred.color, session) ? appearance.color : preferred.color;
        confirm(preferred, session);
    }

    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    private void RPC_RequestAppearance(int request, PlayerAppearanceSelection selected) //호스트가 선택 전체를 하나의 요청으로 검증
    {
        if (!HasStateAuthority) return;
        ApplyResult result = ApplyResult.Accepted; //다른 참가자의 외형을 건드리지 않는 요청 결과
        if (!isWaitingInRoom() || awaitingPreference) result = ApplyResult.Unavailable;
        else if (settings == null || !settings.isValid(selected)) result = ApplyResult.Invalid;
        else
        {
            Dictionary<PlayerRef, Reservation> session = getReservations(); //동시 선택을 호스트에서 직렬 처리
            if (isReserved(selected.color, session)) result = ApplyResult.InUse;
            else confirm(selected, session);
        }
        RPC_AppearanceResult(request, result, appearance);
    }

    private void confirm(PlayerAppearanceSelection selected, Dictionary<PlayerRef, Reservation> session) //한 번에 복제하고 씬 전환용으로 보관
    {
        appearance = selected;
        awaitingPreference = false;
        session[Object.InputAuthority] = new Reservation { selection = selected };
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.InputAuthority)]
    private void RPC_AppearanceResult(int request, ApplyResult result, PlayerAppearanceSelection confirmed) //본인의 정확한 요청에 대한 승인 또는 오류
    {
        if (applyCompleted == null || request != requestSequence) return;
        if (result == ApplyResult.Accepted)
        {
            bool stored = settings != null && settings.save(confirmed); //네트워크 확정 후에만 로컬 저장
            finishApply(stored, stored ? string.Empty : "외형은 적용됐지만 이 PC에 저장하지 못했습니다.");
            return;
        }
        string reason = result switch
        {
            ApplyResult.InUse => "다른 플레이어가 사용 중인 색상입니다.",
            ApplyResult.Invalid => "사용할 수 없는 외형 항목이 있습니다.",
            _ => "대기실에서만 외형을 적용할 수 있습니다."
        }; //설정창의 거절 안내
        finishApply(false, reason);
    }

    private void finishApply(bool accepted, string reason) //현재 요청을 한 번만 완료
    {
        Action<bool, string> completed = applyCompleted; //요청 완료 이벤트
        applyCompleted = null;
        completed?.Invoke(accepted, reason);
    }

    internal static int getOccupiedColors() //본인을 제외한 방 참가자의 실시간 색상 목록
    {
        PlayerAppearance player = readyLocal; //현재 세션의 입력 권한
        if (player == null) return 0;
        int mask = 0; //색상별 점유 상태
        foreach (PlayerRef actor in player.Runner.ActivePlayers)
        {
            if (actor == player.Object.InputAuthority) continue;
            NetworkObject avatar = player.Runner.GetPlayerObject(actor); //현재 씬의 해당 참가자
            if (avatar != null && avatar.TryGetComponent(out PlayerAppearance peer) && peer.isSpawned)
                mask |= 1 << peer.appearance.color;
        }
        return mask;
    }

    internal static Color getColor(int index) //로비와 미리보기에 공용 색상 제공
    {
        return settings != null ? settings.getColor(index) : Color.white;
    }

    internal static void beginCustomization() //편집을 방의 확정 외형 또는 저장 외형에서 시작
    {
        if (settings != null) settings.beginEditing(readyLocal != null ? readyLocal.appearance : settings.preferred);
    }

    internal static void select(PlayerAppearanceSlot slot, int id) //설정 화면의 한 슬롯 미리보기 선택
    {
        if (!canCustomize || isApplying || settings == null || !settings.isValid(slot, id)) return;
        if (slot == PlayerAppearanceSlot.Color && (getOccupiedColors() & (1 << id)) != 0) return;
        settings.select(slot, id);
    }

    internal static bool restoreDefaults() //사용 중인 기본색이면 확정색으로 나머지 슬롯 초기화
    {
        if (!canCustomize || isApplying || settings == null) return true;
        int color = settings.defaults.color; //기획서의 기본 의상 색
        bool available = (getOccupiedColors() & (1 << color)) == 0; //방에 이미 기본색을 쓰는 참가자 여부
        settings.restoreDefaults(available ? color : settings.current.color);
        return available;
    }

    internal static void applyCustomization(Action<bool, string> completed) //오프라인 저장 또는 대기실 호스트에 적용 요청
    {
        if (settings == null || !settings.hasChanges) { completed(true, string.Empty); return; }
        if (!canCustomize) { completed(false, "대기실에서만 외형을 적용할 수 있습니다."); return; }
        PlayerAppearanceSelection selected = settings.selected; //이 요청에서 확정할 외형 전체
        if (!settings.isValid(selected)) { completed(false, "사용할 수 없는 외형 항목이 있습니다."); return; }
        PlayerAppearance player = readyLocal; //대기실이면 반드시 현재 방에 제출
        if (player == null)
        {
            bool stored = settings.save(selected); //메인·로비에서 개인 저장
            completed(stored, stored ? string.Empty : "외형 설정을 저장하지 못했습니다.");
            return;
        }
        if (player.applyCompleted != null) { completed(false, "외형을 적용하는 중입니다."); return; }
        player.applyCompleted = completed;
        player.responseDeadline = Time.unscaledTime + 5f;
        player.RPC_RequestAppearance(++player.requestSequence, selected);
    }

    internal static void cancelCustomization() //미적용 선택 취소
    {
        settings?.cancelEditing();
    }

    public override void Despawned(NetworkRunner runner, bool hasState) //세션 종료 시 로컬 요청과 음성 표시 정리
    {
        spawned = false;
        visuals?.releaseVoice();
        if (localPlayer == this) localPlayer = null;
        finishApply(false, "방과의 연결이 종료돼 외형을 적용하지 못했습니다.");
    }
}
