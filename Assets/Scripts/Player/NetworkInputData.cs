using Fusion;
using UnityEngine;


// 플레이어가 사용할 버튼 종류
public enum InputButton
{
    Jump,
    Sprint,
    Crouch
}


// Fusion으로 전달할 플레이어 입력 데이터
public struct NetworkInputData : INetworkInput
{
    // WASD 이동 방향
    public Vector2 MoveDirection;

    // 마우스 시점 회전값
    public Vector2 LookRotation;

    // 점프, 달리기, 앉기 등의 버튼 입력
    public NetworkButtons Buttons;


    // 특정 버튼이 눌려있는지 확인
    public bool IsPressed(InputButton button)
    {
        return Buttons.IsSet(button);
    }
}