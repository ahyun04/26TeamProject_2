using System.Collections.Generic;
using Fusion;
using UnityEngine;

/// <summary>
/// 탈출 구역. 더 이상 닿기만 하면 자동으로 탈출시키지 않는다 - 기획서 확인 결과
/// "구역 진입 + 상호작용 키 입력"이 필요한 방식이었다(데드바이데이라이트식 2단계 구조가 아님).
///
/// 이 클래스는 "누가 지금 구역 안에 있는지"만 추적한다. 실제 탈출 요청/판정은
/// EscapeInteractionController(Player 프리팹)가 담당한다.
/// </summary>
[RequireComponent(typeof(Collider))]
public class EscapeZone : MonoBehaviour
{
    private readonly HashSet<PlayerRef> _playersInside = new HashSet<PlayerRef>();

    public bool IsPlayerInside(PlayerRef player) => _playersInside.Contains(player);

    private void OnTriggerEnter(Collider other)
    {
        var networkObject = other.GetComponentInParent<NetworkObject>();
        if (networkObject == null) return;

        _playersInside.Add(networkObject.InputAuthority);
    }

    private void OnTriggerExit(Collider other)
    {
        var networkObject = other.GetComponentInParent<NetworkObject>();
        if (networkObject == null) return;

        _playersInside.Remove(networkObject.InputAuthority);
    }
}