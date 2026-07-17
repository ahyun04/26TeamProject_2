using Fusion;
using System;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class MainMenuManager : MonoBehaviour
{
    [Header("로비 UI")]
    [SerializeField] private TMP_InputField roomNameInput;
    [SerializeField] private Button createRoomButton;
    [SerializeField] private Button joinRoomButton;
    [SerializeField] private TMP_Text statusText;

    [Header("방 설정")]
    [SerializeField, Min(1)]
    private int maxPlayers = 8;

    [Header("씬 설정")]
    [SerializeField]
    private int SceneBuildIndex = 1;

    private NetworkRunner runner;
    private NetworkSceneManagerDefault sceneManager;

    private bool isStarting;

    public NetworkRunner Runner => runner;

    private void Awake()
    {
        DontDestroyOnLoad(gameObject);
    }

    public async void CreateRoom()
    {
        await StartSession(GameMode.Host);
    }

    public async void JoinRoom()
    {
        await StartSession(GameMode.Client);
    }

    private async Task StartSession(GameMode gameMode)
    {
        if (isStarting || runner != null)
            return;

        string roomName = roomNameInput.text.Trim();

        if (string.IsNullOrWhiteSpace(roomName))
        {
            SetStatus("방 이름을 입력하세요.");
            return;
        }

        isStarting = true;
        SetButtonsInteractable(false);
        SetStatus("서버에 연결 중입니다.");

        runner = gameObject.AddComponent<NetworkRunner>();
        sceneManager =
            gameObject.AddComponent<NetworkSceneManagerDefault>();

        runner.ProvideInput = true;

        SceneRef currentScene = SceneRef.FromIndex(
            SceneManager.GetActiveScene().buildIndex);

        try
        {
            StartGameResult result =
                await runner.StartGame(
                    new StartGameArgs
                    {
                        GameMode = gameMode,
                        SessionName = roomName,
                        PlayerCount = maxPlayers,

                        Scene = currentScene,
                        SceneManager = sceneManager,

                        IsOpen = true,
                        IsVisible = true,

                        EnableClientSessionCreation = false
                    });

            if (result.Ok)
            {
                string message =
                    gameMode == GameMode.Host
                        ? $"방 생성 성공: {roomName}"
                        : $"방 참가 성공: {roomName}";

                SetStatus(message);
                Debug.Log(message);

                if (gameMode == GameMode.Host)
                {
                    LoadLobbyScene();
                }

                return;
            }

            HandleStartFailure(
                result.ShutdownReason,
                result.ErrorMessage);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            SetStatus("네트워크 연결 중 오류가 발생했습니다.");

            DestroyRunner();
        }
        finally
        {
            isStarting = false;

            if (runner == null)
                SetButtonsInteractable(true);
        }
    }

    private void HandleStartFailure(
        ShutdownReason reason,
        string errorMessage)
    {
        Debug.LogError(
            $"방 연결 실패\n" +
            $"원인: {reason}\n" +
            $"내용: {errorMessage}");

        SetStatus($"방 연결 실패: {reason}");

        DestroyRunner();
    }

    private void DestroyRunner()
    {
        if (runner != null)
        {
            Destroy(runner);
            runner = null;
        }

        if (sceneManager != null)
        {
            Destroy(sceneManager);
            sceneManager = null;
        }
    }

    private void SetButtonsInteractable(bool interactable)
    {
        if (createRoomButton != null)
            createRoomButton.interactable = interactable;

        if (joinRoomButton != null)
            joinRoomButton.interactable = interactable;
    }

    private void SetStatus(string message)
    {
        if (statusText != null)
            statusText.text = message;
    }

    private void LoadLobbyScene()
    {
        if (runner == null ||
            !runner.IsRunning ||
            !runner.IsSceneAuthority)
        {
            return;
        }

        if (SceneBuildIndex < 0 ||
            SceneBuildIndex >=
            SceneManager.sceneCountInBuildSettings)
        {
            Debug.LogError(
                "Lobby 씬의 Build Index가 올바르지 않습니다.");

            return;
        }

        runner.LoadScene(
            SceneRef.FromIndex(SceneBuildIndex),
            LoadSceneMode.Single);
    }
}