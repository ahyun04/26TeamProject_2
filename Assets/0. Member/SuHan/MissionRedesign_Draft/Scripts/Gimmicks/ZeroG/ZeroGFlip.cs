using UnityEngine;

namespace TrustNoOne.Gimmicks
{
    /// <summary>
    /// [역할] 무중력 뒤집기 계산 (엔진 없음 — dotnet 테스트 T55). Vector2 · Vector3 의 덧셈 · 곱셈과 System.Math 만 쓴다 (Quaternion 은 엔진 밖에서 못 씀).
    ///  - 뒤집힘 각도: 떠오르기 동안 0 → 180°, 무중력 동안 180°, 되돌기 동안 180° → 0° (처음 · 끝 느리게)
    ///  - 떠오르기 속도: 떠오르기 2초 동안 발이 1 m 올라가게 이번 틱에 위로 더할 속도
    ///  - 화면 기준 조작: 마우스 움직임 · A/D · Shift/Ctrl 을 뒤집힌 각도만큼 돌림
    ///  - 눈 자리: 몸 가운데를 중심으로 앞뒤 축 회전
    ///  - (17d 테스트 후) 방 높이 가운데까지 올라갈 거리 CenterRise — 위 · 아래 면까지 거리에서 (17d 명세 9장 ZG91 · ZG92)
    /// [이유] 각도 · 속도는 사건 일정(ZeroGSchedule)의 단계 · 진행도에서만 나온다 → 호스트와 본인 PC 가 되감아 다시 계산해도,
    ///  다른 사람 화면이 그릴 때도 같은 시각이면 같은 답 (새 [Networked] 없음). 떠오르기 속도는 "지금 높이 − 한 틱 전 높이"라 틱을 다 더하면 정확히 1 m.
    ///  돌리는 방향: 양의 각도 = 앞뒤 축(시선 · 몸 앞) 기준 머리가 왼쪽으로 넘어감 (Unity 의 z 축 회전과 같은 부호 — 카메라 · 몸이 같은 쪽으로 돈다).
    /// [근거] 17c 명세 ZG67 (질문 1 A 시선 축) · ZG68 (질문 2 A 1 m · 2초) · ZG69 (질문 3 A 되돌기 → 중력) · ZG71 · ZG72 · ZG73
    /// </summary>
    public static class ZeroGFlip
    {
        /// <summary>뒤집히는 각도 — 피드백 "몸을 돌려서 맵 상하 반전"</summary>
        public const float FlipDegrees = 180f;

        /// <summary>떠오르는 높이 1 m — 질문 2 A. (17d 테스트 후) 천장 · 바닥을 못 찾을 때만 (ZG92 — 보통은 CenterRise)</summary>
        public const float DefaultRiseHeight = 1f;

        /// <summary>(17d 테스트 후) 가운데를 잴 때 위 · 아래로 훑는 거리 20 m (Claude 제안 — ZG92)</summary>
        public const float CenterProbeRange = 20f;

        /// <summary>(17d 테스트 후) 가운데까지 올라갈 거리의 최대 5 m — 아주 높은 방에서 2초에 너무 빨리 뜨지 않게 (가장 빠를 때 3.75 m/s, Claude 제안 — ZG92)</summary>
        public const float MaxCenterRise = 5f;

        /// <summary>몸 가운데 높이 0.7 m = 팀원 Player 프리팹 SimpleKCC 키 1.4 의 절반 (뒤집는 중심 — 눈 1.3 m 가 발 위 0.1 m 로)</summary>
        public const float DefaultBodyCenterHeight = 0.7f;

        /// <summary>
        /// 부드러운 계단 3p² − 2p³ (처음 · 끝 느리게 — 질문 2 A "천천히"). 0 ~ 1 밖은 자르고 NaN 은 0.
        /// </summary>
        public static float Ease(float progress)
        {
            if (float.IsNaN(progress) || progress <= 0f)
                return 0f;

            if (progress >= 1f)
                return 1f;

            return progress * progress * (3f - 2f * progress);
        }

        /// <summary>뒤집힌 정도 0 ~ 1: 떠오르기 = Ease(진행도) · 무중력 = 1 · 되돌기 = 1 − Ease(진행도) · 그 밖 0 (ZG69 · ZG71)</summary>
        public static float FlipAmount(ZeroGState state)
        {
            switch (state.Phase)
            {
                case ZeroGPhase.Rising: return Ease(state.Progress);
                case ZeroGPhase.Floating: return 1f;
                case ZeroGPhase.Returning: return 1f - Ease(state.Progress);
                default: return 0f;
            }
        }

        /// <summary>뒤집힌 각도(도) = 180 × 뒤집힌 정도</summary>
        public static float RollDegrees(ZeroGState state)
        {
            return FlipDegrees * FlipAmount(state);
        }

        /// <summary>
        /// 떠오른 높이(m, 사건 시작 자리 기준): 경고까지 0 · 떠오르기 = 높이 × Ease(진행도) · 그 뒤 높이 (ZG68).
        /// 높이가 NaN · 0 이하이거나 떠오르기 길이가 0 이면 늘 0 (한 틱에 1 m 를 튀지 않게).
        /// </summary>
        public static float RiseOffset(ZeroGPlan plan, float elapsed, float height)
        {
            if (!plan.Planned || float.IsNaN(height) || height <= 0f)
                return 0f;

            ZeroGTiming timing = ZeroGSchedule.Normalize(plan.Timing);

            if (timing.RiseSeconds <= 0f)
                return 0f;

            ZeroGState state = ZeroGSchedule.Evaluate(plan, elapsed);

            switch (state.Phase)
            {
                case ZeroGPhase.Rising: return height * Ease(state.Progress);
                case ZeroGPhase.Floating:
                case ZeroGPhase.Returning:
                case ZeroGPhase.Ended: return height;
                default: return 0f;
            }
        }

