namespace TrustNoOne.Missions
{
    /// <summary>
    /// [역할] 좌표 설정(TG013)의 판정 규칙 (엔진 없음 — dotnet 테스트 T46). 화면 좌표는 탐색 영역 반지름을 1 로 둔 값이고, 조작 원 가운데는 x · y 각각 −1 ~ 1.
    ///  - StepAxis · ReleasedDirection: 한 축 이동 (끝에서 튕김) · 손을 떼면 다음 방향은 반대
    ///  - Distance · Fill · IsAligned: 목표까지 거리 · 색 채움(가까울수록 1) · 허용 거리 안인가
    ///  - AdvanceStill: "맞춘 채 두 버튼을 모두 뗀" 시간 쌓기 (누르거나 벗어나면 0)
    ///  - BeepInterval: 신호음 간격 (채움 0 → 느리게 · 1 → 빠르게)
    ///  - PickTarget: 목표 자리 뽑기 (가운데에서 일정 거리 이상 · 넓이 고르게 · x · y 모두 0 에서 떨어지게)
    ///  - SeatsReady: 완료해도 되는 담당 상태인가 (초록 · 노랑을 서로 다른 두 사람이 맡음 — 혼자 테스트면 예외)
    /// [이유] 장치(CoordinateStation)는 Fusion 상태 · 표시만 맡고, 숫자 규칙은 엔진 밖에서 시험한다 (작업 규칙). 무작위 값은 밖에서 받아 결과가 정해진다.
    /// [근거] 13b 명세 CO14 (누를 때마다 반대 · 끝에서 튕김), CO15 (목표 반지름 0.45 ~ 0.8), CO16 (채움 · 신호음 간격), CO6 · CO17 (허용 거리 · 1초), CO35,
    ///  CO37 · CO38 (계획 실행 중 보충 — 한 색만으로 · 혼자 번갈아서는 못 깨게, 질문 3 A "혼자서는 못 깸")
    /// </summary>
    public static class CoordinateRules
    {
        /// <summary>한 축이 갈 수 있는 끝 (탐색 영역 반지름 = 1).</summary>
        public const float AxisLimit = 1f;

        // 아주 큰 한 걸음(멈췄던 틱이 몰려 들어온 경우)에도 끝에서 몇 번 튕긴 뒤 범위 안에서 멈추게 하는 횟수
        private const int MaxBounces = 4;

        /// <summary>
        /// 한 축을 direction(±1) 쪽으로 speed × deltaTime 만큼 움직인다. ±1 을 넘으면 넘친 만큼 되돌아오고 방향이 바뀐다 (CO14 — 끝에서 튕김).
        /// </summary>
        public static void StepAxis(ref float position, ref int direction, float speed, float deltaTime)
        {
            if (direction == 0)
                direction = 1;

            position += direction * speed * deltaTime;

            for (int i = 0; i < MaxBounces; i++)
            {
                if (position > AxisLimit)
                {
                    position = 2f * AxisLimit - position;
                    direction = -1;
                }
                else if (position < -AxisLimit)
                {
                    position = -2f * AxisLimit - position;
                    direction = 1;
                }
                else
                {
                    return;
                }
            }

            position = Clamp(position, -AxisLimit, AxisLimit);
        }

        /// <summary>손을 뗐을 때 다음에 누르면 갈 방향 — 지난번과 반대 (CO14: 조금 지나쳤으면 떼었다 다시 눌러 돌아온다).</summary>
        public static int ReleasedDirection(int direction) => direction >= 0 ? -1 : 1;

        public static float Distance(float x, float y, float targetX, float targetY)
        {
            float dx = targetX - x;
            float dy = targetY - y;
            return (float)System.Math.Sqrt(dx * dx + dy * dy);
        }

