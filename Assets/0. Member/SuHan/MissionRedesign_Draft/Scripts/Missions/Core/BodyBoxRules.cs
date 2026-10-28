using System;
using System.Collections.Generic;
using UnityEngine;

namespace TrustNoOne.Missions
{
    /// <summary>
    /// [역할] 장치 · 선반의 "막는 몸체" 상자를 정하는 순수 규칙 (11a 명세 BC7).
    ///  - PadHorizontal: 모델 범위 상자를 가로(x · z)로만 넓힌다 (높이는 모델 그대로)
    ///  - Merge: 겹치거나 닿는 상자를 하나(합집합 상자)로, 더 합칠 게 없을 때까지 → 붙어 있는 부품은 상자 하나, 멀리 떨어진 부품(압력 피스톤 7m)은 따로
    ///  - (테스트 후 추가 — BC19) SliceTriangles · MergeTight: 휜 모양(압력 콘솔)은 0.4m 조각 상자로 곡선을 따라가고, 합쳐도 빈 공간이 거의 늘지 않을 때만 합친다
    ///  - (테스트 후 추가 — BC20) RaiseTop: 상자 높이를 최소 2.2m 로 — 낮은 몸체 위에 올라서서 "공중 부양"하지 못하게
    /// [이유] 플레이어 카메라가 몸 앞면(앞쪽 0.3 · 몸 반지름 0.3)에 붙어 있고 0.3m 보다 가까운 것은 그리지 않는다(Near 0.3).
    ///  모델 크기 그대로 막으면 몸이 닿는 순간 앞면이 잘려 안이 보인다 → 카메라가 모델 표면에서 0.35m 이상 떨어지게 넓힌다 (BC2).
    ///  어떤 상자를 만들지를 에디터 도구(MissionBodyBuilder)와 떼어 엔진 밖에서 테스트한다 (T36).
    /// [근거] 11a 명세 BC2 (사용자 결정 C — 넓힌 콜라이더 + 카메라 요청), BC7, BC16, BC19 · BC20 (사용자 결정 A — 압력 공중 부양, 진단 로그로 확인)
    /// </summary>
    public static class BodyBoxRules
    {
        /// <summary>넓히는 거리 (m) = 카메라 Near 0.3 + 여유 0.05 (BC2).</summary>
        public const float CameraClearance = 0.35f;

        /// <summary>
        /// 몸체 최소 높이 (m, BC20) — 상자 바닥에서 이만큼. 플레이어 점프(약 0.46m) + 턱 오르기(0.3m)로도 윗면에 못 올라서게 넉넉히
        /// (압력 패널 0.89m 상자 위에 실제로 올라섰다 — 진단 로그).
        /// </summary>
        public const float MinBodyHeight = 2.2f;

        /// <summary>휜 모양 조각 폭 (m, BC19).</summary>
        public const float SliceWidth = 0.4f;

        /// <summary>조각 상자 넓힘 (m, BC19) — 팀원이 카메라 Near 를 0.03 으로 낮춰(cd2a5ef) 0.35 가 필요 없다. 버튼 앞까지 다가가게.</summary>
        public const float TightPadding = 0.05f;

        /// <summary>합쳤을 때 늘어나도 되는 빈 부피 비율 (BC19) — 곧은 조각은 합치고 곡선은 남긴다.</summary>
        public const float MaxMergeWaste = 0.1f;

        /// <summary>상자 높이가 minHeight 보다 낮으면 위로 늘린다 (바닥은 그대로 — BC20).</summary>
        public static Bounds RaiseTop(Bounds box, float minHeight)
        {
            if (box.size.y >= minHeight)
                return box;

            Bounds raised = new Bounds();
            raised.SetMinMax(box.min, new Vector3(box.max.x, box.min.y + minHeight, box.max.z));
            return raised;
        }

