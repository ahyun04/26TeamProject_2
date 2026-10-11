using System;

namespace TrustNoOne.Missions
{
    /// <summary>
    /// [역할] 고장난 장비 조립(TG003)의 계산 규칙. 엔진 없이 테스트한다 (T21). 3c 명세 3-1.
    ///  - 배치: 6b 보강부터 장치가 1층 NavMesh 위 무작위로 놓는다 (AssemblyStation.PlaceParts · SpawnPointRules — 기획서 "주변에 흩어져" · "맵 곳곳" 둘 다)
    ///  - 조립 위치 안: 수평 거리만 본다 (바닥 원이라 높이 차이는 무시)
    ///  - 시각 공식: 자리별 붙은 시각만 동기화하고, 날아가기 진행은 각 PC 가 그 시각으로 계산한다 (EA12, 3a · 3b 와 같은 방식)
    /// [이유] 판정 규칙을 엔진 · 네트워크와 떼어 두면 숫자를 바꿔도 테스트로 바로 확인할 수 있다.
    /// </summary>
    public static class AssemblyRules
    {
        /// <summary>부품 수 = 장비 자리 수. 색 번호 0 파랑 · 1 회색 · 2 빨강 · 3 노랑.</summary>
        public const int PartCount = 4;

        /// <summary>붙은 시각이 "없음"(빈 자리)일 때.</summary>
        public const float NotAttached = -1f;

        /// <summary>조립 위치 원 안인가 (수평 거리 ≤ 반경, 경계 포함).</summary>
        public static bool InZone(float dx, float dz, float radius)
        {
            return dx * dx + dz * dz <= radius * radius;
        }

        /// <summary>날아가기 진행 0 ~ 1. 빈 자리면 −1, 붙은 시각 이전 · 같음 0, duration 뒤 1 (duration ≤ 0 이면 바로 1).</summary>
        public static float FlyProgress(float t, float attachTime, float duration)
        {
            if (attachTime < 0f)
                return -1f;

            if (duration <= 0f)
                return 1f;

            float progress = (t - attachTime) / duration;
            return progress < 0f ? 0f : (progress > 1f ? 1f : progress);
        }

        /// <summary>붙은 자리 수 (붙은 시각이 0 이상).</summary>
        public static int CountAttached(float[] attachTimes)
        {
            int count = 0;

            foreach (float attachTime in attachTimes)
            {
                if (attachTime >= 0f)
                    count++;
            }

            return count;
        }
    }
}
