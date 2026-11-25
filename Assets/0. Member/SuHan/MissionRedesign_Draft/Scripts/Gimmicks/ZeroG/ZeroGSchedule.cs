namespace TrustNoOne.Gimmicks
{
    /// <summary>무중력 사건의 단계 (17b 명세 ZG52). t = 카운트다운이 끝난 순간 기준.</summary>
    public enum ZeroGPhase
    {
        /// <summary>이번 판 무중력 없음 — 계획이 없거나 판이 짧거나 암전을 피할 자리가 없다 (ZG44 · ZG46)</summary>
        None,
        /// <summary>경고 전 — 평소 이동</summary>
        Waiting,
        /// <summary>경고 문구 + 카운트다운 (t −5 ~ 0) — 아직 평소 이동</summary>
        Warning,
        /// <summary>떠오르기 (t 0 ~ 2) — 무중력 이동 시작, 떠오르며 몸을 180° 돌린다 (17c ZG68)</summary>
        Rising,
        /// <summary>무중력 (t 2 ~ 59) — 뒤집힌 채 떠 있음 (17d 테스트 후: 되돌기가 1초로 짧아져 58 → 59)</summary>
        Floating,
        /// <summary>
        /// 되돌기 (t 59 ~ 60) — 아직 무중력 이동이지만 중력이 켜져, 떨어지면서 몸을 0° 로 되돌린다. 60 (게이지 0) 에 평소 이동 → 끝
        /// (17d 테스트 후 ZG93 · ZG94 — 17c ZG69 "58 ~ 60 떠 있는 채, 60 에 중력" 바뀜)
        /// </summary>
        Returning,
        /// <summary>끝 — 중력 복구 (평소 이동). 한 판에 1번이라 다시 오지 않는다 (ZG44)</summary>
        Ended,
    }

    /// <summary>사건 구간 길이(초): 경고 · 무중력(떠오르기 · 되돌기 포함) · 떠오르기 · 되돌기 (ZG52 · ZG53, 17c ZG69)</summary>
    public readonly struct ZeroGTiming
    {
        public readonly float WarningSeconds;
        public readonly float ZeroGSeconds;
        public readonly float RiseSeconds;
        public readonly float ReturnSeconds;

        public ZeroGTiming(float warningSeconds, float zeroGSeconds, float riseSeconds, float returnSeconds)
        {
            WarningSeconds = warningSeconds;
            ZeroGSeconds = zeroGSeconds;
            RiseSeconds = riseSeconds;
            ReturnSeconds = returnSeconds;
        }

        /// <summary>사건 전체 = 경고 + 무중력 (떠오르기 · 되돌기 모두 무중력 안 — 17c ZG69, 17b 의 "+ 돌아오기" 바뀜). 기본 65초</summary>
        public float Span => WarningSeconds + ZeroGSeconds;

        /// <summary>기본: 경고 5 (피드백 "5초 뒤에") · 무중력 60 (질문 2) · 떠오르기 2 (Claude 제안 — ZG52) · 되돌기 1 (17d 테스트 후 ZG93)</summary>
        public static ZeroGTiming Default => new ZeroGTiming(
            ZeroGSchedule.DefaultWarningSeconds, ZeroGSchedule.DefaultZeroGSeconds, ZeroGSchedule.DefaultRiseSeconds, ZeroGSchedule.DefaultReturnSeconds);
    }

    /// <summary>이번 판 계획: 판 시작 후 몇 초에 경고가 시작되나 + 구간 길이. Planned 가 거짓이면 이번 판 없음.</summary>
    public readonly struct ZeroGPlan
    {
        public readonly bool Planned;
        public readonly float StartOffset;
        public readonly ZeroGTiming Timing;

        public ZeroGPlan(float startOffset, ZeroGTiming timing)
        {
            Planned = true;
            StartOffset = startOffset;
            Timing = timing;
        }

        public static ZeroGPlan None => default;
    }

    /// <summary>
    /// 지금 순간의 사건 상태 (ZG56). SecondsToWarning 은 대기 중에만, Countdown 은 경고 중에만 (5 → 1),
    /// Gauge · SecondsToRestore 는 떠오르기 · 무중력 · 되돌기 중에만, Progress 는 떠오르기 · 되돌기 중에만 (0 → 1) 뜻이 있다 (17c ZG69).
    /// </summary>
    public readonly struct ZeroGState
    {
        public readonly ZeroGPhase Phase;
        public readonly float SecondsToWarning;
        public readonly int Countdown;
        public readonly float Gauge;
        public readonly float SecondsToRestore;
        public readonly float Progress;

        public ZeroGState(ZeroGPhase phase, float secondsToWarning, int countdown, float gauge, float secondsToRestore, float progress)
        {
            Phase = phase;
            SecondsToWarning = secondsToWarning;
            Countdown = countdown;
            Gauge = gauge;
            SecondsToRestore = secondsToRestore;
            Progress = progress;
        }

        public static ZeroGState Of(ZeroGPhase phase) => new ZeroGState(phase, 0f, 0, 0f, 0f, 0f);
    }

    /// <summary>
    /// [역할] 무중력 사건의 시간 규칙 (엔진 없음 — dotnet 테스트 T54).
    ///  - Pick: 이번 판 경고 시작 시각 — 판 끝 전에 중력 복구까지 끝나고, 암전과 앞뒤로 떨어진 곳에서 고르게
    ///  - Evaluate: 판 시작 후 지난 초 → 단계 · 카운트다운 숫자 · 게이지 · 진행도
    /// [이유] 호스트는 계획만 정해 나누고, 모든 PC 가 같은 계획 · 같은 틱 시각으로 같은 답을 계산한다 (암전 BlackoutSchedule 과 같은 틀).
    ///  이동을 바꾸는 순간(t = 0 · 60)도 이 답에서 나오므로 모든 PC 가 같은 틱에 바꾼다. 무작위 값은 밖에서 받는다.
    /// [근거] 2차 피드백 "경고 문구 뜬 후에 5초 카운트 다운" · "무중력 시작할때부터 게이지 … 중력 복구 상태로 돌아오기까지" ·
    ///  사용자 결정 질문 2 · 4 · 7 — 17b 명세 ZG44 · ZG46 · ZG49 · ZG52 ~ ZG56,
    ///  (17d 테스트 후) 되돌기 1초 · 그 동안 중력 (UsesGravity — 17d 명세 9장 ZG93 · ZG94)
    /// </summary>
    public static class ZeroGSchedule
    {
        /// <summary>첫 대기 120초 — 질문 2 A "판 시작 120초 뒤 랜덤한 때" (암전과 같은 값)</summary>
        public const float DefaultFirstDelay = 120f;

        /// <summary>경고 · 카운트다운 5초 — 피드백 "5초 뒤에 무중력 상태에 들어갑니다"</summary>
        public const float DefaultWarningSeconds = 5f;

        /// <summary>무중력 60초 — 질문 2 A (게이지 길이)</summary>
        public const float DefaultZeroGSeconds = 60f;

        /// <summary>떠오르기 2초 — Claude 제안 (피드백 "천천히", ZG52)</summary>
        public const float DefaultRiseSeconds = 2f;

        /// <summary>
        /// 되돌기 1초 — 무중력 끝 1초, 그 처음부터 중력 (17d 테스트 후 질문 2 B — ZG93. 17c ZG69 의 2초 바뀜).
        /// ⚠️ 씬에 놓인 관리자는 Inspector 값이 저장돼 있어 이 값이 바뀌어도 그대로 — 테스트 씬은 메뉴를 다시 돌린다 (ZG98)
        /// </summary>
        public const float DefaultReturnSeconds = 1f;

        /// <summary>암전과 떨어질 거리 10초 — 질문 4 A</summary>
        public const float DefaultBlackoutGap = 10f;

        /// <summary>이상한 길이를 고친다: 음수 · NaN → 0, 떠오르기가 무중력보다 길면 무중력 길이로, 되돌기는 무중력에서 떠오르기를 뺀 만큼까지 (17c ZG69)</summary>
        public static ZeroGTiming Normalize(ZeroGTiming timing)
        {
            float zeroG = NonNegative(timing.ZeroGSeconds);
            float rise = NonNegative(timing.RiseSeconds);

            float back = NonNegative(timing.ReturnSeconds);

            if (rise > zeroG)
                rise = zeroG;

            if (back > zeroG - rise)
                back = zeroG - rise;   // 17c: 되돌기도 무중력 안 — 떠오르기와 겹치지 않게

            return new ZeroGTiming(NonNegative(timing.WarningSeconds), zeroG, rise, back);
        }

        /// <summary>
        /// 이번 판 계획을 뽑는다 (ZG54). blackout 은 같은 판 시작 기준으로 옮긴 암전 계획 (없으면 BlackoutPlan.None).
        /// 고를 수 있는 구간 [첫 대기, 판 길이 − 사건 전체] 에서 금지 구간 (암전 시작 − 사건 전체 − 거리, 암전 끝 + 거리) 을 빼고,
        /// 남은 조각(최대 2개)을 이어 붙여 startRoll(0 ~ 1)로 한 점을 고른다 → 길이에 비례해 고르게. 경계는 허용 (정확히 거리만큼 떨어짐).
        /// </summary>
        public static ZeroGPlan Pick(float roundLength, float firstDelay, ZeroGTiming timing, BlackoutPlan blackout, float blackoutGap, float startRoll)
        {
            timing = Normalize(timing);

            if (timing.ZeroGSeconds <= 0f)
                return ZeroGPlan.None;

            float earliest = NonNegative(firstDelay);
            float latest = roundLength - timing.Span;   // 판이 끝나기 전에 중력 복구까지 끝나게 (암전 KB3 와 같은 생각)

            if (float.IsNaN(latest) || latest < earliest)
                return ZeroGPlan.None;

            // 조각 1 [earliest, end1] · 조각 2 [start2, latest] — 암전이 없으면 조각 1 만
            float end1 = latest;
            float start2 = float.PositiveInfinity;

            if (blackout.Planned && blackout.Duration > 0f)
            {
                float gap = NonNegative(blackoutGap);
                float holeStart = blackout.StartOffset - timing.Span - gap;
                float holeEnd = blackout.StartOffset + blackout.Duration + gap;
                end1 = Min(latest, holeStart);
                start2 = Max(earliest, holeEnd);
            }

            bool has1 = end1 >= earliest;
            bool has2 = start2 <= latest;

            if (!has1 && !has2)
                return ZeroGPlan.None;

            float length1 = has1 ? end1 - earliest : 0f;
            float length2 = has2 ? latest - start2 : 0f;
            float x = Clamp01(startRoll) * (length1 + length2);
            float start;

            if (has1 && (x <= length1 || !has2))
                start = earliest + Min(x, length1);
            else
                start = start2 + Max(0f, x - length1);

            return new ZeroGPlan(start, timing);
        }

        /// <summary>
        /// 판 시작 후 elapsed 초일 때의 상태 (ZG52 · ZG56). t = elapsed − 경고 시작 − 경고 길이 (카운트다운이 끝난 순간 0):
        ///  t &lt; −경고 대기 · t &lt; 0 경고 (숫자 = 남은 초 올림) · t &lt; 떠오르기 떠오르기 · t &lt; 무중력 − 되돌기 무중력 · t &lt; 무중력 되돌기 · 그 뒤 끝 (17c ZG69).
        ///  게이지 = (무중력 − t) / 무중력 — 0 에서 가득, 60 에서 0 (되돌기 동안에도 줄어든다).
        /// </summary>
        public static ZeroGState Evaluate(ZeroGPlan plan, float elapsed)
        {
            if (!plan.Planned)
                return ZeroGState.Of(ZeroGPhase.None);

            ZeroGTiming timing = Normalize(plan.Timing);

            if (timing.ZeroGSeconds <= 0f)
                return ZeroGState.Of(ZeroGPhase.None);

            float t = elapsed - plan.StartOffset - timing.WarningSeconds;

            if (float.IsNaN(t))
                return ZeroGState.Of(ZeroGPhase.Waiting);

            if (t < -timing.WarningSeconds)
                return new ZeroGState(ZeroGPhase.Waiting, -timing.WarningSeconds - t, 0, 0f, 0f, 0f);

            if (t < 0f)
                return new ZeroGState(ZeroGPhase.Warning, 0f, (int)System.Math.Ceiling(-t), 0f, 0f, 0f);

            float gauge = (timing.ZeroGSeconds - t) / timing.ZeroGSeconds;

            if (t < timing.RiseSeconds)
                return new ZeroGState(ZeroGPhase.Rising, 0f, 0, gauge, timing.ZeroGSeconds - t, t / timing.RiseSeconds);

            float returnStart = timing.ZeroGSeconds - timing.ReturnSeconds;   // 17c: 되돌기는 무중력 끝 (Normalize 가 떠오르기와 겹치지 않게 함)

            if (t < returnStart)
                return new ZeroGState(ZeroGPhase.Floating, 0f, 0, gauge, timing.ZeroGSeconds - t, 1f);

            if (t < timing.ZeroGSeconds)
                return new ZeroGState(ZeroGPhase.Returning, 0f, 0, gauge, timing.ZeroGSeconds - t, (t - returnStart) / timing.ReturnSeconds);

            return ZeroGState.Of(ZeroGPhase.Ended);
        }

        /// <summary>이 단계에서 무중력 이동을 쓰나 — 떠오르기 · 무중력 · 되돌기 (경고 · 끝은 평소 이동 — ZG57, 17c ZG69 되돌기 더함)</summary>
        public static bool UsesZeroGMovement(ZeroGPhase phase)
        {
            return phase == ZeroGPhase.Rising || phase == ZeroGPhase.Floating || phase == ZeroGPhase.Returning;
        }

        /// <summary>(17d 테스트 후) 무중력 이동 중 중력을 켜는 단계 — 되돌기만 (떨어지면서 화면이 돌아온다 — ZG93 · ZG94)</summary>
        public static bool UsesGravity(ZeroGPhase phase)
        {
            return phase == ZeroGPhase.Returning;
        }

        /// <summary>게이지 글자용 "분:초" — 남은 초를 올림 (41.3 → "0:42"). 음수 · NaN → "0:00" (ZG56)</summary>
        public static string FormatClock(float seconds)
        {
            int whole = float.IsNaN(seconds) || seconds <= 0f ? 0 : (int)System.Math.Ceiling(seconds);
            return $"{whole / 60}:{whole % 60:00}";
        }

        private static float NonNegative(float value)
        {
            return float.IsNaN(value) || value < 0f ? 0f : value;
        }

        private static float Clamp01(float value)
        {
            return float.IsNaN(value) || value < 0f ? 0f : value > 1f ? 1f : value;
        }

        private static float Min(float a, float b) => a < b ? a : b;

        private static float Max(float a, float b) => a > b ? a : b;
    }
}
