using Fusion;
using LockdownProtocol.Networking;
using UnityEngine;

/// <summary>
/// 탈출 상호작용(E키) 처리.
///
/// 기획서 "탈출 처리 순서": 접근 -> 상호작용 입력 -> 탈출 요청 -> 서버 결과 수신 ->
/// 성공 연출 -> HUD 변경 -> 관전자 화면 전환.
///
/// 로컬 입력 감지(E키, 구역 안에 있는지)는 이 클래스가 하고, 실제 판정은 RPC를 받은
/// State Authority(Host)에서 EscapeValidator + EscapeCapacityManager + EscapeGateManager를
/// 조합해 처리한다. KillManager와 동일한 패턴(로컬 요청 -> 서버 검증 -> 결과 회신)이다.
///
/// 지금은 별도 EscapeManager 없이 이 클래스가 그 역할까지 겸한다 - 판정 로직이 더 복잡해지면
/// 그때 분리해도 된다.
/// </summary>
[RequireComponent(typeof(PlayerHealth))]
[RequireComponent(typeof(KillManager))]
public class EscapeInteractionController : NetworkBehaviour
{
    [SerializeField] private KeyCode interactKey = KeyCode.E;

    private PlayerHealth _health;
    private KillManager _killManager;

    public override void Spawned()
    {
        _health = GetComponent<PlayerHealth>();
        _killManager = GetComponent<KillManager>();
    }

    private void Update()
    {
        if (!Object.HasInputAuthority) return;
        if (_health.IsDead || _health.IsEscaped) return;

        if (FindCurrentZone() != null && Input.GetKeyDown(interactKey))
        {
            RPC_RequestEscape();
        }
    }

    /// <summary>
    /// 지금 서 있는 위치와 겹치는 EscapeZone을 찾는다. 이 결과는 로컬 UI/입력 게이팅용일 뿐이고,
    /// 실제 보안이 걸린 판정은 ProcessEscapeRequest()에서 서버가 다시 확인한다.
    /// </summary>
    private EscapeZone FindCurrentZone()
    {
        var zones = FindObjectsOfType<EscapeZone>();
        foreach (var zone in zones)
        {
            if (zone.IsPlayerInside(Object.InputAuthority))
                return zone;
        }
        return null;
    }

    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    private void RPC_RequestEscape()
    {
        ProcessEscapeRequest();
    }

    /// <summary>실제 판정 로직 본체. RPC와 분리해둔 이유는 테스트 코드에서 직접 호출해 검증할 수 있게 하기 위함.</summary>
    private void ProcessEscapeRequest()
    {
        if (!Object.HasStateAuthority) return;

        // 클라이언트가 보낸 "구역 안에 있다"는 판단을 그대로 믿지 않고, 서버가 다시 확인한다.
        var zone = FindCurrentZone();
        if (zone == null)
        {
            RPC_EscapeFailed(Object.InputAuthority, "구역 밖");
            return;
        }

        var capacityManager = FindObjectOfType<EscapeCapacityManager>();
        var gateManager = FindObjectOfType<EscapeGateManager>();

        var result = EscapeValidator.Validate(
            _health,
            _killManager,
            gateManager != null && gateManager.IsOpen,
            capacityManager);

        if (result != EscapeValidationResult.Success)
        {
            RPC_EscapeFailed(Object.InputAuthority, result.ToString());
            return;
        }

        _health.Escape();
        capacityManager.ConsumeSlot();

        // 탈출이 확정될 때마다 게임 종료 조건(인원 소진/전원 처리 완료)을 검사한다.
        FindObjectOfType<EscapeResultManager>()?.CheckGameEndCondition();

        RPC_EscapeSuccess(Object.InputAuthority);
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_EscapeSuccess([RpcTarget] PlayerRef player)
    {
        Debug.Log("[EscapeInteractionController] 탈출 성공");
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_EscapeFailed([RpcTarget] PlayerRef player, string reason)
    {
        Debug.Log($"[EscapeInteractionController] 탈출 실패: {reason}");
    }
}