        /// <summary>
        /// 이번 틱에 위로 더할 속도(m/s) = (지금 높이 − 한 틱 전 높이) / 틱 길이 (ZG68). 틱 길이가 0 이하 · NaN 이면 0.
        /// 무중력 이동이 쌓는 관성 속도와 따로 더한다 → 떠오르기가 끝나면 위로 흐르지 않는다.
        /// </summary>
        public static float RiseSpeed(ZeroGPlan plan, float elapsed, float deltaTime, float height)
        {
            if (float.IsNaN(deltaTime) || deltaTime <= 0f)
                return 0f;

            float speed = (RiseOffset(plan, elapsed, height) - RiseOffset(plan, elapsed - deltaTime, height)) / deltaTime;
            return float.IsNaN(speed) || float.IsInfinity(speed) ? 0f : speed;
        }

        /// <summary>
        /// (17d 테스트 후 — ZG91 · ZG92) 몸 가운데를 바닥 ~ 천장 가운데로 옮기려면 올라갈 거리(m).
        /// below = 캡슐 아래 끝에서 바닥까지, above = 캡슐 위 끝에서 천장까지 → 방 = below + 몸 + above 이므로 (above − below) / 2.
        /// 0 ~ MaxCenterRise 로 자른다 (천장 쪽이 더 가까우면 0 = 안 뜸). 둘 중 하나라도 무한 · NaN · 음수(못 찾음)면 fallback (NaN · 음수면 0).
        /// </summary>
        public static float CenterRise(float below, float above, float fallback)
        {
            if (!IsDistance(below) || !IsDistance(above))
                return float.IsNaN(fallback) || fallback < 0f ? 0f : fallback;

            float rise = (above - below) * 0.5f;
            return rise <= 0f ? 0f : rise > MaxCenterRise ? MaxCenterRise : rise;
        }

        private static bool IsDistance(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value) && value >= 0f;
        }

        /// <summary>
        /// 화면 기준 마우스 움직임(x = 오른쪽, y = 위) → 뒤집히기 전 틀 기준 (x = 좌우 돌기, y = 위아래 보기) (ZG72 — 질문 3 A "내 화면 기준").
        /// 180° 이면 둘 다 반대. 각도가 NaN 이면 그대로.
        /// </summary>
        public static Vector2 RotateLook(Vector2 screenDelta, float rollDegrees)
        {
            if (float.IsNaN(rollDegrees))
                return screenDelta;

            Cos(rollDegrees, out float c, out float s);
            return new Vector2(screenDelta.x * c - screenDelta.y * s, screenDelta.x * s + screenDelta.y * c);
        }

        /// <summary>화면 오른쪽 축 (A/D) = cos · 수평 오른쪽 + sin · 세계 위. 180° 이면 수평 왼쪽 (ZG72)</summary>
        public static Vector3 ScreenRight(Vector3 flatRight, Vector3 worldUp, float rollDegrees)
        {
            Cos(rollDegrees, out float c, out float s);
            return flatRight * c + worldUp * s;
        }

        /// <summary>화면 위 축 (Shift = 머리 쪽 · Ctrl = 발 쪽) = −sin · 수평 오른쪽 + cos · 세계 위. 180° 이면 세계 아래 (ZG72)</summary>
        public static Vector3 ScreenUp(Vector3 flatRight, Vector3 worldUp, float rollDegrees)
        {
            Cos(rollDegrees, out float c, out float s);
            return flatRight * -s + worldUp * c;
        }

        /// <summary>
        /// 몸 쪽 자리(루트 기준 — 루트는 좌우로만 돈다)를 center 를 중심으로 앞뒤(z) 축 회전한 자리 (ZG73).
        /// 눈 (0, 1.3, 0) · 가운데 (0, 0.7, 0) · 180° → (0, 0.1, 0). 앞뒤(z) 값은 그대로.
        /// </summary>
        public static Vector3 RollAround(Vector3 localPoint, Vector3 center, float rollDegrees)
        {
            Cos(rollDegrees, out float c, out float s);
            float x = localPoint.x - center.x;
            float y = localPoint.y - center.y;
            return new Vector3(center.x + x * c - y * s, center.y + x * s + y * c, localPoint.z);
        }

        /// <summary>cos · sin. NaN 이면 0° 로 (돌리지 않음). 180° · 90° 는 계산 오차 없이 딱 맞춘다 (뒤집힌 동안 조작이 조금씩 비틀리지 않게)</summary>
        private static void Cos(float degrees, out float c, out float s)
        {
            if (float.IsNaN(degrees) || float.IsInfinity(degrees))
            {
                c = 1f;
                s = 0f;
                return;
            }

            double radians = degrees * System.Math.PI / 180.0;
            c = Snap((float)System.Math.Cos(radians));
            s = Snap((float)System.Math.Sin(radians));
        }

        private static float Snap(float value)
        {
            if (System.Math.Abs(value) < 1e-6f)
                return 0f;

            if (System.Math.Abs(value - 1f) < 1e-6f)
                return 1f;

            return System.Math.Abs(value + 1f) < 1e-6f ? -1f : value;
        }
    }
}