        /// <summary>
        /// 삼각형들을 전체 범위의 긴 가로축(x 또는 z) 방향으로 같은 폭(sliceWidth 이하) 조각으로 나눠, 조각마다 그 안에 걸친 삼각형들의 범위 상자 (BC19).
        /// 조각에 걸친 삼각형은 그 축 범위만 조각에 맞게 자르고 나머지 축은 삼각형 범위 그대로 (조금 넉넉).
        /// </summary>
        public static List<Bounds> SliceTriangles(IReadOnlyList<Vector3> vertices, IReadOnlyList<int> triangles, float sliceWidth)
        {
            List<Bounds> result = new List<Bounds>();

            if (vertices.Count == 0 || triangles.Count < 3)
                return result;

            Vector3 min = vertices[0], max = vertices[0];

            foreach (Vector3 v in vertices)
            {
                min = Vector3.Min(min, v);
                max = Vector3.Max(max, v);
            }

            int axis = max.x - min.x >= max.z - min.z ? 0 : 2;
            float start = min[axis];
            float extent = max[axis] - start;
            int count = sliceWidth > 0f ? Math.Max(1, (int)Math.Ceiling(extent / sliceWidth - 1e-4)) : 1;
            float width = extent > 0f ? extent / count : 1f;
            Bounds[] slices = new Bounds[count];
            bool[] filled = new bool[count];

            for (int i = 0; i + 2 < triangles.Count; i += 3)
            {
                Vector3 a = vertices[triangles[i]], b = vertices[triangles[i + 1]], c = vertices[triangles[i + 2]];
                Vector3 triMin = Vector3.Min(a, Vector3.Min(b, c));
                Vector3 triMax = Vector3.Max(a, Vector3.Max(b, c));
                int first = Clamp((int)Math.Floor((triMin[axis] - start) / width), 0, count - 1);
                int last = Clamp((int)Math.Floor((triMax[axis] - start) / width), 0, count - 1);

                for (int s = first; s <= last; s++)
                {
                    Vector3 lo = triMin, hi = triMax;
                    lo[axis] = Math.Max(triMin[axis], start + s * width);
                    hi[axis] = Math.Min(triMax[axis], start + (s + 1) * width);

                    if (hi[axis] < lo[axis])
                        continue;

                    if (!filled[s])
                    {
                        slices[s] = new Bounds();
                        slices[s].SetMinMax(lo, hi);
                        filled[s] = true;
                    }
                    else
                    {
                        slices[s].Encapsulate(lo);
                        slices[s].Encapsulate(hi);
                    }
                }
            }

            for (int s = 0; s < count; s++)
            {
                if (filled[s])
                    result.Add(slices[s]);
            }

            return result;
        }

        /// <summary>
        /// 겹치거나 닿는 두 상자를, 합집합 상자의 부피가 두 상자가 실제로 차지하는 부피(겹친 부분은 한 번)보다
        /// maxWaste 비율 이상 크지 않을 때만 합친다 (BC19 — 곧은 조각은 하나로, 휜 조각 · ㄱ자는 따로). 더 합칠 게 없을 때까지.
        /// </summary>
        public static List<Bounds> MergeTight(IList<Bounds> boxes, float maxWaste)
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
                        // 맞닿음은 오차 여유를 두고 본다 — Bounds 는 가운데 ± 반 크기로 저장돼 맞댄 조각 경계에 아주 작은 틈이 생긴다
                        if (!Touches(result[i], result[j], TouchTolerance))
                            continue;

                        Bounds union = result[i];
                        union.Encapsulate(result[j]);
                        float occupied = Volume(result[i]) + Volume(result[j]) - OverlapVolume(result[i], result[j]);

                        if (Volume(union) > occupied * (1f + maxWaste) + 1e-6f)
                            continue;

                        result[i] = union;
                        result.RemoveAt(j);
                        merged = true;
                        break;
                    }
                }
            }

            return result;
        }

        // 맞닿음 오차 여유 (m)
        private const float TouchTolerance = 1e-4f;

        private static bool Touches(Bounds a, Bounds b, float tolerance)
        {
            return a.min.x <= b.max.x + tolerance && a.max.x >= b.min.x - tolerance
                && a.min.y <= b.max.y + tolerance && a.max.y >= b.min.y - tolerance
                && a.min.z <= b.max.z + tolerance && a.max.z >= b.min.z - tolerance;
        }

        private static float Volume(Bounds box) => box.size.x * box.size.y * box.size.z;

        private static float OverlapVolume(Bounds a, Bounds b)
        {
            float x = Math.Min(a.max.x, b.max.x) - Math.Max(a.min.x, b.min.x);
            float y = Math.Min(a.max.y, b.max.y) - Math.Max(a.min.y, b.min.y);
            float z = Math.Min(a.max.z, b.max.z) - Math.Max(a.min.z, b.min.z);
            return x > 0f && y > 0f && z > 0f ? x * y * z : 0f;
        }

        private static int Clamp(int value, int lo, int hi) => value < lo ? lo : value > hi ? hi : value;

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
