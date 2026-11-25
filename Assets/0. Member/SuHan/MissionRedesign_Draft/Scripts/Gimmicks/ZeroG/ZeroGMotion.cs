using UnityEngine;

namespace TrustNoOne.Gimmicks
{
    /// <summary>
    /// 무중력 이동 설정 값 묶음 (기획서 9장 변수 6개 — 단위 m/s · m/s²).
    /// [역할] 규칙(ZeroGMotion)이 받는 숫자. 에셋(ZeroGMovementSettings)이 이 묶음으로 바꿔 넘긴다.
    /// [이유] 규칙을 엔진 밖에서 테스트하려고 ScriptableObject 대신 값 묶음으로 받는다.
    /// [근거] 17a 명세 ZG11 · ZG12
    /// </summary>
    public readonly struct ZeroGMotionSettings
    {
        /// <summary>이동 속도 — 키를 누르면 바로 붙는 출발 속도</summary>
        public readonly float MoveSpeed;
        /// <summary>이동 가속도 — 계속 누를 때 빨라지는 정도</summary>
        public readonly float Acceleration;
        /// <summary>최대 이동 속도 — 넘지 않는 속력</summary>
        public readonly float MaxSpeed;
        /// <summary>관성 감속도 — 이동 키가 없을 때 줄어드는 정도</summary>
        public readonly float InertiaDeceleration;
        /// <summary>역방향 감속도 — 반대 키 · 방향 전환 때 줄어드는 정도</summary>
        public readonly float ReverseDeceleration;
        /// <summary>충돌 감속도 — 닿은 면 쪽 속도가 줄어드는 정도</summary>
        public readonly float CollisionDeceleration;

        public ZeroGMotionSettings(float moveSpeed, float acceleration, float maxSpeed,
            float inertiaDeceleration, float reverseDeceleration, float collisionDeceleration)
        {
            MoveSpeed = moveSpeed;
            Acceleration = acceleration;
            MaxSpeed = maxSpeed;
            InertiaDeceleration = inertiaDeceleration;
            ReverseDeceleration = reverseDeceleration;
            CollisionDeceleration = collisionDeceleration;
        }

        /// <summary>
        /// 기본값 (17a 명세 ZG11 — 사용자 승인한 제안 값). 걷기 4 m/s 보다 느리게 (기획서 3장 "일반 맵보다 느린 이동 속도"):
        /// 2 m/s 에서 놓으면 2초 · 2 m, 최대에서 3.5초 · 6 m 미끄러짐, 2 m/s 에서 반대 키 약 1.3초 뒤 반대로 2 m/s (0 을 지나 끊김 없이 — ZG41), 벽 쪽 약 0.35초.
        /// </summary>
        public static ZeroGMotionSettings Default => new ZeroGMotionSettings(2f, 0.5f, 3.5f, 1f, 3f, 10f);

        /// <summary>음수 · NaN 은 0, 최대 속도가 이동 속도보다 작으면 최대 = 이동 속도 (잘못 넣은 Inspector 값으로 이상하게 움직이지 않게).</summary>
        public ZeroGMotionSettings Sanitized()
        {
            float move = NonNegative(MoveSpeed);
            return new ZeroGMotionSettings(move, NonNegative(Acceleration), Mathf.Max(NonNegative(MaxSpeed), move),
                NonNegative(InertiaDeceleration), NonNegative(ReverseDeceleration), NonNegative(CollisionDeceleration));
        }

        private static float NonNegative(float value) => float.IsNaN(value) || value < 0f ? 0f : value;
    }

    /// <summary>
    /// 무중력 이동 규칙 (엔진 밖에서 테스트하는 순수 계산 — Vector3 계산만 쓴다).
    /// [역할] 이번 틱의 속도 = 지난 속도 + 입력 방향 + 멈춤 + 지난 틱에 막힌 속도 → 새 속도 (ZG10 순서 ① ~ ⑤).
    /// [이유] 호스트와 본인 PC 가 되감아 다시 계산해도 같은 답이 나오게, 입력과 [Networked] 값만으로 정한다.
    /// [근거] 17a 명세 ZG4 · ZG5 · ZG6 · ZG9 · ZG10 · ZG15 · ZG41 (테스트 후 — 반대 키에서 멈추지 않게), 기획서 3 · 4 · 5 · 7장,
    ///  17d 명세 ZG86 (닿음 글자를 화면 기준으로) · 9장 ZG94 (되돌기 동안 위아래를 빼는 Horizontal)
    /// </summary>
    public static class ZeroGMotion
    {
        /// <summary>이보다 느리면 0 — 아주 느리게 끝없이 흐르는 것을 막는다 (ZG10 ⑤). 이동 키를 누르는 동안은 끊지 않고 누른 쪽으로 이만큼 남긴다 (ZG41)</summary>
        public const float StopSpeed = 0.05f;

