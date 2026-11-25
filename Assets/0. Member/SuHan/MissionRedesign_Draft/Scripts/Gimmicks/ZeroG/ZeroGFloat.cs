using UnityEngine;

namespace TrustNoOne.Gimmicks
{
    /// <summary>
    /// 발밑 면 한 틱 결과 (ZeroGFloat.StepFloor).
    /// [역할] 이동 부품(ZeroGMovement)이 [Networked] 닿은 시간 · 이번 이동에 더할 밀기 · 관성에 넘길 속도를 한 번에 받는다.
    /// [근거] 17d 명세 ZG83 ~ ZG85
    /// </summary>
    public readonly struct ZeroGFloorStep
    {
        /// <summary>새 닿은 시간(초) — [Networked] 에 쓴다</summary>
        public readonly float Touch;

        /// <summary>이번 틱에 화면 위로 더할 밀기 속도(m/s) — 관성에 쌓지 않는다</summary>
        public readonly float Push;

        /// <summary>이번 틱에 관성(velocity)에 넘길 화면 위 속도(m/s) — 밀기를 놓는 틱에만 0 이 아니다</summary>
        public readonly float Handover;

        public ZeroGFloorStep(float touch, float push, float handover)
        {
            Touch = touch;
            Push = push;
            Handover = handover;
        }
    }

    /// <summary>
    /// [역할] 무중력 "떠 있는 느낌" 계산 (엔진 없음 — dotnet 테스트 T56). Vector3 의 덧셈 · 내적과 System.Math 만 쓴다.
    ///  - 흔들림 (보이는 것만): 가만히 있을 때(0.3 m/s 이하) 화면 위아래 ±4 cm · 3초 주기 사인. 움직이면 1초에 걸쳐 사라지고, 멈추면 1초에 걸쳐 생긴다
    ///  - 발밑 면 밀기: 무중력 단계에 발밑 면(화면 아래)에서 5 cm 안에 3초 넘게 있으면 화면 위로 초당 0.5 씩(최대 0.5 m/s) 민다.
    ///    밀기 시작 뒤에는 면에서 1 m 넘게 떨어지면 놓고, 놓는 틱에 밀던 속도를 관성에 넘긴다.
    ///    Ctrl · 멈춤이면 0 (넘기지 않음), 무중력 단계를 벗어나면 0 (밀던 속도는 관성에)
    /// [이유] 닿은 시간 하나만 [Networked] 로 두고 밀기 속도는 거기서 계산한다 → 호스트와 본인 PC 가 되감아 다시 계산해도 같은 답.
    ///  밀기를 관성에 바로 쌓지 않는 까닭: 이동 키가 없을 때 관성 감속 1 m/s² 가 밀기 0.5 m/s² 보다 커서 쌓으면 매 틱 깎여 늘 0 이 된다
    ///  → 떠오르기 속도(17c ZG68)처럼 이번 이동에만 따로 더하고, 놓을 때 관성에 넘겨 서서히 멈추게 한다.
    ///  흔들림은 그리기에만 쓰여 네트워크 값이 없다 (실제 위치 · 충돌체 그대로).
    /// [근거] 17d 명세 ZG81 ~ ZG85 (질문 1 C — 1 m 에서 놓기, 질문 2 A — 무중력 단계만), 17b 명세 ZG47 · ZG48
    ///  (피드백 "가만히 있을 때 위아래로 살짝살짝씩 움직여서 동동 떠있는 느낌" · "바닥에 3초이상 닿아 있으면 점점 위로 올라가게")
    /// </summary>
    public static class ZeroGFloat
    {
        /// <summary>흔들림 크기 ±4 cm — 17b ZG47 (Inspector Bob Amplitude 의 처음 값)</summary>
        public const float BobAmplitude = 0.04f;

        /// <summary>흔들림 한 번(위 → 아래 → 위)의 길이(초) — 17b ZG47 "약 3초"</summary>
        public const float BobPeriod = 3f;

        /// <summary>이 속력(m/s) 이하면 가만히 있는 것으로 본다 — 17b ZG47</summary>
        public const float BobStillSpeed = 0.3f;

        /// <summary>흔들림 세기가 0 ↔ 1 로 바뀌는 시간(초) — "서서히 사라짐" (Claude 제안 — ZG81)</summary>
        public const float BobFadeSeconds = 1f;

        /// <summary>발밑 면에 이만큼(초) 넘게 닿으면 밀기 시작 — 피드백 "3초이상"</summary>
        public const float TouchSeconds = 3f;

        /// <summary>밀기가 초마다 늘어나는 양(m/s²) — 17b ZG48 "매초 0.5 씩"</summary>
        public const float PushRamp = 0.5f;

        /// <summary>가장 센 밀기(m/s) — 17b ZG48 "최대 0.5 m/s"</summary>
        public const float PushMax = 0.5f;

        /// <summary>발밑 면이 이 거리(m) 안이면 닿은 것으로 센다 (Claude 제안 — ZG83)</summary>
        public const float TouchDistance = 0.05f;

        /// <summary>밀기 시작 뒤 발밑 면이 이 거리(m) 넘게 떨어지면 놓는다 — 질문 1 C</summary>
        public const float ReleaseDistance = 1f;

        /// <summary>닿은 시간 상한(초) = 밀기가 최대가 되는 때 (3 + 0.5 / 0.5 = 4). 좁은 곳에서 끝없이 늘지 않게 (Claude 제안 — ZG83)</summary>
        public const float TouchLimit = TouchSeconds + PushMax / PushRamp;

