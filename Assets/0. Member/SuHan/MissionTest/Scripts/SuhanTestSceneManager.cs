using System;
using Fusion;

/// <summary>
/// [테스트 전용] SuhanTest 씬을 "경로 기반 장면 번호"로 알려 주는 Fusion 씬 관리자.
///
/// [역할] GetSceneRef 하나만 바꾼다. SuhanTest 면 항상 SceneRef.FromPath(ScenePath), 아니면 Fusion 기본 동작.
///  나머지(이미 열린 씬 넘겨받기 → 씬에 놓인 네트워크 오브젝트 등록)는 기본 씬 관리자 그대로라,
///  실제 게임과 같은 등록 경로(NetworkSceneManagerDefault.OnSceneLoaded → Runner.RegisterSceneObjects)를 탄다.
/// [이유] 기본 동작은 빌드 번호 → 주소 지정(addressables) 순서로 장면을 찾는다. SuhanTest 는 공유 빌드 목록에 없어
///  에디터에선 번호가 없고(−1 → 장면 없음), SuhanTest 하나만 넣은 테스트 빌드 안에선 0번이다.
///  기본 동작이면 에디터 호스트와 빌드 손님이 서로 다른 장면으로 알아, 손님이 열린 씬을 넘겨받지 못하고
///  씬 오브젝트(미션 장치)를 맞추지 못한다. 경로를 먼저 보면 양쪽 값이 같다.
/// [근거] 6a 명세 MT2 (사용자 결정 A — 빌드 목록은 건드리지 않는다)
/// </summary>
public class SuhanTestSceneManager : NetworkSceneManagerDefault
{
    public const string ScenePath = "Assets/0. Member/SuHan/SuhanTest.unity";

    // 씬 이름만으로 물어 오는 경우도 받는다
    private const string SceneName = "SuhanTest";

    public override SceneRef GetSceneRef(string sceneNameOrPath)
    {
        // 들어온 문자열이 아니라 항상 같은 문자열로 만들어, 에디터 · 빌드 · 호스트 · 손님 모두 같은 값이 되게 한다
        if (IsTestScene(sceneNameOrPath))
            return SceneRef.FromPath(ScenePath);

        return base.GetSceneRef(sceneNameOrPath);
    }

    public static bool IsTestScene(string sceneNameOrPath)
    {
        return string.Equals(sceneNameOrPath, ScenePath, StringComparison.OrdinalIgnoreCase)
            || string.Equals(sceneNameOrPath, SceneName, StringComparison.OrdinalIgnoreCase);
    }
}
