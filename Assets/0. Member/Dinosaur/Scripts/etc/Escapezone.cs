using Fusion;
using UnityEngine;

/// <summary>
/// 탈출 구역. 플레이어가 이 트리거에 들어오면 State Authority(Host)가 PlayerHealth.Escape()를 호출한다.
///
/// 정식 탈출 시스템(EscapeManager, EscapeGateManager, EscapeCapacityManager, EscapeValidator 등)이
/// 갖춰지기 전까지 쓰는 단순화 버전이다. 탈출구 개방 조건(시간/미션), 탈출 가능 인원, 역할 체크는
/// 아직 검사하지 않는다 - "죽지 않았고 아직 탈출하지 않은 사람이 구역에 들어오면 무조건 탈출"로 단순화했다.
/// </summary>
[RequireComponent(typeof(Collider))]
public class EscapeZone : MonoBehaviour
{
    private NetworkRunner _runner;

    private void Start()
    {
        // 씬에 하나뿐인 NetworkRunner를 찾아 캐싱한다. 이 스크립트는 NetworkBehaviour가 아니라
        // Object.HasStateAuthority에 직접 접근할 수 없으므로, Runner.IsServer로 대신 판단한다.
        _runner = FindObjectOfType<NetworkRunner>();
    }

    private void OnTriggerEnter(Collider other)
    {
        if (_runner == null || !_runner.IsServer) return;

        // SimpleKCC는 실제 충돌 콜라이더를 런타임에 별도 자식 오브젝트로 만들 수 있으므로,
        // 부모 계층까지 올라가며 PlayerHealth를 찾는다.
        var health = other.GetComponentInParent<PlayerHealth>();
        if (health == null) return;
        if (health.IsDead || health.IsEscaped) return;

        health.Escape();
        Debug.Log($"[EscapeZone] {other.name} 탈출 처리 완료");
    }
}