using System.Collections;
using Fusion;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

public class GameResultFeedbackComponent : MonoBehaviour
{
    [SerializeField] private CanvasGroup overlay; //씬 로딩부터 유지하는 검은 화면
    [SerializeField] private TMP_Text resultText; //본인 역할과 승패 한 문구
    [SerializeField, Min(0f)] private float holdSeconds = 1.25f; //대기실 준비 후 결과 유지 시간
    [SerializeField, Min(0.01f)] private float fadeSeconds = 1.5f; //결과와 검은 배경이 사라지는 시간

    private static GameResultFeedbackComponent instance; //클라이언트 하나의 종료 화면
    private NetworkRunner runner; //이번 결과가 속한 세션
    private string resultMessage; //씬 파괴 전 보관한 확정 결과
    private Coroutine resultRoutine; //대기실 준비와 페이드 처리
    internal static bool blocksPlayerInput => instance != null; //종료 연출 중 로컬 입력 차단

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void resetInstance() //에디터 재실행 시 정적 참조 초기화
    {
        instance = null;
    }

    internal bool prepare(NetworkRunner source, PlayerRole role, PlayerResult result) //씬 전환 전에 본인 결과와 검은 화면 보관
    {
        if (overlay == null || resultText == null || source == null || !source.IsRunning ||
            (role != PlayerRole.Citizen && role != PlayerRole.Killer) ||
            (result != PlayerResult.Win && result != PlayerResult.Lose)) return false;
        if (instance == this) return runner == source;
        if (instance != null) instance.cancel();

        runner = source;
        resultMessage = (role == PlayerRole.Killer ? "살인마" : "시민") +
            (result == PlayerResult.Win ? " 승리" : " 패배");
        resultText.text = string.Empty;
        overlay.alpha = 1f;
        instance = this;
        transform.SetParent(null, false);
        DontDestroyOnLoad(gameObject);
        gameObject.SetActive(true);
        SceneManager.sceneLoaded += handleSceneLoaded;
        return true;
    }

    internal void showAfterSceneLoad(NetworkRunner source, string sceneName) //Fusion의 대기실 로딩 완료를 표시로 연결
    {
        if (instance != this || runner != source || resultRoutine != null) return;
        resultRoutine = StartCoroutine(showResult(sceneName));
    }

    private IEnumerator showResult(string sceneName) //대기실 카메라 준비 전에는 완전한 암전 유지
    {
        while (!isCameraReady(sceneName))
        {
            if (runner == null || !runner.IsRunning || SessionDisconnectUIComponent.IsOpen)
            {
                cancel();
                yield break;
            }
            yield return null;
        }
        resultText.text = resultMessage;
        float elapsed = 0f; //결과 문구를 표시한 시간
        while (elapsed < holdSeconds)
        {
            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }
        elapsed = 0f;
        while (elapsed < fadeSeconds)
        {
            elapsed += Time.unscaledDeltaTime;
            overlay.alpha = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / fadeSeconds));
            yield return null;
        }
        resultRoutine = null;
        cancel();
    }

    private bool isCameraReady(string sceneName) //본인 플레이어와 실제 대기실 시점이 준비되었는지 확인
    {
        if (runner == null || !runner.IsRunning || runner.SceneManager == null ||
            runner.SceneManager.MainRunnerScene.name != sceneName) return false;
        NetworkObject player = runner.GetPlayerObject(runner.LocalPlayer); //새로 생성한 대기실 플레이어
        Transform listener = PlayerCameraController.LocalListenerTransform; //새 대기실 시점
        return player != null && player.IsValid && listener != null && listener.gameObject.activeInHierarchy &&
            listener.IsChildOf(player.transform);
    }

    private void handleSceneLoaded(Scene scene, LoadSceneMode mode) //로비 이탈 등 다른 경로에서 검은 화면 잔류 방지
    {
        if (scene.name == "Main" || scene.name == "Lobby") cancel();
    }

    internal void cancel() //완료·퇴장·연결 실패 시 종료 화면과 입력 차단 해제
    {
        if (resultRoutine != null) StopCoroutine(resultRoutine);
        resultRoutine = null;
        if (instance == this) instance = null;
        SceneManager.sceneLoaded -= handleSceneLoaded;
        if (overlay != null) overlay.alpha = 0f;
        gameObject.SetActive(false);
        Destroy(gameObject);
    }

    private void OnDestroy() //세션 제거와 플레이 종료 시 정적 상태와 이벤트 정리
    {
        if (instance == this) instance = null;
        SceneManager.sceneLoaded -= handleSceneLoaded;
    }
}
