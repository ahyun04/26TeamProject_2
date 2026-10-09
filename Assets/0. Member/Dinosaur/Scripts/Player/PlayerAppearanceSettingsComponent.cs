using System;
using UnityEngine;

[DefaultExecutionOrder(-200)]
[DisallowMultipleComponent]
public class PlayerAppearanceSettingsComponent : MonoBehaviour
{
    private const string preferenceKey = "TrustNoOne.Appearance.Color"; //본인의 확정 색상 저장 키
    [SerializeField] private Color[] palette =
    {
        new Color(0.16f, 0.52f, 0.90f), new Color(0.90f, 0.20f, 0.23f), new Color(0.18f, 0.70f, 0.36f),
        new Color(0.96f, 0.67f, 0.12f), new Color(0.62f, 0.30f, 0.86f), new Color(0.12f, 0.80f, 0.86f),
        new Color(0.96f, 0.38f, 0.70f), new Color(0.98f, 0.43f, 0.13f), new Color(0.62f, 0.85f, 0.22f),
        new Color(0.90f, 0.91f, 0.94f), new Color(0.46f, 0.51f, 0.59f), new Color(0.43f, 0.28f, 0.18f),
        new Color(0.20f, 0.27f, 0.46f), new Color(0.25f, 0.75f, 0.64f), new Color(0.80f, 0.57f, 0.82f)
    }; //서버와 클라이언트가 공통으로 사용하는 선택 색상
    [SerializeField] private int defaultColorIndex; //개인 설정의 기본 색상

    internal static PlayerAppearanceSettingsComponent instance { get; private set; } //공용 프리팹의 개인 설정 담당
    internal event Action changed; //선택·확정 색상 변경 알림
    internal int selectedIndex => isEditing ? draftIndex : savedIndex; //설정창에서 표시할 색상
    internal int committedIndex => savedIndex; //현재 확정한 개인 색상
    internal int colorCount => palette.Length; //선택 가능한 색상 개수

    private int savedIndex; //적용을 마친 색상
    private int draftIndex; //미리보기 색상
    private bool isEditing; //설정창 편집 중 여부
    private bool prepared; //공용 저장에 포함할 색상 키 작성 여부
    private bool hadPreference; //저장 실패 시 기존 키 복원 여부
    private int previousPreference; //저장 실패 시 복원할 값

    private void Awake() //공용 설정 연결과 저장값 읽기
    {
        if (instance != null && instance != this) return;
        instance = this;
        savedIndex = validIndex(PlayerPrefs.GetInt(preferenceKey, defaultColorIndex));
        draftIndex = savedIndex;
    }

    internal Color getColor(int index) //공통 팔레트의 색상 조회
    {
        return palette[validIndex(index)];
    }

    private int validIndex(int index) //잘못 저장된 색상 보정
    {
        return index >= 0 && index < palette.Length ? index : Mathf.Clamp(defaultColorIndex, 0, palette.Length - 1);
    }

    internal void beginEditing() //확정값을 미리보기 시작값으로 사용
    {
        if (isEditing) return;
        draftIndex = savedIndex;
        isEditing = true;
        changed?.Invoke();
    }

    internal void selectColor(int index) //유효한 미리보기 색상 선택
    {
        if (!isEditing || index < 0 || index >= palette.Length || draftIndex == index) return;
        draftIndex = index;
        changed?.Invoke();
    }

    internal void restoreDefaults() //기본값을 미리보기에만 반영
    {
        selectColor(validIndex(defaultColorIndex));
    }

    internal bool prepareSave() //음량과 색상을 같은 PlayerPrefs 저장에 포함
    {
        if (!isEditing || prepared) return true;
        try
        {
            hadPreference = PlayerPrefs.HasKey(preferenceKey);
            previousPreference = PlayerPrefs.GetInt(preferenceKey, savedIndex);
            PlayerPrefs.SetInt(preferenceKey, draftIndex);
            prepared = true;
            return true;
        }
        catch (Exception exception)
        {
            Debug.LogError($"[PlayerAppearanceSettingsComponent] 색상 저장 준비 실패: {exception.Message}");
            return false;
        }
    }

    internal bool completeSave() //공용 저장 성공 뒤 색상 확정
    {
        bool hasChanged = savedIndex != draftIndex; //서버에 전달할 변경 여부
        savedIndex = draftIndex;
        isEditing = false;
        prepared = false;
        changed?.Invoke();
        return hasChanged;
    }

    internal void rollbackSave() //공용 저장 실패 시 기존 색상 키 복원
    {
        if (!prepared) return;
        if (hadPreference) PlayerPrefs.SetInt(preferenceKey, previousPreference);
        else PlayerPrefs.DeleteKey(preferenceKey);
        prepared = false;
    }

    internal void cancelEditing() //미적용 색상을 폐기
    {
        rollbackSave();
        draftIndex = savedIndex;
        isEditing = false;
        changed?.Invoke();
    }

    internal void acceptColor(int index) //호스트가 조정한 실제 색상 저장
    {
        if (index < 0 || index >= palette.Length || savedIndex == index) return;
        savedIndex = index;
        if (!isEditing) draftIndex = index;
        try
        {
            PlayerPrefs.SetInt(preferenceKey, index);
            PlayerPrefs.Save();
        }
        catch (Exception exception)
        {
            Debug.LogError($"[PlayerAppearanceSettingsComponent] 배정 색상 저장 실패: {exception.Message}");
        }
        changed?.Invoke();
    }

    private void OnDestroy() //공용 참조 정리
    {
        if (instance == this) instance = null;
    }
}
