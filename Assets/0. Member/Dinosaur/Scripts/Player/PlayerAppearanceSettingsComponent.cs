using System;
using UnityEngine;

[DefaultExecutionOrder(-200)]
[DisallowMultipleComponent]
public class PlayerAppearanceSettingsComponent : MonoBehaviour
{
    private const string preferencePrefix = "TrustNoOne.Appearance."; //기존 색상 키와 새 장식 키의 공통 접두어
    [SerializeField] private Color[] palette =
    {
        new Color(0.16f, 0.52f, 0.90f), new Color(0.90f, 0.20f, 0.23f), new Color(0.18f, 0.70f, 0.36f),
        new Color(0.96f, 0.67f, 0.12f), new Color(0.62f, 0.30f, 0.86f), new Color(0.12f, 0.80f, 0.86f),
        new Color(0.96f, 0.38f, 0.70f), new Color(0.98f, 0.43f, 0.13f), new Color(0.62f, 0.85f, 0.22f),
        new Color(0.90f, 0.91f, 0.94f), new Color(0.46f, 0.51f, 0.59f), new Color(0.43f, 0.28f, 0.18f),
        new Color(0.20f, 0.27f, 0.46f), new Color(0.25f, 0.75f, 0.64f), new Color(0.80f, 0.57f, 0.82f)
    }; //서버와 클라이언트가 공통으로 사용하는 선택 색상
    [SerializeField] private int defaultColorIndex; //개인 설정의 기본 색상
    [SerializeField] private PlayerCosmeticsCatalog cosmeticsCatalog; //장식 선택과 서버 검증에 사용할 목록

    internal static PlayerAppearanceSettingsComponent instance { get; private set; } //공용 프리팹의 개인 설정 담당
    internal event Action changed; //미리보기 또는 확정 외형 변경 알림
    internal PlayerAppearanceSelection selected => isEditing ? draft : saved; //설정창에서 표시할 외형
    internal PlayerAppearanceSelection preferred => saved; //다음 방에서 요청할 개인 외형
    internal PlayerAppearanceSelection current => editingStart; //현재 방에서 확정된 외형
    internal PlayerAppearanceSelection defaults => new() { color = defaultColorIndex }; //기본 색상과 장식 없음
    internal int colorCount => palette.Length; //선택 색상 수
    internal PlayerCosmeticsCatalog catalog => cosmeticsCatalog; //설정 UI와 캐릭터가 공유하는 카탈로그
    internal bool hasChanges => isEditing && (!draft.Equals(editingStart) || saveFailed); //적용 또는 재저장이 필요한 편집

    private PlayerAppearanceSelection saved; //디스크에 저장한 개인 외형
    private PlayerAppearanceSelection editingStart; //취소할 때 돌아갈 현재 기준
    private PlayerAppearanceSelection draft; //미리보기에서만 바꾼 외형
    private bool isEditing; //설정창 편집 중 여부
    private bool saveFailed; //승인된 외형을 로컬에 재저장해야 하는지 여부

    private void Awake() //기존 색상과 유효한 장식 저장값 읽기
    {
        if (instance != null && instance != this) return;
        instance = this;
        saved.color = validColor(PlayerPrefs.GetInt(preferencePrefix + "Color", defaultColorIndex));
        saved.face = loadItem(PlayerAppearanceSlot.Face);
        saved.head = loadItem(PlayerAppearanceSlot.Head);
        saved.body = loadItem(PlayerAppearanceSlot.Body);
    }

    private int loadItem(PlayerAppearanceSlot slot) //삭제되거나 잘못된 로컬 장식은 장착하지 않음
    {
        int id = PlayerPrefs.GetInt(preferencePrefix + slot); //슬롯의 기존 저장 ID
        return isValid(slot, id) ? id : 0;
    }

    private int validColor(int index) //잘못된 로컬 색상 값 보정
    {
        return index >= 0 && index < palette.Length ? index : Mathf.Clamp(defaultColorIndex, 0, palette.Length - 1);
    }

    internal Color getColor(int index) //공통 팔레트 색상 조회
    {
        return palette.Length > 0 ? palette[validColor(index)] : Color.white;
    }

    internal bool isValid(PlayerAppearanceSlot slot, int id) //로컬 선택과 호스트 요청에 공통 적용
    {
        return slot == PlayerAppearanceSlot.Color
            ? id >= 0 && id < palette.Length
            : id == 0 || (cosmeticsCatalog != null && cosmeticsCatalog.find(slot, id) != null);
    }

    internal bool isValid(PlayerAppearanceSelection selection) //다른 슬롯의 ID와 등록되지 않은 색상 거부
    {
        return isValid(PlayerAppearanceSlot.Color, selection.color) && isValid(PlayerAppearanceSlot.Face, selection.face) &&
               isValid(PlayerAppearanceSlot.Head, selection.head) && isValid(PlayerAppearanceSlot.Body, selection.body);
    }

    internal void beginEditing(PlayerAppearanceSelection baseline) //확정 외형을 현재 설정의 시작값으로 사용
    {
        editingStart = baseline;
        draft = baseline;
        isEditing = true;
        changed?.Invoke();
    }

    internal void updateCurrent(PlayerAppearanceSelection confirmed) //초기 배정과 이후 승인 결과를 취소 기준에 반영
    {
        if (!isEditing || editingStart.Equals(confirmed)) return;
        for (PlayerAppearanceSlot slot = PlayerAppearanceSlot.Color; slot <= PlayerAppearanceSlot.Body; slot++)
            if (draft.get(slot) == editingStart.get(slot)) draft.set(slot, confirmed.get(slot));
        editingStart = confirmed;
        changed?.Invoke();
    }

    internal void select(PlayerAppearanceSlot slot, int id) //유효한 한 슬롯만 미리보기에서 변경
    {
        if (!isEditing || !isValid(slot, id) || draft.get(slot) == id) return;
        draft.set(slot, id);
        changed?.Invoke();
    }

    internal void restoreDefaults(int color) //선택 가능한 기본 색상으로 장식을 모두 제거
    {
        if (!isEditing) return;
        draft = new PlayerAppearanceSelection { color = color };
        changed?.Invoke();
    }

    internal bool save(PlayerAppearanceSelection confirmed) //승인된 외형만 개인 설정으로 저장
    {
        try
        {
            writePreferences(confirmed);
            PlayerPrefs.Save();
            saved = confirmed;
            saveFailed = false;
            updateCurrent(confirmed);
            return true;
        }
        catch (Exception exception)
        {
            saveFailed = true;
            try { writePreferences(saved); }
            catch (Exception restoreException) { Debug.LogException(restoreException); }
            Debug.LogError($"[PlayerAppearanceSettingsComponent] 외형 저장 실패: {exception.Message}");
            return false;
        }
    }

    private static void writePreferences(PlayerAppearanceSelection selection) //색상 키 호환과 슬롯별 값 유지
    {
        PlayerPrefs.SetInt(preferencePrefix + "Color", selection.color);
        PlayerPrefs.SetInt(preferencePrefix + "Face", selection.face);
        PlayerPrefs.SetInt(preferencePrefix + "Head", selection.head);
        PlayerPrefs.SetInt(preferencePrefix + "Body", selection.body);
    }

    internal void cancelEditing() //미적용 선택을 폐기
    {
        isEditing = false;
        saveFailed = false;
        changed?.Invoke();
    }

    private void OnDestroy() //공용 참조 정리
    {
        if (instance == this) instance = null;
    }
}
