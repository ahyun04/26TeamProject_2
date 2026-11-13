namespace TrustNoOne.Gimmicks
{
    /// <summary>살인마 전용 암전의 단계 (16a 명세 KB16).</summary>
    public enum BlackoutPhase
    {
        /// <summary>이번 판 암전 없음 — 계획이 없거나 판이 너무 짧다 (KB3)</summary>
        None,
        /// <summary>시작 전</summary>
        Waiting,
        /// <summary>발동 순간부터 어두워지는 시간 동안 — 어두움이 0 → 1 로 일정하게 커진다 (KB6)</summary>
        FadingIn,
        /// <summary>완전히 어두움</summary>
        Dark,
        /// <summary>끝남 — 한 판에 1번이라 다시 오지 않는다 (KB2)</summary>
        Ended,
    }

    /// <summary>이번 판의 암전 계획: 판 시작 후 몇 초에 시작해서 몇 초 동안. Planned 가 거짓이면 이번 판 없음.</summary>
    public readonly struct BlackoutPlan
    {
        public readonly bool Planned;
        public readonly float StartOffset;
        public readonly float Duration;

        public BlackoutPlan(float startOffset, float duration)
        {
            Planned = true;
            StartOffset = startOffset;
            Duration = duration;
        }

        /// <summary>이번 판 암전 없음</summary>
        public static BlackoutPlan None => default;
    }

    /// <summary>지금 순간의 암전 상태. SecondsToStart 는 대기 중에만, SecondsLeft 는 어두워지는 중 · 암전 중에만 뜻이 있다.</summary>
    public readonly struct BlackoutState
    {
        public readonly BlackoutPhase Phase;
        public readonly float Darkness;
        public readonly float SecondsToStart;
        public readonly float SecondsLeft;

        public BlackoutState(BlackoutPhase phase, float darkness, float secondsToStart, float secondsLeft)
        {
            Phase = phase;
            Darkness = darkness;
            SecondsToStart = secondsToStart;
            SecondsLeft = secondsLeft;
        }
    }

    /// <summary>
    /// [역할] 살인마 전용 암전의 시간 규칙 (엔진 없음 — dotnet 테스트 T48).
    ///  - Pick: 이번 판 계획 뽑기 — 길이 = 최소 ~ 최대 사이, 시작 = 첫 대기 ~ (판 길이 − 길이) 사이. 구간이 없으면 없음
    ///  - Evaluate: 판 시작 후 지난 초 → 단계 · 어두움 0 ~ 1
    /// [이유] 호스트는 계획만 정해 나누고, 모든 PC 가 같은 계획 · 같은 시각으로 같은 답을 계산한다 (KB9 · KB14).
    ///  숫자 규칙을 한 곳에 모으고 엔진 밖에서 시험한다 (작업 규칙 — 판정 규칙은 순수 클래스). 무작위 값은 밖에서 받는다.
    /// [근거] 기획서 "게임 시작 후 2분 동안은 없음, 2분 경과 이후 랜덤 시점에 발동" · "10~15초 랜덤 유지 후 자동 해제" ·
    ///  "점점 화면이 어두워진다" + "즉시 암전 효과가 적용된다" — 16a 명세 KB2 · KB3 · KB4 · KB6 · KB15 · KB16 · KB17
    /// </summary>
    public static class BlackoutSchedule
    {
        /// <summary>첫 대기 120초 — 기획서 "게임 시작 후 2분 동안은 없음" (KB17)</summary>
        public const float DefaultFirstDelay = 120f;

        /// <summary>지속 시간 10 ~ 15초 — 기획서 "10~15초 랜덤 유지" (KB4 · KB17)</summary>
        public const float DefaultMinDuration = 10f;
        public const float DefaultMaxDuration = 15f;

        /// <summary>어두워지는 시간 1초 — 사용자 결정 A (질문 4, KB6)</summary>
        public const float DefaultFadeSeconds = 1f;

        /// <summary>씬에 GameTimer 가 없을 때의 판 길이 500초 — GamePlay · 4~12 씬의 gameDurationSeconds 와 같은 값 (KB12)</summary>
        public const float DefaultRoundLength = 500f;

        /// <summary>
        /// 이번 판 계획을 뽑는다 (KB15). durationRoll · startRoll 은 0 ~ 1 무작위 값 (호스트가 UnityEngine.Random 으로 넣는다).
        /// 이상한 입력은 고쳐 쓴다: 음수 → 0, 최소 > 최대 → 바꿈, 무작위 값 → 0 ~ 1 로 자름. 길이가 0 이하면 없음.
        /// </summary>
        public static BlackoutPlan Pick(
            float roundLength, float firstDelay, float minDuration, float maxDuration, float durationRoll, float startRoll)
        {
            if (firstDelay < 0f)
                firstDelay = 0f;

            if (minDuration < 0f)
                minDuration = 0f;

            if (maxDuration < 0f)
                maxDuration = 0f;

            if (minDuration > maxDuration)
            {
                float swap = minDuration;
                minDuration = maxDuration;
                maxDuration = swap;
            }

            float duration = Lerp(minDuration, maxDuration, Clamp01(durationRoll));

            if (duration <= 0f)
                return BlackoutPlan.None;

            // KB3: 판이 끝나기 전에 반드시 풀리게 — 가장 늦은 시작 = 판 길이 − 길이. 첫 대기와 같으면 허용 (경계 포함)
            float latestStart = roundLength - duration;

            if (latestStart < firstDelay)
                return BlackoutPlan.None;

            return new BlackoutPlan(Lerp(firstDelay, latestStart, Clamp01(startRoll)), duration);
        }

        /// <summary>
        /// 판 시작 후 elapsed 초일 때의 상태 (KB16). t = elapsed − 시작:
        ///  t &lt; 0 대기(0) · 0 ≤ t &lt; fade 어두워지는 중(t / fade) · fade ≤ t &lt; 길이 암전(1) · t ≥ 길이 끝(0 — 바로 밝아짐, KB6).
        /// fade 가 0 이하면 시작 순간 바로 암전, 길이보다 길면 길이로 줄인다 (어두워지는 시간은 지속 시간에 포함 — KB6).
        /// </summary>
        public static BlackoutState Evaluate(BlackoutPlan plan, float elapsed, float fadeSeconds)
        {
            if (!plan.Planned || plan.Duration <= 0f)
                return new BlackoutState(BlackoutPhase.None, 0f, 0f, 0f);

            float fade = fadeSeconds < 0f ? 0f : fadeSeconds;

            if (fade > plan.Duration)
                fade = plan.Duration;

            float t = elapsed - plan.StartOffset;

            if (t < 0f)
                return new BlackoutState(BlackoutPhase.Waiting, 0f, -t, plan.Duration);

            if (t >= plan.Duration)
                return new BlackoutState(BlackoutPhase.Ended, 0f, 0f, 0f);

            float left = plan.Duration - t;

            if (t < fade)
                return new BlackoutState(BlackoutPhase.FadingIn, t / fade, 0f, left);

            return new BlackoutState(BlackoutPhase.Dark, 1f, 0f, left);
        }

        private static float Clamp01(float value)
        {
            return value < 0f ? 0f : value > 1f ? 1f : value;
        }

        private static float Lerp(float from, float to, float t)
        {
            return from + (to - from) * t;
        }
    }
}
