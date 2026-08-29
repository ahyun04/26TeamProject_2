using UnityEngine;


/// <summary>
/// 전선 미션에서 사용하는 4가지 전선 색상 정의
/// WiringMission, WireStartPoint, WireEndPoint가 같은 색상 기준을 공유하기 위해 사용한다
/// </summary>
public enum WireColor
{
    Red,
    Blue,
    Green,
    Yellow
}


/// <summary>
/// WireColor 값을 Unity의 실제 Color 값으로 변환하는 도우미 클래스
/// 전선 시작점 Renderer와 LineRenderer가 같은 색상을 사용하도록 변환 규칙을 한 곳에서 관리한다
/// </summary>
public static class WireColorUtility
{
    public static Color ToUnityColor(WireColor color)
    {
        switch (color)
        {
            case WireColor.Red:
                return Color.red;

            case WireColor.Blue:
                return Color.blue;

            case WireColor.Green:
                return Color.green;

            case WireColor.Yellow:
                return Color.yellow;

            default:
                return Color.white;
        }
    }
}