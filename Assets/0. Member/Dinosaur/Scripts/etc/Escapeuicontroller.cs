using Fusion;
using UnityEngine;

/// <summary>
/// 탈출 성공 시 간단한 안내 텍스트를 띄운다.
/// 정식 UI 시스템(EscapeHUDController 등)이 나오기 전까지 쓰는 임시 구현이다.
/// 로컬 플레이어(자기 자신)에게만 표시된다.
/// </summary>
[RequireComponent(typeof(PlayerHealth))]
public class EscapeUIController : NetworkBehaviour
{
    private PlayerHealth _health;
    private bool _showEscapedMessage;

    public override void Spawned()
    {
        if (!Object.HasInputAuthority) return;

        _health = GetComponent<PlayerHealth>();
        _health.Escaped += HandleEscaped;
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        if (_health != null)
            _health.Escaped -= HandleEscaped;
    }

    private void HandleEscaped()
    {
        _showEscapedMessage = true;
    }

    // ===== 임시 UI - 정식 UI 시스템(EscapeHUDController 등) 나오면 교체 =====
    private void OnGUI()
    {
        if (!_showEscapedMessage) return;

        var style = new GUIStyle(GUI.skin.label)
        {
            fontSize = 32,
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = Color.white }
        };

        GUI.Label(new Rect(0, Screen.height / 2 - 50, Screen.width, 100), "탈출했습니다!", style);
    }
}