using Fusion;
using UnityEngine;

public class GameResultUI : MonoBehaviour
{
    [SerializeField] private GameEndSystem gameEndSystem; //기존 게임 종료 판정
    [SerializeField] private RoleAssignment roleAssignment; //본인 역할 배정
    [SerializeField] private GameResultFeedbackComponent resultFeedback; //씬 전환 동안 유지할 결과 연출

    private void OnEnable() //기존 게임 종료 알림과 표시 진입점 연결
    {
        if (gameEndSystem == null) gameEndSystem = FindFirstObjectByType<GameEndSystem>();
        if (roleAssignment == null) roleAssignment = FindFirstObjectByType<RoleAssignment>();
        if (gameEndSystem == null) return;
        gameEndSystem.OnGameEnded += handleGameEnded;
        if (gameEndSystem.Object != null && gameEndSystem.Object.IsValid && gameEndSystem.IsGameEnded)
            handleGameEnded();
    }

    private void OnDisable() //씬 교체 시 종료 알림 구독 해제
    {
        if (gameEndSystem != null) gameEndSystem.OnGameEnded -= handleGameEnded;
    }

    private void handleGameEnded() //본인 결과가 준비되면 자동 복귀 전 화면부터 가리기
    {
        if (gameEndSystem == null || gameEndSystem.Object == null || !gameEndSystem.Object.IsValid ||
            roleAssignment == null || roleAssignment.Object == null || !roleAssignment.Object.IsValid ||
            roleAssignment.Runner != gameEndSystem.Runner) return;
        PlayerRef localPlayer = gameEndSystem.Runner.LocalPlayer; //이 화면의 참가자
        if (!roleAssignment.TryGetRole(localPlayer, out PlayerRole role)) return;
        FindFirstObjectByType<NetworkBootstrap>()?.prepareGameResult(gameEndSystem.Runner, role,
            gameEndSystem.GetPlayerResult(localPlayer));
    }

    internal GameResultFeedbackComponent prepareResult(NetworkRunner runner, PlayerRole role, PlayerResult result) //외부에서는 결과 표시 진입점만 호출
    {
        return resultFeedback != null && resultFeedback.prepare(runner, role, result) ? resultFeedback : null;
    }
}