        /// <summary>발밑 면으로 보는 기울기 한계 cos 45° — 화면 위를 45° 안으로 보는 면만 (벽은 빼고 — Claude 제안 ZG83)</summary>
        public const float FloorCos = 0.70710677f;

        /// <summary>사람마다 흔들림 시작점을 고르게 흩는 수 (황금비의 소수 부분 — Claude 제안 ZG81)</summary>
        private const double GoldenStep = 0.6180339887498949;

        /// <summary>가만히 있나 — 속력이 0.3 m/s 이하. NaN 은 아님</summary>
        public static bool IsStill(float speed)
        {
            return !float.IsNaN(speed) && speed <= BobStillSpeed;
        }

        /// <summary>
        /// 흔들림 세기(0 ~ 1)를 한 프레임 옮긴다: 가만히 = 1 쪽, 움직임 = 0 쪽으로 fadeSeconds 에 걸쳐.
        /// fadeSeconds 가 0 이하 · NaN 이면 바로 목표로, deltaTime 이 음수 · NaN 이면 그대로. 세기 NaN 은 0.
        /// </summary>
        public static float StepBobWeight(float weight, bool still, float fadeSeconds, float deltaTime)
        {
            weight = float.IsNaN(weight) ? 0f : System.Math.Max(0f, System.Math.Min(1f, weight));
            float target = still ? 1f : 0f;

            if (float.IsNaN(deltaTime) || deltaTime < 0f)
                return weight;

            if (float.IsNaN(fadeSeconds) || fadeSeconds <= 0f)
                return target;

            float step = deltaTime / fadeSeconds;
            return weight < target ? System.Math.Min(target, weight + step) : System.Math.Max(target, weight - step);
        }

        /// <summary>
        /// 흔들림 높이(m) = amplitude × sin(2π × (time / period + phase)). 오래 켜 두어도 어긋나지 않게 한 바퀴 단위로 자른 뒤 계산한다.
        /// period 0 이하 · NaN, 다른 값 NaN · 무한이면 0.
        /// </summary>
        public static float BobOffset(float time, float phase, float amplitude, float period)
        {
            if (float.IsNaN(time) || float.IsInfinity(time) || float.IsNaN(phase) || float.IsInfinity(phase)
                || float.IsNaN(amplitude) || float.IsInfinity(amplitude) || float.IsNaN(period) || period <= 0f)
                return 0f;

            double cycles = (double)time / period + phase;
            cycles -= System.Math.Floor(cycles);
            return amplitude * (float)System.Math.Sin(cycles * 2.0 * System.Math.PI);
        }

        /// <summary>사람마다 흔들림 시작점 0 ~ 1 (NetworkId.Raw × 황금비의 소수 부분 — 이웃한 번호끼리 멀리 떨어짐)</summary>
        public static float PhaseFor(uint id)
        {
            double p = id * GoldenStep;
            return (float)(p - System.Math.Floor(p));
        }

        /// <summary>발밑 면인가 — 면의 법선이 화면 위 축과 45° 안. 길이 0 · NaN 이면 아님</summary>
        public static bool IsFloorSurface(Vector3 normal, Vector3 screenUp)
        {
            float n = normal.magnitude;
            float u = screenUp.magnitude;

            if (!(n > 1e-6f) || !(u > 1e-6f) || float.IsInfinity(n) || float.IsInfinity(u))
                return false;

            return Vector3.Dot(normal, screenUp) / (n * u) >= FloorCos - 1e-6f;
        }

        /// <summary>밀기 속도(m/s) = min(0.5, 0.5 × (닿은 시간 − 3)). 3초 이하 · NaN 이면 0</summary>
        public static float PushSpeed(float touchSeconds)
        {
            if (float.IsNaN(touchSeconds) || touchSeconds <= TouchSeconds)
                return 0f;

            return System.Math.Min(PushMax, PushRamp * (touchSeconds - TouchSeconds));
        }

        /// <summary>
        /// 발밑 면 한 틱 (ZG83 ~ ZG85).
        /// touch = 지난 닿은 시간, floating = 지금 무중력 단계인가, holdingDown = Ctrl · 멈춤을 누르고 있나,
        /// distance = 발밑 면까지 거리(m — 없으면 무한), deltaTime = 틱 길이(초).
        ///  ① Ctrl · 멈춤 → 0 · 밀기 없음 · 넘기지 않음
        ///  ② 무중력 단계가 아님 → 0 · 밀던 속도를 관성에
        ///  ③ 밀기 전(3초 미만)에는 5 cm 안, 밀기 시작 뒤(3초 이상)에는 1 m 안이면 시간을 더함(최대 4초) — 밖이면 0 · 밀던 속도를 관성에
        /// </summary>
        public static ZeroGFloorStep StepFloor(float touch, bool floating, bool holdingDown, float distance, float deltaTime)
        {
            if (float.IsNaN(touch) || float.IsInfinity(touch) || touch < 0f)
                touch = 0f;

            if (holdingDown)
                return new ZeroGFloorStep(0f, 0f, 0f);

            if (!floating)
                return new ZeroGFloorStep(0f, 0f, PushSpeed(touch));

            if (float.IsNaN(deltaTime) || deltaTime <= 0f)
                return new ZeroGFloorStep(touch, PushSpeed(touch), 0f);

            float reach = touch >= TouchSeconds ? ReleaseDistance : TouchDistance;

            if (float.IsNaN(distance) || distance > reach)
                return new ZeroGFloorStep(0f, 0f, PushSpeed(touch));

            float next = System.Math.Min(touch + deltaTime, TouchLimit);
            return new ZeroGFloorStep(next, PushSpeed(next), 0f);
        }
    }
}
