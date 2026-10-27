using System;

namespace TrustNoOne.Missions
{
    /// <summary>생명 유지 장치 본체 화면 상태 (7d 명세 L3B9).</summary>
    public enum LifeSupportScreen
    {
        Empty = 0,
        Check = 1,
        Success = 2,
        Damaged = 3,
    }

    /// <summary>
    /// [역할] 생명 유지 장치(TG004 · 산소통 선별)의 계산 규칙. 엔진 없이 테스트한다 (T19). 3b 명세 3-1.
    ///  - 배치: 정상 / 위험 섞기 — 정상 개수는 정확히 (LS2). 자리는 1층 NavMesh 위 무작위 (6b 보강 — SpawnPointRules · NavMeshSpawnArea)
    ///  - 시각 공식: 넣은 시각 · 폭발 시각만 동기화하고, 카운트다운 숫자 · 폭발 섬광은 각 PC 가 그 시각으로 계산한다 (LS14, 3a C3 와 같은 방식)
    ///  - 폭발: 반경 안 여부와 피해량 (LS5)
    ///  - 7d: 본체 한 대 = 산소통 1개. 화면 상태 ScreenFor · 넣기 조건 CanInsert (L3B8 · L3B9). WARNING 깜빡임(IsBlinkOn)은 없앰 (L3B3)
    ///  - 7e: 시간 초과 때 남은 산소통은 그대로 두고 모자란 만큼만 채운다 — MissingTanks (7e 명세 RT3)
    /// [이유] 판정 규칙을 엔진 · 네트워크와 떼어 두면 숫자를 바꿔도 테스트로 바로 확인할 수 있다.
    /// </summary>
    public static class LifeSupportRules
    {
        /// <summary>시각 값이 "없음"(판정 중 아님 · 폭발 없음)일 때.</summary>
        public const float NotStarted = -1f;

        /// <summary>
        /// dangerous 중 정확히 safeCount 개(0 ~ 길이로 제한)를 false(정상), 나머지를 true(위험)로 하고 순서를 섞는다 (LS2).
        /// 기획서 "산소통마다 정상 또는 위험 상태가 무작위" — 다만 필요한 공급 수만큼의 정상 통은 반드시 있어야 미션을 끝낼 수 있다.
        /// </summary>
        public static void DrawDangers(bool[] dangerous, int safeCount, Func<int, int> randomBelow)
        {
            int safe = Math.Min(Math.Max(safeCount, 0), dangerous.Length);

            for (int i = 0; i < dangerous.Length; i++)
                dangerous[i] = i >= safe;

            for (int i = dangerous.Length - 1; i > 0; i--)
            {
                int j = randomBelow(i + 1);
                (dangerous[i], dangerous[j]) = (dangerous[j], dangerous[i]);
            }
        }

        /// <summary>
        /// 시간 초과 때 새로 놓을 산소통 수 (7e 명세 RT3): 본체 한 대가 놓은 통 중 남은 것을 빼고, 원래 구성(정상 safeTanks · 위험 tankCount − safeTanks)이 되도록.
        /// 넣어서 사라진 통만 그 종류로 채운다 — 정상을 썼으면 정상, 위험이 터졌으면 위험. 남은 게 더 많아도 0 아래로 내려가지 않는다.
        /// </summary>
        public static void MissingTanks(int tankCount, int safeTanks, int aliveSafe, int aliveDangerous, out int safe, out int dangerous)
        {
            int wantSafe = Math.Max(0, Math.Min(safeTanks, tankCount));
            int wantDangerous = Math.Max(0, tankCount - wantSafe);

            safe = Math.Max(0, wantSafe - aliveSafe);
            dangerous = Math.Max(0, wantDangerous - aliveDangerous);
        }

        /// <summary>넣은 뒤 판정 전(duration 초 동안)인가.</summary>
        public static bool IsCounting(float t, float insertStart, float duration)
        {
            return insertStart >= 0f && t >= insertStart && t < insertStart + duration;
        }

        /// <summary>판정 중이면 화면에 띄울 남은 초(올림 — 5 → 1), 아니면 0.</summary>
        public static int CountdownSeconds(float t, float insertStart, float duration)
        {
            if (!IsCounting(t, insertStart, duration))
                return 0;

            return Math.Max(1, (int)Math.Ceiling((double)(insertStart + duration - t)));
        }

        /// <summary>판정할 시각이 되었는가 (기획서 "넣은후 5초후").</summary>
        public static bool IsResolved(float t, float insertStart, float duration)
        {
            return insertStart >= 0f && t >= insertStart + duration;
        }

        /// <summary>start 부터 duration 초 동안인가 (폭발 뒤 DAMAGED 화면 등). start 가 없으면(−1) false.</summary>
        public static bool IsShowing(float t, float start, float duration)
        {
            return start >= 0f && t >= start && t < start + duration;
        }

        /// <summary>
        /// 본체 화면 상태 (7d 명세 L3B9): 우선순위 성공 > 판정 중 > 폭발 뒤 > 대기.
        /// 판정 중에는 정상 · 위험을 구분하지 않는다 (피드백 — 결과는 5초 뒤 폭발 또는 SUCCESS 로만 드러난다).
        /// </summary>
        public static LifeSupportScreen ScreenFor(bool completed, bool counting, bool damagedShowing)
        {
            if (completed)
                return LifeSupportScreen.Success;

            if (counting)
                return LifeSupportScreen.Check;

            return damagedShowing ? LifeSupportScreen.Damaged : LifeSupportScreen.Empty;
        }

        /// <summary>산소통을 넣을 수 있는가 (7d 명세 L3B8): 이 본체가 아직 성공하지 않았고 판정 중이 아닐 때.</summary>
        public static bool CanInsert(bool completed, bool judging)
        {
            return !completed && !judging;
        }

        /// <summary>폭발 섬광 진행 0 ~ 1 (커지는 비율). 구간 밖이면 −1.</summary>
        public static float FlashProgress(float t, float start, float duration)
        {
            if (duration <= 0f || !IsShowing(t, start, duration))
                return -1f;

            return (t - start) / duration;
        }

        /// <summary>
        /// 폭발음을 지금 틀 것인가 (12a — 7d L3B15, T39). 각 PC 가 동기화된 폭발 시각으로 정한다:
        /// 폭발이 있고(시작 ≥ 0) · 아직 이 폭발을 안 틀었고 · 화면 시각이 폭발 시각에 닿았고 · 그 뒤 window 초 안.
        /// 닿기 전에는 기다린다 (섬광과 같은 순간). 창이 지난 폭발(늦게 들어온 사람)은 틀지 않는다.
        /// </summary>
        /// <param name="lastPlayed">이 PC 가 마지막으로 튼 폭발 시각 (없으면 NotStarted)</param>
        public static bool ShouldPlayBlast(float t, float explodeStart, float lastPlayed, float window)
        {
            if (explodeStart < 0f || explodeStart == lastPlayed)
                return false;

            return t >= explodeStart && t - explodeStart <= window;
        }

        /// <summary>폭발 반경 안인가 (경계 포함).</summary>
        public static bool InBlast(float distance, float radius)
        {
            return distance <= radius;
        }

        /// <summary>폭발 피해 = 최대 체력 × 비율 (LS5). 음수는 0.</summary>
        public static float BlastDamage(float maxHealth, float ratio)
        {
            return Math.Max(0f, maxHealth * ratio);
        }
    }
}