        /// <summary>막힌 속도가 이보다 작으면 닿지 않은 것으로 본다 (실제 움직임의 계산 오차 — ZG15)</summary>
        public const float BlockedEpsilon = 0.01f;

        private const float InputEpsilon = 1e-6f;

        /// <summary>
        /// 누른 키 → 세계 방향 (ZG9). W/S = 바라보는 방향(위아래 기울기 포함), A/D = 바라보는 방향의 오른쪽(수평),
        /// Shift/Ctrl = 세계 위 · 아래. 합친 길이가 1 을 넘으면 1 로 (대각선 · 세 방향을 함께 눌러도 더 빠르지 않게).
        /// </summary>
        public static Vector3 WishDirection(Vector3 lookForward, Vector3 flatRight, float forwardInput, float rightInput, float upInput)
        {
            return WishDirection(lookForward, flatRight, Vector3.up, forwardInput, rightInput, upInput);
        }

        /// <summary>
        /// (17c) 화면 기준 축으로 (ZG72): A/D = rightAxis, Shift/Ctrl = upAxis — 뒤집히면 ZeroGFlip.ScreenRight · ScreenUp
        /// (180° 이면 A/D 반대, Shift = 머리 쪽 = 세계 아래). W/S 는 그대로 시선 방향. 0° 이면 위 WishDirection 과 같다.
        /// </summary>
        public static Vector3 WishDirection(Vector3 lookForward, Vector3 rightAxis, Vector3 upAxis, float forwardInput, float rightInput, float upInput)
        {
            Vector3 wish = lookForward.normalized * forwardInput + rightAxis.normalized * rightInput + upAxis.normalized * upInput;
            float length = wish.magnitude;

            if (float.IsNaN(length) || length < InputEpsilon)
                return Vector3.zero;

            return length > 1f ? wish / length : wish;
        }

        /// <summary>
        /// 한 틱 계산 (ZG10). wish 는 WishDirection 결과, blocked 는 지난 틱의 Blocked 결과, dt 는 틱 길이(초).
        /// </summary>
        public static Vector3 Step(Vector3 velocity, Vector3 wish, bool halt, Vector3 blocked, ZeroGMotionSettings settings, float dt)
        {
            // ① 멈춤: 속도 0, 이동 키 무시. 풀려도 예전 속도는 되살리지 않는다 (기획서 7장 "이전 이동 속도나 관성은 복구하지 않음")
            if (halt)
                return Vector3.zero;

            if (!IsFinite(velocity))
                velocity = Vector3.zero;

            if (float.IsNaN(dt) || dt <= 0f)
                return velocity;

            ZeroGMotionSettings s = settings.Sanitized();
            Vector3 v = velocity;

            bool hasInput = IsFinite(wish) && wish.sqrMagnitude >= InputEpsilon;
            Vector3 direction = hasInput ? wish.normalized : Vector3.zero;

            if (!hasInput)
            {
                // ② 이동 키 없음: 방향은 그대로, 속력만 관성 감속도로 줄인다 (4장 "서서히 감소하며 정지")
                v = Shrink(v, s.InertiaDeceleration * dt);
            }
            else
            {
                // ③ 이동 키 있음: 누른 방향 성분 p 와 나머지 성분으로 나눈다
                float p = Vector3.Dot(v, direction);
                Vector3 rest = v - direction * p;

                if (v.sqrMagnitude < InputEpsilon)
                    p = s.MoveSpeed;                                     // 완전히 멈춰 있으면 바로 출발 속도로 (계속 가속형 — ZG4)
                else if (p < s.MoveSpeed)
                    // 반대쪽 · 옆 · 느린 흐름: 역방향 감속도로 0 을 지나 이동 속도까지 이어서 늘린다 (4장 "감소한 후 반대 방향으로").
                    // 예전에는 0 까지 줄인 뒤 다음 틱에 이동 속도로 튀어, 멈췄다 출발하는 느낌이었다 (QA — 사용자 결정 A, ZG41)
                    p = Mathf.Min(p + s.ReverseDeceleration * dt, s.MoveSpeed);
                else
                    p = Mathf.Min(p + s.Acceleration * dt, s.MaxSpeed);  // 계속 누르면 빨라짐 (최대에서 멈춤)

                // 옆으로 흐르던 속도도 역방향 감속도로 줄인다 — 빠를수록 방향 전환이 오래 걸린다 (4장, Claude 제안 — ZG10)
                v = direction * p + Shrink(rest, s.ReverseDeceleration * dt);
            }

            // ④ 지난 틱에 막힌 면 쪽으로 들어가는 성분만 충돌 감속도로 줄인다. 면을 따라가는 성분은 그대로 (5장 · ZG5)
            if (IsFinite(blocked) && blocked.magnitude >= BlockedEpsilon)
            {
                Vector3 normal = blocked.normalized;
                float into = Vector3.Dot(v, normal);

                if (into > 0f)
                    v -= normal * (into - Mathf.Max(0f, into - s.CollisionDeceleration * dt));
            }

            // ⑤ 최대 속도에서 자르고, 아주 느리면 0
            float speed = v.magnitude;

            if (speed > s.MaxSpeed)
                v = v / speed * s.MaxSpeed;

            if (v.magnitude >= StopSpeed)
                return v;

            // 이동 키를 누르고 있으면 0 으로 끊지 않고 누른 쪽으로 StopSpeed 를 남긴다 (ZG41) —
            // 0 이 되면 다음 틱이 "멈춰 있음"으로 보여 이동 속도로 튄다 (반대 키로 0 을 지날 때 · 벽에 대고 누를 때)
            return hasInput ? direction * Mathf.Min(StopSpeed, s.MaxSpeed) : Vector3.zero;
        }

