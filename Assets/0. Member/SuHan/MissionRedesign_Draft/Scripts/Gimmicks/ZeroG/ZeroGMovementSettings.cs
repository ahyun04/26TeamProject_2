using UnityEngine;

namespace TrustNoOne.Gimmicks
{
    /// <summary>
    /// 무중력 이동 설정 에셋 (변수 6개).
    /// [역할] 플레이어 · 관전자 무중력 프리팹 변형이 같은 에셋 하나를 본다. 규칙에는 ToMotion() 값 묶음으로 넘긴다.
    /// [이유] 기획서 9장 "일반 맵과 무중력 맵의 이동 설정을 분리" — 팀원 이동 부품의 값과 완전히 따로 둔다.
    ///  Play 중 Project 창에서 이 에셋을 골라 Inspector 로 바꾸면 바로 반영되고, Play 를 끝내도 값이 남는다 (플레이 테스트로 조절).
    ///  메뉴 SuHan/Build Zero-G Test Scene 은 이미 있는 에셋의 값을 덮어쓰지 않는다.
    /// [근거] 17a 명세 ZG11 · ZG12, 기획서 9장 변수 표
    /// </summary>
    public class ZeroGMovementSettings : ScriptableObject
    {
        [Header("무중력 이동 (기획서 9장 — 단위 m/s · m/s²)")]
        [Tooltip("이동 속도: 키를 누르면 바로 붙는 출발 속도 (걷기 4 m/s 보다 느리게)")]
        [Min(0f)] public float moveSpeed = 2f;

        [Tooltip("이동 가속도: 계속 누를 때 빨라지는 정도 — 0.5 면 3초 동안 1.5 m/s 빨라짐")]
        [Min(0f)] public float acceleration = 0.5f;

        [Tooltip("최대 이동 속도: 계속 눌러도 넘지 않는 속력")]
        [Min(0f)] public float maxSpeed = 3.5f;

        [Tooltip("관성 감속도: 이동 키를 놓았을 때 줄어드는 정도 — 1 이면 2 m/s 에서 2초 뒤 정지")]
        [Min(0f)] public float inertiaDeceleration = 1f;

        [Tooltip("역방향 감속도: 반대 키 · 방향 전환 때 기존 속도가 줄어드는 정도")]
        [Min(0f)] public float reverseDeceleration = 3f;

        [Tooltip("충돌 감속도: 벽 · 천장 · 바닥에 닿았을 때 그쪽 속도가 줄어드는 정도 (아주 크게 하면 바로 멈춤)")]
        [Min(0f)] public float collisionDeceleration = 10f;

        public ZeroGMotionSettings ToMotion()
        {
            return new ZeroGMotionSettings(moveSpeed, acceleration, maxSpeed, inertiaDeceleration, reverseDeceleration, collisionDeceleration);
        }
    }
}
