using System;

namespace TrustNoOne.Gimmicks
{
    /// <summary>
    /// [역할] 판 시작 비교 결과 한 개 — 암전 기준 지난 초 · 타이머 기준 지난 초 · 차이 · 허용 오차 · 같음 여부.
    /// [근거] 16c 명세 KB50
    /// </summary>
    public readonly struct BlackoutTimerComparison
    {
        /// <summary>암전 관리자가 잰 판 시작 후 지난 초 (KillerBlackoutManager.Elapsed)</summary>
        public float BlackoutElapsed { get; }

        /// <summary>팀원 GameTimer 로 잰 판 시작 후 지난 초 (= 전체 − 남은)</summary>
        public float TimerElapsed { get; }

        /// <summary>차이 = 암전 기준 − 타이머 기준. 양수면 암전 쪽 판 시작이 더 이르다</summary>
        public float Gap { get; }

        /// <summary>같다고 보는 차이의 크기 (0 이상)</summary>
        public float Tolerance { get; }

        /// <summary>|차이| ≤ 허용 오차</summary>
        public bool Matches { get; }

        public BlackoutTimerComparison(float blackoutElapsed, float timerElapsed, float gap, float tolerance, bool matches)
        {
            BlackoutElapsed = blackoutElapsed;
            TimerElapsed = timerElapsed;
            Gap = gap;
            Tolerance = tolerance;
            Matches = matches;
        }
    }

    /// <summary>
    /// [역할] 살인마 암전의 판 시작 시각이 팀원 GameTimer 의 시작과 같은 순간인지 판정하는 순수 규칙 (엔진 참조 없음).
    /// [이유] 암전의 판 시작 = 미션 배정 알림을 받은 순간(16a KB10)이다. 팀원 RoleAssignment 는 같은 함수 안에서 미션 배정 →
    ///  역할 배정 끝 알림(→ GameTimer 시작) 순서로 부르므로 코드로는 같은 틱이지만(16c 명세 1-2), 실제 게임에서 기록으로 확인한다 (16a 위험 1).
    ///  두 값은 같은 틱에 정해지므로 차이는 0 이어야 하고, float 반올림만 1틱까지 허용한다.
    /// [근거] 16c 명세 KB44 · KB50 (사용자 결정 B — 질문 3), 작업 규칙 "판정 규칙은 엔진 밖 순수 클래스"
    /// </summary>
    public static class BlackoutTimerCheck
    {
        /// <summary>
        /// 비교한다. 타이머 기준 지난 초 = timerTotal − timerRemaining, 차이 = blackoutElapsed − 타이머 기준.
        /// tolerance 는 보통 1틱(Runner.DeltaTime — 틱 64 면 약 0.016초). 음수면 0 으로 본다.
        /// </summary>
        public static BlackoutTimerComparison Compare(float blackoutElapsed, float timerTotal, float timerRemaining, float tolerance)
        {
            float timerElapsed = timerTotal - timerRemaining;
            float gap = blackoutElapsed - timerElapsed;
            float safeTolerance = Math.Max(0f, tolerance);
            bool matches = Math.Abs(gap) <= safeTolerance;
            return new BlackoutTimerComparison(blackoutElapsed, timerElapsed, gap, safeTolerance, matches);
        }
    }
}
