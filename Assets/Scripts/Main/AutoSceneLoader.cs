using UnityEngine;
using UnityEngine.SceneManagement;

namespace LockdownProtocol.Lobby
{
    /// <summary>
    /// Main 씬 시작 시 자동으로 다음 씬(로비 메뉴)으로 넘어가게 하는 임시 스크립트.
    /// Main 씬에 실제 타이틀 화면/메인 메뉴가 생기면 이 스크립트는 지우고,
    /// 메인 메뉴의 "시작하기" 버튼 OnClick에서 LoadScene을 직접 호출하면 된다.
    ///
    /// 씬 빌드 인덱스 대신 이름으로 참조한다 — 개발 중엔 인덱스가 자주 바뀌므로
    /// (지금처럼 개인 테스트 씬이 자리를 차지하는 경우 등) 이름 참조가 더 안전하다.
    /// </summary>
    public class AutoSceneLoader : MonoBehaviour
    {
        [Tooltip("Build Settings에 등록된 씬 이름 그대로 입력 (확장자/경로 없이). 예: Lobby_DH_Test, Lobby")]
        [SerializeField] private string targetSceneName = "Lobby";

        [SerializeField] private float delaySeconds = 0f;

        private void Start()
        {
            if (delaySeconds <= 0f)
            {
                LoadScene();
            }
            else
            {
                Invoke(nameof(LoadScene), delaySeconds);
            }
        }

        private void LoadScene()
        {
            SceneManager.LoadScene(targetSceneName);
        }
    }
}