        /// <summary>색 채움 0 ~ 1 = 1 − 거리 ÷ range (CO16 — 기획서 "가까워질수록 … 색상이 점점 채워진다").</summary>
        public static float Fill(float distance, float range)
        {
            if (range <= 0f)
                return distance <= 0f ? 1f : 0f;

            return Clamp(1f - distance / range, 0f, 1f);
        }

        /// <summary>허용 거리 안인가 (경계 포함 — CO17).</summary>
        public static bool IsAligned(float distance, float tolerance) => distance <= tolerance;

        /// <summary>
        /// 맞춘 채 두 버튼을 모두 뗀 시간을 쌓는다 (CO6). 맞지 않거나 누가 누르고 있으면 0 부터 다시.
        /// </summary>
        public static float AdvanceStill(float seconds, bool aligned, bool anyPressing, float deltaTime)
        {
            return aligned && !anyPressing ? seconds + deltaTime : 0f;
        }

        /// <summary>신호음 간격(초): 채움 0 → slowest, 1 → fastest, 사이는 직선 (CO16 — 기획서 "가까워질수록 … 재생 속도가 빨라진다").</summary>
        public static float BeepInterval(float fill, float slowest, float fastest)
        {
            float t = Clamp(fill, 0f, 1f);
            return slowest + (fastest - slowest) * t;
        }

        /// <summary>
        /// 목표 자리를 뽑는다 (CO15). 가운데(조작 원 시작 자리)에서 minRadius ~ maxRadius 사이 — 시작하자마자 맞는 일이 없게.
        /// 반지름은 넓이가 고르게 퍼지도록 반지름² 사이를 보간한다 (바깥 띠가 넓으니 더 자주 나온다).
        /// (CO37) x · y 모두 0 에서 minAxis 이상 떨어지게 방향을 네 사분면의 가운데 쪽으로만 고른다 — 목표가 한 축 위에 있으면
        ///  조작 원이 처음부터 그 축은 맞아 있어서 한 색만으로 끝나 버린다.
        /// </summary>
        /// <param name="angleRoll">0 ~ 1 무작위 값 → 사분면 · 방향</param>
        /// <param name="radiusRoll">0 ~ 1 무작위 값 → 거리</param>
        public static void PickTarget(float angleRoll, float radiusRoll, float minRadius, float maxRadius, float minAxis, out float x, out float y)
        {
            float r2 = minRadius * minRadius + (maxRadius * maxRadius - minRadius * minRadius) * Clamp(radiusRoll, 0f, 1f);
            float r = (float)System.Math.Sqrt(r2);

            // 사분면 안 각도 범위: |y| = r·sin θ ≥ minAxis 이고 |x| = r·cos θ ≥ minAxis. 불가능하면(반지름이 너무 작음) 45° 로
            float low = r > 0f ? (float)System.Math.Asin(Clamp(minAxis / r, 0f, 1f)) : 0f;
            float high = (float)(System.Math.PI * 0.5) - low;

            if (high < low)
                low = high = (float)(System.Math.PI * 0.25);

            float roll = Clamp(angleRoll, 0f, 0.999999f) * 4f;
            int quadrant = (int)roll;
            float a = quadrant * (float)(System.Math.PI * 0.5) + low + (high - low) * (roll - quadrant);
            x = r * (float)System.Math.Cos(a);
            y = r * (float)System.Math.Sin(a);
        }

        /// <summary>
        /// 완료해도 되는 담당 상태인가 (CO38): 초록 · 노랑을 서로 다른 두 사람이 지금 맡고 있어야 한다 — 혼자 한 색을 맞추고 멀어졌다가(담당 풀림)
        /// 돌아와 다른 색을 맞추는 식으로 혼자 끝내지 못하게. 혼자 테스트(allowSolo)면 늘 true.
        /// </summary>
        public static bool SeatsReady(bool greenSeated, bool yellowSeated, bool sameActor, bool allowSolo)
        {
            return allowSolo || (greenSeated && yellowSeated && !sameActor);
        }

        private static float Clamp(float value, float min, float max) => value < min ? min : value > max ? max : value;
    }
}
