using System;

namespace TrustNoOne.Missions
{
    /// <summary>
    /// [역할] 대형 방화문(TG005)의 계산 규칙. 엔진 없이 테스트한다 (T22). 3d 명세 3-1.
    ///  - 패널 화면: 완료 > 실패 > 30초 > 잠김 > 끼움 > READY 우선순위 (FD2 · FD5 · FD9)
    ///  - 시각 공식: 30초 시작 · 내린 시각 · 튕겨 낸 시각 · 열린 시각만 동기화하고, 카운트다운 · 레버 움직임 · 포물선 · 문 열림은
    ///    각 PC 가 그 시각으로 계산한다 (FD13, 3a ~ 3c 와 같은 방식)
    /// [이유] 판정 규칙을 엔진 · 네트워크와 떼어 두면 숫자를 바꿔도 테스트로 바로 확인할 수 있다.
    /// </summary>
    public static class FireDoorRules
    {
        /// <summary>패널(레버) 수 — 방화문 양쪽에 하나씩 (FD1).</summary>
        public const int PanelCount = 2;

        /// <summary>시각 값이 "없음"일 때.</summary>
        public const float NotStarted = -1f;

        /// <summary>패널 화면에 띄울 상태.</summary>
        public enum PanelScreen
        {
            Locked,
            Ready,
            Wait,
            Countdown,
            Failed,
            Open,
        }

        /// <summary>패널 화면 고르기 — 완료 &gt; 실패 직후 &gt; 30초 중 &gt; 잠김 &gt; 이 패널에 끼움 &gt; READY.</summary>
        public static PanelScreen ScreenFor(bool completed, bool unlocked, bool mounted, bool counting, bool failedShowing)
        {
            if (completed)
                return PanelScreen.Open;

            if (failedShowing)
                return PanelScreen.Failed;

            if (counting)
                return PanelScreen.Countdown;

            if (!unlocked)
                return PanelScreen.Locked;

            return mounted ? PanelScreen.Wait : PanelScreen.Ready;
        }

        /// <summary>start 부터 duration 초 동안인가 (30초 진행 중).</summary>
        public static bool IsCounting(float t, float start, float duration)
        {
            return start >= 0f && t >= start && t < start + duration;
        }

        /// <summary>start 부터 duration 초 동안 보여 주는가 (FAILED 화면). 계산은 IsCounting 과 같다.</summary>
        public static bool IsShowing(float t, float start, float duration)
        {
            return IsCounting(t, start, duration);
        }

        /// <summary>30초가 끝났는가 (start 가 없으면 false).</summary>
        public static bool IsTimeUp(float t, float start, float duration)
        {
            return start >= 0f && t >= start + duration;
        }

        /// <summary>30초 중이면 남은 초(올림 — 30 → 1), 아니면 0.</summary>
        public static int CountdownSeconds(float t, float start, float duration)
        {
            if (!IsCounting(t, start, duration))
                return 0;

            return Math.Max(1, (int)Math.Ceiling((double)(start + duration - t)));
        }

        /// <summary>경고(빨간 숫자)인가 — 1 ≤ 남은 초 ≤ threshold.</summary>
        public static bool IsWarning(int seconds, int threshold)
        {
            return seconds >= 1 && seconds <= threshold;
        }

        /// <summary>start 부터의 진행 0 ~ 1 (레버 내림 · 튕겨 나가기 · 문 열림). start 가 없으면 −1, duration ≤ 0 이면 1.</summary>
        public static float Progress01(float t, float start, float duration)
        {
            if (start < 0f)
                return -1f;

            if (duration <= 0f)
                return 1f;

            float progress = (t - start) / duration;
            return progress < 0f ? 0f : (progress > 1f ? 1f : progress);
        }

        /// <summary>튕겨 나가는 포물선 높이 — 출발 · 도착 0, 가운데 peak.</summary>
        public static float PopHeight(float progress, float peak)
        {
            return 4f * peak * progress * (1f - progress);
        }

        /// <summary>모든 패널에 레버를 끼웠나 (패널이 없으면 false).</summary>
        public static bool AllMounted(bool[] mounted)
        {
            if (mounted.Length == 0)
                return false;

            foreach (bool value in mounted)
            {
                if (!value)
                    return false;
            }

            return true;
        }

        /// <summary>모든 레버를 내렸나 — 내린 시각이 0 이상 (패널이 없으면 false).</summary>
        public static bool AllPulled(float[] pulledTimes)
        {
            if (pulledTimes.Length == 0)
                return false;

            foreach (float pulledTime in pulledTimes)
            {
                if (pulledTime < 0f)
                    return false;
            }

            return true;
        }
    }
}
