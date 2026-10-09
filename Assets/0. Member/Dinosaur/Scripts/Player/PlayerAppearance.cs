using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Fusion;
using LockdownProtocol.Lobby;
using UnityEngine;
using UnityEngine.SceneManagement;

[RequireComponent(typeof(PlayerAppearanceVisualComponent))]
public class PlayerAppearance : NetworkBehaviour
{
    [Networked] internal int colorIndex { get; private set; } //호스트가 승인한 캐릭터 색상
    [Networked] private NetworkBool colorAssigned { get; set; } //초기 색상 배정 완료 여부
    private static readonly ConditionalWeakTable<NetworkRunner, Dictionary<PlayerRef, int>> sessionColors = new(); //씬 전환 중 유지하는 서버의 색상 예약
    private static PlayerAppearance localPlayer; //본인의 색상 요청 진입점
    private PlayerAppearanceVisualComponent visuals; //캐릭터 재질과 음성 표시 담당
    private int displayedIndex = -1; //마지막으로 표시한 색상

    internal static PlayerAppearanceSettingsComponent settings => PlayerAppearanceSettingsComponent.instance; //개인 설정 구성 요소 연결
    internal static int selectedColorIndex => settings != null ? settings.selectedIndex : 0; //설정창 미리보기 색상
    internal static int colorCount => settings != null ? settings.colorCount : 0; //공통 팔레트 크기
    internal static bool canCustomize => SceneManager.GetActiveScene().name != "GamePlay" &&
        (RoomManager.Instance == null || RoomManager.Instance.Object == null || !RoomManager.Instance.Object.IsValid ||
         RoomManager.Instance.CurrentRoomState == RoomManager.RoomState.Waiting); //게임 진행·시작 중 색상 변경 차단

    public override void Spawned() //권한별 초기 배정과 로컬 표시 연결
    {
        visuals = GetComponent<PlayerAppearanceVisualComponent>();
        visuals.initialize(GetComponent<Photon.Voice.Fusion.VoiceNetworkObject>(), GetComponent<PlayerHealth>(), HasInputAuthority);
        if (HasStateAuthority)
        {
            Dictionary<PlayerRef, int> colors = getSessionColors(); //이 방에서 유지할 색상 예약
            if (!colors.TryGetValue(Object.InputAuthority, out int index))
            {
                index = findAvailableColor(settings != null ? settings.committedIndex : 0, colors);
                colors[Object.InputAuthority] = index;
            }
            colorIndex = index;
            colorAssigned = true;
        }
        if (HasInputAuthority)
        {
            localPlayer = this;
            RPC_RequestColor(settings != null ? settings.committedIndex : 0);
        }
    }

    public override void Render() //복제된 색상을 표시 구성 요소에 전달
    {
        if (!colorAssigned || settings == null || displayedIndex == colorIndex) return;
        visuals.setColor(settings.getColor(colorIndex));
        displayedIndex = colorIndex;
    }

    private Dictionary<PlayerRef, int> getSessionColors() //퇴장한 예약을 제거하고 같은 Runner의 배정 유지
    {
        Dictionary<PlayerRef, int> colors = sessionColors.GetOrCreateValue(Runner); //씬과 별개인 세션 예약
        List<PlayerRef> departed = new(); //삭제할 퇴장 플레이어
        foreach (PlayerRef actor in colors.Keys)
        {
            bool present = false; //현재 세션 참가 여부
            foreach (PlayerRef active in Runner.ActivePlayers) if (active == actor) { present = true; break; }
            if (!present) departed.Add(actor);
        }
        foreach (PlayerRef actor in departed) colors.Remove(actor);
        return colors;
    }

    private int findAvailableColor(int requested, Dictionary<PlayerRef, int> colors) //중복 요청을 미사용 색상으로 조정
    {
        int count = settings != null ? settings.colorCount : 0; //서버 팔레트 범위
        if (requested >= 0 && requested < count && !isReserved(requested, colors)) return requested;
        List<int> available = new(); //사용 가능한 대체 색상
        for (int i = 0; i < count; i++) if (!isReserved(i, colors)) available.Add(i);
        return available.Count > 0 ? available[Random.Range(0, available.Count)] : colorIndex;
    }

    private bool isReserved(int index, Dictionary<PlayerRef, int> colors) //다른 참가자의 색상 점유 확인
    {
        foreach (KeyValuePair<PlayerRef, int> pair in colors)
            if (pair.Key != Object.InputAuthority && pair.Value == index) return true;
        return false;
    }

    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    private void RPC_RequestColor(int requested) //호스트의 범위·씬·중복 검증
    {
        if (!HasStateAuthority || settings == null || requested < 0 || requested >= settings.colorCount) return;
        RoomManager room = RoomManager.Instance; //같은 세션의 대기 상태
        bool canChange = Runner.SceneManager != null && Runner.SceneManager.MainRunnerScene.name == "StandBy" &&
            room != null && room.Object != null && room.Object.IsValid && room.Runner == Runner &&
            room.CurrentRoomState == RoomManager.RoomState.Waiting; //대기실에서만 실제 색상 변경
        if (canChange)
        {
            Dictionary<PlayerRef, int> colors = getSessionColors(); //검증할 예약 상태
            colorIndex = findAvailableColor(requested, colors);
            colors[Object.InputAuthority] = colorIndex;
        }
        RPC_AcceptColor(colorIndex);
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.InputAuthority)]
    private void RPC_AcceptColor(int accepted) //본인에게 실제 배정색 전달
    {
        settings?.acceptColor(accepted);
    }

    internal static Color getColor(int index) //설정창과 미리보기의 공통 색상 조회
    {
        return settings != null ? settings.getColor(index) : Color.white;
    }

    internal static void beginCustomization() //외부 UI의 편집 시작 진입점
    {
        settings?.beginEditing();
    }

    internal static void selectColor(int index) //외부 UI의 색상 선택 진입점
    {
        if (canCustomize) settings?.selectColor(index);
    }

    internal static void restoreDefaults() //편집 중 기본 색상 선택
    {
        if (canCustomize) settings?.restoreDefaults();
    }

    internal static bool prepareSave() //기존 음량 저장과 함께 색상 저장 준비
    {
        return settings == null || settings.prepareSave();
    }

    internal static void completeSave() //설정 적용 성공 후 서버 변경 요청
    {
        bool changed = settings != null && settings.completeSave(); //확정된 색상 변경 여부
        if (changed && canCustomize && localPlayer != null && localPlayer.Object != null && localPlayer.Object.IsValid)
            localPlayer.RPC_RequestColor(settings.committedIndex);
    }

    internal static void rollbackSave() //공용 설정 저장 실패 복원
    {
        settings?.rollbackSave();
    }

    internal static void cancelCustomization() //미적용 색상 편집 취소
    {
        settings?.cancelEditing();
    }

    public override void Despawned(NetworkRunner runner, bool hasState) //음성 표시와 로컬 진입점 정리
    {
        visuals?.releaseVoice();
        if (localPlayer == this) localPlayer = null;
    }
}
