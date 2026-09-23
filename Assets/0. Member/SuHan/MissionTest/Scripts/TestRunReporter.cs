using Fusion;
using TrustNoOne.Missions;
using UnityEngine;

/// <summary>
/// [테스트 전용] 로컬 플레이어가 달리기 시작하면 PlayerRan 사실을 알린다 ("한 번도 달리지 않기" 행동 목표 테스트용).
///
/// [왜 플레이어 코드를 고치지 않고 따로 두었나] 실제로는 PlayerMovement 쪽에서 발행하는 게 맞지만,
///  이번 작업은 플레이어/게임 기존 코드를 건드리지 않기로 했다. 그래서 호스트에서 이동 속도를 보고 판단한다.
///
/// [판단 기준] PlayerMovement.CurrentSpeed 가 걷기 속도(4)보다 확실히 빠르면(5 초과) 달리는 중으로 본다
///  (달리기 속도는 6). 달리기 "시작" 순간에만 한 번 알린다 — 매 프레임 보내지 않는다.
/// </summary>
public class TestRunReporter : MonoBehaviour
{
    [SerializeField] private float runSpeedThreshold = 5f;

    private MissionManager manager;
    private bool wasRunning;

    private void Update()
    {
        if (manager == null)
        {
            manager = FindFirstObjectByType<MissionManager>();

            if (manager == null)
                return;
        }

        NetworkObject managerObject = manager.Object;

        if (managerObject == null || !managerObject.IsValid || !managerObject.HasStateAuthority || !manager.Initialized)
            return;

        NetworkRunner runner = manager.Runner;
        PlayerRef localPlayer = runner.LocalPlayer;

        if (!runner.TryGetPlayerObject(localPlayer, out NetworkObject playerObject) || playerObject == null)
            return;

        PlayerMovement movement = playerObject.GetComponent<PlayerMovement>();

        if (movement == null)
            return;

        bool isRunning = movement.CurrentSpeed > runSpeedThreshold;

        if (isRunning && !wasRunning)
        {
            manager.Publish(new MissionEvent(MissionEventType.PlayerRan, localPlayer));
            Debug.Log("[TestRunReporter] 달리기 감지 → PlayerRan 발행");
        }

        wasRunning = isRunning;
    }
}