        /// <summary>막힌 속도 = 넣은 속도 − 실제로 움직인 속도 (ZG15). 차이가 BlockedEpsilon 보다 작으면 0 (안 닿음).</summary>
        public static Vector3 Blocked(Vector3 intended, Vector3 actual)
        {
            Vector3 blocked = intended - actual;
            return IsFinite(blocked) && blocked.magnitude >= BlockedEpsilon ? blocked : Vector3.zero;
        }

        /// <summary>(17d 테스트 후 — ZG94) 세계 위아래 성분을 뺀 수평 벡터 (되돌기 동안 위아래는 중력만). NaN · 무한이면 0</summary>
        public static Vector3 Horizontal(Vector3 v)
        {
            return IsFinite(v) ? new Vector3(v.x, 0f, v.z) : Vector3.zero;
        }

        /// <summary>테스트 글자용 (ZG21): 막힌 방향이 거의 세계 위면 천장, 거의 아래면 바닥, 나머지는 벽 (= 화면 위 축이 세계 위 — 뒤집히지 않았을 때).</summary>
        public static string DescribeContact(Vector3 blocked)
        {
            return DescribeContact(blocked, Vector3.up);
        }

        /// <summary>
        /// (17d) 화면 기준 닿음 (ZG86): 막힌 방향이 거의 화면 위(머리 쪽)면 천장, 거의 화면 아래(발 쪽)면 바닥, 나머지는 벽.
        /// 뒤집히면 screenUp = ZeroGFlip.ScreenUp (180° 이면 세계 아래 → 원래 천장에 막히면 "바닥"). screenUp 이 0 · NaN 이면 세계 위.
        /// </summary>
        public static string DescribeContact(Vector3 blocked, Vector3 screenUp)
        {
            if (!IsFinite(blocked) || blocked.magnitude < BlockedEpsilon)
                return "없음";

            Vector3 axis = IsFinite(screenUp) && screenUp.sqrMagnitude > InputEpsilon ? screenUp.normalized : Vector3.up;
            float up = Vector3.Dot(blocked.normalized, axis);

            if (up > 0.7f)
                return "천장";

            return up < -0.7f ? "바닥" : "벽";
        }

        /// <summary>
        /// 날 때 앞으로 숙이는 각도 (17a 테스트 후 추가 — ZG34). 수평 속력이 startSpeed 이하면 0°, fullSpeed 이상이면 maxAngle, 사이는 비례.
        /// [이유] 날기 클립(Fly_move)에 엉덩이 뼈 회전이 없어 몸이 서 있는 채로 허우적거린다 → 움직일 때만 코드로 숙인다 (사용자 결정 C).
        ///  정지(Fly_idle)는 숙이지 않는다. 시작 ≥ 끝이면 끝 속력에서 딱 끊는다. NaN · 음수 속력은 0°.
        /// </summary>
        public static float BodyLeanAngle(float horizontalSpeed, float startSpeed, float fullSpeed, float maxAngle)
        {
            if (float.IsNaN(horizontalSpeed) || float.IsNaN(maxAngle) || horizontalSpeed <= 0f)
                return 0f;

            if (fullSpeed <= startSpeed)
                return horizontalSpeed >= fullSpeed ? maxAngle : 0f;

            return maxAngle * Mathf.Clamp01((horizontalSpeed - startSpeed) / (fullSpeed - startSpeed));
        }

        private static Vector3 Shrink(Vector3 v, float amount)
        {
            float length = v.magnitude;

            if (length <= amount || length < InputEpsilon)
                return Vector3.zero;

            return v * ((length - amount) / length);
        }

        private static bool IsFinite(Vector3 v)
        {
            return !(float.IsNaN(v.x) || float.IsNaN(v.y) || float.IsNaN(v.z)
                     || float.IsInfinity(v.x) || float.IsInfinity(v.y) || float.IsInfinity(v.z));
        }
    }
}
