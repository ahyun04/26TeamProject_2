using System;

namespace TrustNoOne.Missions
{
    /// <summary>
    /// [역할] 고장난 장비 조립(TG003)의 계산 규칙. 엔진 없이 테스트한다 (T21). 3c 명세 3-1.
    ///  - 배치: 6b 보강부터 장치가 1층 NavMesh 위 무작위로 놓는다 (AssemblyStation.PlaceParts · SpawnPointRules — 기획서 "주변에 흩어져" · "맵 곳곳" 둘 다)
    ///  - 끼우기: 부품 색 = 자리 색 · 빈 자리일 때만 (7c — 맞는 색 자리를 조준하고 우클릭, CanAttach. 바닥 원 규칙은 없앰)
    ///  - 시각 공식: 자리별 붙은 시각만 동기화하고, 날아가기 진행은 각 PC 가 그 시각으로 계산한다 (EA12, 3a · 3b 와 같은 방식)
    /// [이유] 판정 규칙을 엔진 · 네트워크와 떼어 두면 숫자를 바꿔도 테스트로 바로 확인할 수 있다.
    /// </summary>
    public static class AssemblyRules
    {
        /// <summary>부품 수 = 장비 자리 수. 색 번호 0 파랑 · 1 회색 · 2 빨강 · 3 노랑.</summary>
        public const int PartCount = 4;

        /// <summary>붙은 시각이 "없음"(빈 자리)일 때.</summary>
        public const float NotAttached = -1f;

        /// <summary>
        /// 부품을 이 자리에 끼울 수 있는가 (7c 명세 AR5): 부품 색 = 자리 색 · 자리가 비었음 · 색 번호가 범위 안.
        /// 기획서 "잘못된 부품을 조립 위치에 가져와도 해당 장비의 부품으로 인정되지 않음" — 다른 색 자리에 우클릭하면 거부.
        /// </summary>
        public static bool CanAttach(int partColor, int slotColor, bool slotEmpty)
        {
            return slotEmpty && partColor == slotColor && partColor >= 0 && partColor < PartCount;
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
