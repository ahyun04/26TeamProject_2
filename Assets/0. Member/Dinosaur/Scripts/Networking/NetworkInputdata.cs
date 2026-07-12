using Fusion;
using UnityEngine;

namespace LockdownProtocol.Networking
{
    /// <summary>
    /// 버튼 입력을 비트 플래그로 구분하기 위한 인덱스.
    /// NetworkButtons는 내부적으로 비트마스크로 저장되므로, bool 여러 개보다 훨씬 적은 대역폭을 사용한다.
    /// </summary>
    public static class InputButton
    {
        public const int Jump = 0;
        public const int Sprint = 1;
        public const int Crouch = 2;
        public const int Interact = 3;
        public const int Attack = 4;
    }

    /// <summary>
    /// 한 틱(Tick) 동안의 플레이어 입력을 담는 데이터 컨테이너.
    /// 이 구조체는 로직을 갖지 않는다 (순수 데이터). 로컬 클라이언트가 값을 채우고,
    /// Fusion이 이를 직렬화해 State Authority(Host)로 전송하면,
    /// PlayerMovement가 FixedUpdateNetwork()에서 이 값을 소비해 실제 이동을 계산한다.
    /// </summary>
    public struct NetworkInputData : INetworkInput
    {
        /// <summary>WASD 등 수평 이동 입력. X = 좌우, Y = 전후.</summary>
        public Vector2 MoveDirection;

        /// <summary>마우스 시점 회전. X = Yaw(좌우), Y = Pitch(상하).</summary>
        public Vector2 LookRotation;

        /// <summary>Jump, Sprint, Crouch, Interact, Attack 등을 비트로 압축해서 담는다.</summary>
        public NetworkButtons Buttons;

        /// <summary>버튼이 눌려있는지 조회하는 헬퍼. 호출부에서 비트 인덱스를 직접 다루지 않도록 캡슐화.</summary>
        public bool IsPressed(int buttonIndex) => Buttons.IsSet(buttonIndex);
    }
}