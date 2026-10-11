using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// [에디터 전용, 1클릭 도구] SuhanTest 씬 하나만 넣은 Windows 테스트 빌드를 만든다 (멀티 테스트의 두 번째 창).
///
/// [역할] BuildPipeline.BuildPlayer 에 씬 목록을 직접 넘겨 빌드한다 → 공유 빌드 설정(Build Settings 씬 목록)은 그대로다.
///  출력: 프로젝트/Builds/SuhanTest/SuhanTest.exe (.gitignore 의 /[Bb]uilds/ 로 git 에서 빠진다).
///  개발 빌드라 오류가 빌드 창 구석에 바로 보이고, 로그는 %USERPROFILE%\AppData\LocalLow\DefaultCompany\26TeamProject_2\Player.log
/// [이유] 멀티 테스트는 에디터 + 빌드 1개로 한다 (패키지 설치 없음). 코드 · 씬을 바꾸면 다시 빌드해야 빌드 쪽에 반영된다.
///  특히 설정 메뉴를 다시 실행했다면 반드시 다시 빌드한다 — 씬 오브젝트 구성이 다르면 손님이 장치를 맞추지 못한다.
///  빌드 대상 전환은 프로젝트 전체를 다시 불러와 오래 걸리므로 하지 않고 알리기만 한다.
/// [근거] 6a 명세 MT1 · MT13
/// </summary>
public static class MissionTestBuild
{
    private const string OutputPath = "Builds/SuhanTest/SuhanTest.exe";

    [MenuItem("SuHan/Build Mission Test (Windows)")]
    public static void Build()
    {
        BuildTarget target = EditorUserBuildSettings.activeBuildTarget;

        if (target != BuildTarget.StandaloneWindows64 && target != BuildTarget.StandaloneWindows)
        {
            Debug.LogError($"[MissionTestBuild] 지금 빌드 대상이 {target} 입니다. File > Build Settings 에서 Windows 로 바꾼 뒤 다시 실행하세요 (이 메뉴는 대상을 바꾸지 않습니다).");
            return;
        }

        // 저장 안 한 변경이 빌드에서 빠지지 않게 (취소하면 빌드하지 않는다)
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            return;

        if (!File.Exists(SuhanTestSceneManager.ScenePath))
        {
            Debug.LogError($"[MissionTestBuild] 씬이 없습니다: {SuhanTestSceneManager.ScenePath}");
            return;
        }

        BuildPlayerOptions options = new BuildPlayerOptions
        {
            scenes = new[] { SuhanTestSceneManager.ScenePath },
            locationPathName = OutputPath,
            target = target,
            targetGroup = BuildTargetGroup.Standalone,
            options = BuildOptions.Development,
        };

        BuildReport report = BuildPipeline.BuildPlayer(options);
        BuildSummary summary = report.summary;

        if (summary.result == BuildResult.Succeeded)
        {
            Debug.Log($"[MissionTestBuild] 빌드 성공: {Path.GetFullPath(OutputPath)} ({summary.totalTime.TotalSeconds:0}초, 경고 {summary.totalWarnings}개)");
            EditorUtility.RevealInFinder(OutputPath);
        }
        else
        {
            Debug.LogError($"[MissionTestBuild] 빌드 {summary.result}: 오류 {summary.totalErrors}개 — 위의 빌드 오류 로그를 확인하세요");
        }
    }
}
