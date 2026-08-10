using Fusion;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using System.Threading.Tasks;


[RequireComponent(typeof(NetworkRunner))]
[RequireComponent(typeof(NetworkSceneManagerDefault))]
public class LobbyNetworkManager : MonoBehaviour
{
    [SerializeField] private TMP_InputField roomNameInput;

    // BuildSetting 에 씬 번호임 (변경하면 이것도 수정해야함)
    private int lobbySceneIndex = 1;
    private int standBySceneIndex = 2;
    
    private int maxPlayers = 8;

    private NetworkRunner runner;
    private NetworkSceneManagerDefault sceneManager;

    private bool isStarting;
    private bool isReturning;


    private void Awake()
    {
        // 씬 이동하면 이전 씬에 NetworkRunner 오브젝트가 사라지는데
        // 사라지게 되면 Photon 방 연결도 유지되지 않기 때문에
        // 유지하기 위해 DontDestroyOnLoad(gameObject) 작성함 
        DontDestroyOnLoad(gameObject);

        runner = GetComponent<NetworkRunner>();
        sceneManager = GetComponent<NetworkSceneManagerDefault>();
    }


    // async void == 실행시키고, 끝났는지 신경 못 씀
    // async Task == 실행시키고, 끝날 때까지 기다릴 수도 있음
    public async void CreateRoom()
    {
        await StartRoom(GameMode.Host);
    }

    public async void JoinRoom()
    {
        await StartRoom(GameMode.Client);
    }


    // 방이름 확인, 방생성, 접속요청, 실패처리 담당 함수
    private async Task StartRoom(GameMode mode)
    {
        if (isStarting) return;

        // InputField에 입력된 글자를 가져옴
        string roomName = roomNameInput.text.Trim();

        // 방 이름이 비었는지 검사
        if (string.IsNullOrEmpty(roomName))
        {
            Debug.LogWarning("방 이름을 입력하세요.");
            return;
        }

        // 접속 시작 상태로 변경
        isStarting = true;

        // runner.StartGame 는 Photon Fusion에 네트워크 게임을 시작하라는 뜻
        // StartGameArgs 는 설정값 묶음
        StartGameResult result = await runner.StartGame(new StartGameArgs
        {
            GameMode = mode,
            SessionName = roomName,
            PlayerCount = maxPlayers,

            Scene = SceneRef.FromIndex(standBySceneIndex),
            SceneManager = sceneManager,

            // 클라이언트가 존재하지 않는 방에 접속하려고 했을 때 새 방을 만들지 못하게 함
            EnableClientSessionCreation = false
        });

        // 접속 실패 시
        if (!result.Ok)
        {
            Debug.LogError($"접속 실패 : {result.ShutdownReason}");

            Destroy(gameObject);
            SceneManager.LoadScene(lobbySceneIndex);
        }
    }


    // 방을 나가고 Lobby로 돌아가는 함수
    public async void ReturnToLobby()
    {
        if (isReturning) return;

        isReturning = true;

        // Photon 세션 연결과 NetworkRunner를 정상적으로 종료
        await runner.Shutdown(false);

        Destroy(gameObject);
        SceneManager.LoadScene(lobbySceneIndex);
    }
}

