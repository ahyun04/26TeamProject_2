using Fusion;
using UnityEngine;

/// <summary>
/// 로컬 클라이언트의 키보드/마우스 입력을 매 프레임(Update) 누적하고,
/// Fusion이 요청하는 시점(NetworkBootstrap.OnInput)에 NetworkInputData로 패킹해 반환한다.
///
/// 이 클래스는 순수 로컬 로직이다. State Authority 여부, 네트워크 상태를 전혀 신경 쓰지 않는다.
/// Runner.ProvideInput = true인 클라이언트에서만 이 값이 실제로 소비된다.
///
/// InputButton은 팀원이 정의한 enum(Jump, Sprint, Crouch)을 그대로 따른다.
/// NetworkButtons.Set()은 int를 받으므로, enum 값은 (int)로 형변환해서 넘긴다.
/// </summary>
public class PlayerInputHandler : MonoBehaviour
{
    [Header("Look Sensitivity")]
    [SerializeField] private float lookSensitivity = 2f;

    // 마우스 델타는 Unity Update()가 Fusion 시뮬레이션 틱보다 더 자주 실행될 수 있어
    // 프레임마다 누적해뒀다가 GatherInput() 호출 시점에 한 번에 소비하고 초기화한다.
    // 그렇지 않으면 틱 사이의 마우스 움직임이 유실되어 시점 회전이 끊겨 보인다.
    private Vector2 _accumulatedLookDelta;

    // GetKeyDown은 눌린 그 프레임에만 true이므로, 그 프레임에 틱이 없으면 입력이 유실된다.
    // 따라서 소비(GatherInput) 시점까지 상태를 누적/보존한다.
    private NetworkButtons _accumulatedButtons;

    private void Update()
    {
        _accumulatedLookDelta.x += Input.GetAxisRaw("Mouse X") * lookSensitivity;
        _accumulatedLookDelta.y += Input.GetAxisRaw("Mouse Y") * lookSensitivity;

        if (Input.GetKeyDown(KeyCode.Space))
            _accumulatedButtons.Set((int)InputButton.Jump, true);
    }

    /// <summary>
    /// NetworkBootstrap.OnInput()에서 호출된다.
    /// 누적된 "순간 버튼" 입력은 여기서 소비 후 초기화하고,
    /// 이동/시점처럼 지속 상태인 값은 이 시점에 새로 읽는다.
    /// </summary>
    public NetworkInputData GatherInput()
    {
        var data = new NetworkInputData
        {
            MoveDirection = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical")),
            LookRotation = _accumulatedLookDelta,
            Buttons = _accumulatedButtons
        };

        // Held 상태(누르는 동안 계속 true)는 매 틱 새로 읽어도 유실 위험이 없다.
        data.Buttons.Set((int)InputButton.Sprint, Input.GetKey(KeyCode.LeftShift));
        data.Buttons.Set((int)InputButton.Crouch, Input.GetKey(KeyCode.LeftControl));

        // 누적값 소비 완료 - 다음 틱을 위해 초기화
        _accumulatedLookDelta = Vector2.zero;
        _accumulatedButtons = default;

        return data;
    }
}