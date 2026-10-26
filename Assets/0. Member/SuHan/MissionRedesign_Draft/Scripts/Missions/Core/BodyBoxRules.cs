using System.Collections.Generic;
using UnityEngine;

namespace TrustNoOne.Missions
{
    /// <summary>
    /// [역할] 장치 · 선반의 "막는 몸체" 상자를 정하는 순수 규칙 (11a 명세 BC7).
    ///  - PadHorizontal: 모델 범위 상자를 가로(x · z)로만 넓힌다 (높이는 모델 그대로)
    ///  - Merge: 겹치거나 닿는 상자를 하나(합집합 상자)로, 더 합칠 게 없을 때까지 → 붙어 있는 부품은 상자 하나, 멀리 떨어진 부품(압력 피스톤 7m)은 따로
    /// [이유] 플레이어 카메라가 몸 앞면(앞쪽 0.3 · 몸 반지름 0.3)에 붙어 있고 0.3m 보다 가까운 것은 그리지 않는다(Near 0.3).
    ///  모델 크기 그대로 막으면 몸이 닿는 순간 앞면이 잘려 안이 보인다 → 카메라가 모델 표면에서 0.35m 이상 떨어지게 넓힌다 (BC2).
    ///  어떤 상자를 만들지를 에디터 도구(MissionBodyBuilder)와 떼어 엔진 밖에서 테스트한다 (T36).
    /// [근거] 11a 명세 BC2 (사용자 결정 C — 넓힌 콜라이더 + 카메라 요청), BC7, BC16
    /// </summary>
    public static class BodyBoxRules
    {
        /// <summary>넓히는 거리 (m) = 카메라 Near 0.3 + 여유 0.05 (BC2).</summary>
        public const float CameraClearance = 0.35f;

        /// <summary>
        /// 가로(x · z)로만 양쪽에 넓힌다. 가운데 · 높이는 그대로.
        /// 축마다 따로 받는 이유: 상자는 루트 기준 좌표라, 루트 크기가 1 이 아니면 같은 0.35m 가 축마다 다른 값이 된다.
        /// </summary>
        public static Bounds PadHorizontal(Bounds box, float padX, float padZ)
        {
            Vector3 size = box.size;
            size.x += padX * 2f;
            size.z += padZ * 2f;
            return new Bounds(box.center, size);
        }

        /// <summary>
        /// 겹치거나 닿는(Bounds.Intersects — 경계가 맞닿아도 참) 상자 둘을 합집합 상자로 바꾸기를, 더 합칠 게 없을 때까지 되풀이한다.
        /// 결과 순서 = 각 묶음에서 입력 순서상 처음 나온 상자의 자리. 입력 목록은 바꾸지 않는다.
        /// </summary>
        public static List<Bounds> Merge(IList<Bounds> boxes)
        {
            List<Bounds> result = new List<Bounds>(boxes);
            bool merged = true;

            while (merged)
            {
                merged = false;

                for (int i = 0; i < result.Count && !merged; i++)
                {
                    for (int j = i + 1; j < result.Count; j++)
                    {
                        if (!result[i].Intersects(result[j]))
                            continue;

                        // 합친 상자가 커지면서 앞에서 안 닿던 상자와 닿을 수 있다 → 처음부터 다시 본다
                        Bounds union = result[i];
                        union.Encapsulate(result[j]);
                        result[i] = union;
                        result.RemoveAt(j);
                        merged = true;
                        break;
                    }
                }
            }

            return result;
        }
    }
}
