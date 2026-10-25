using System.Collections.Generic;
using TrustNoOne.Missions;
using UnityEditor;
using UnityEngine;

/// <summary>
/// [역할 · 에디터 전용, 1클릭 도구] 실제 게임 미션 데이터에 난이도를 넣고, 실제 게임 풀의 난이도별 배정을 켠다 (9b 명세 DF5 · DF8).
///  메뉴 `SuHan/Apply Mission Difficulty`:
///  - 단체 미션 TG001 ~ TG011 중 난이도가 "없음"인 것에만 아래 표 값을 넣는다 (방화문 TG005 는 표에 없음 — "없음"이 맞다)
///  - 실제 게임 풀(4~12 맵이 쓰는 MissionPool_Game)은 한 번도 설정하지 않았을 때(체크 꺼짐 · 개수 모두 0)만 켜고 4 · 2 · 1
/// [이유] 공유 데이터라 메뉴로만 바꾸고, 처음 한 번만 넣는다 — 기획자가 인스펙터에서 바꾼 값은 다시 실행해도 그대로 둔다 (6b SW7 · 7a TC13 과 같은 방식).
///  테스트 씬 데이터는 테스트 설정 메뉴(SoloMissionTestSetup)가 이 표(For)를 읽어 매번 넣는다.
/// [근거] 9b 명세 DF2 (사용자 결정 A — 4~6인 맵 4 · 2 · 1), DF5 (미션 난이도 설정 기획), DF8
/// </summary>
public static class MissionDifficultySetup
{
    // 미션 난이도 설정 기획: 쉬움 = 밸브 · 차단기 · 전선 · 필터, 보통 = 안테나 · 발전기 · 코드, 어려움 = 압력 · 장비 조립 · 생명 유지
    private static readonly Dictionary<string, MissionDifficulty> Table = new Dictionary<string, MissionDifficulty>
    {
        { "TG006", MissionDifficulty.Easy },    // 밸브 잠그기
        { "TG007", MissionDifficulty.Easy },    // 차단기 올리기
        { "TG008", MissionDifficulty.Easy },    // 전선 연결하기
        { "TG011", MissionDifficulty.Easy },    // 필터 청소하기
        { "TG009", MissionDifficulty.Normal },  // 안테나 방향 맞추기
        { "TG001", MissionDifficulty.Normal },  // 발전기 작동시키기
        { "TG002", MissionDifficulty.Normal },  // 코드 순서 맞추기
        { "TG010", MissionDifficulty.Hard },    // 압력 수치 맞추기
        { "TG003", MissionDifficulty.Hard },    // 고장난 장비 조립
        { "TG004", MissionDifficulty.Hard },    // 생명 유지 장치 복구
    };

    // 지금 테스트 맵(4~12)의 기본 개수 — 기획서 4~6인 맵 "쉬움 4 · 보통 2 · 어려움 1" (9b DF2 · 사용자 결정 A)
    public const int DefaultEasy = 4;
    public const int DefaultNormal = 2;
    public const int DefaultHard = 1;

    // 단체 미션 정의 ID 들 (TG001 ~ TG011)
    private const int FirstTeamNumber = 1;
    private const int LastTeamNumber = 11;

    /// <summary>표의 난이도. 표에 없는 ID(방화문 TG005 · 행동 목표 등)는 없음.</summary>
    public static MissionDifficulty For(string id)
    {
        return id != null && Table.TryGetValue(id, out MissionDifficulty difficulty) ? difficulty : MissionDifficulty.None;
    }

    [MenuItem("SuHan/Apply Mission Difficulty")]
    public static void Apply()
    {
        int written = 0;
        int kept = 0;
        int missing = 0;

        for (int number = FirstTeamNumber; number <= LastTeamNumber; number++)
        {
            string id = $"TG{number:000}";
            MissionDifficulty difficulty = For(id);

            if (difficulty == MissionDifficulty.None)
                continue;

            string path = $"{MissionMapInstaller.GameDataFolder}/{id}.asset";
            MissionDefinition definition = AssetDatabase.LoadAssetAtPath<MissionDefinition>(path);

            if (definition == null)
            {
                missing++;
                Debug.LogWarning($"[MissionDifficultySetup] 실제 게임 미션 데이터가 없습니다: {path}");
                continue;
            }

            SerializedObject so = new SerializedObject(definition);
            SerializedProperty property = so.FindProperty("difficulty");

            // 이미 난이도가 있으면(기획자가 정한 값 포함) 그대로 둔다
            if (property.intValue != (int)MissionDifficulty.None)
            {
                kept++;
                continue;
            }

            property.intValue = (int)difficulty;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(definition);
            written++;
        }

        string poolResult = ApplyPoolDefaults();
        AssetDatabase.SaveAssets();

        Debug.Log($"[MissionDifficultySetup] 완료 — 난이도 {written}개 넣음 · {kept}개 이미 있어 그대로 · {missing}개 데이터 없음 (방화문 TG005 는 '없음' 그대로), 실제 게임 풀: {poolResult}");
    }

    /// <summary>실제 게임 풀: 한 번도 설정하지 않았을 때만 난이도별 배정을 켜고 4 · 2 · 1. 결과를 문장으로 돌려준다.</summary>
    private static string ApplyPoolDefaults()
    {
        MissionPool pool = AssetDatabase.LoadAssetAtPath<MissionPool>(MissionMapInstaller.GamePoolPath);

        if (pool == null)
            return $"없음 ({MissionMapInstaller.GamePoolPath})";

        SerializedObject so = new SerializedObject(pool);
        SerializedProperty use = so.FindProperty("useDifficultyCounts");
        SerializedProperty easy = so.FindProperty("easyCount");
        SerializedProperty normal = so.FindProperty("normalCount");
        SerializedProperty hard = so.FindProperty("hardCount");

        bool untouched = !use.boolValue && easy.intValue == 0 && normal.intValue == 0 && hard.intValue == 0;

        if (!untouched)
            return $"이미 설정됨 — 그대로 (난이도별 배정 {(use.boolValue ? "켜짐" : "꺼짐")} · 쉬움 {easy.intValue} · 보통 {normal.intValue} · 어려움 {hard.intValue})";

        use.boolValue = true;
        easy.intValue = DefaultEasy;
        normal.intValue = DefaultNormal;
        hard.intValue = DefaultHard;
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(pool);

        return $"난이도별 배정 켬 · 쉬움 {DefaultEasy} · 보통 {DefaultNormal} · 어려움 {DefaultHard}";
    }
}
