using System;
using System.Reflection;
using TrustNoOne.Minimap;
using UnityEngine;

namespace TrustNoOne.Gimmicks
{
    /// <summary>
    /// [역할] 살인마 암전 동안 미니맵 · M 전체 지도를 끈다 — 끝나면 원래대로.
    ///  1) 전체 지도가 열려 있으면 팀원 MinimapController 의 비공개 닫기 함수(closeFull)를 이름으로 찾아 부른다 (커서 잠금도 그 함수가 되돌림)
    ///  2) 미니맵 컴포넌트를 끈다 → 매 프레임 미니맵을 켜던 Update 가 멈추고 M 이 무시된다
    ///  3) 미니맵 오브젝트에 CanvasGroup 을 실행 중에만 붙여 투명하게 — 이미 켜져 있던 미니맵 그림을 가린다
    ///  닫기 함수를 못 찾으면 (팀원이 이름을 바꿈) 3) 만 한다 — 이때 지도가 열려 있으면 조준점 아래에 "지도 닫기: M" (BlackoutPromptLabel).
    /// [이유] 기획서 "별도의 시야 확보 기능을 제공하지 않음". 투명하게만 하면 M 으로 보이지 않는 지도가 열리고 커서만 풀린다 (질문 4).
    ///  미니맵은 미션 목록 · 남은 시간과 같은 캔버스에 있어 캔버스를 통째로 가리면 안 된다 (조사 1-3).
    ///  암전 중 누른 M 이 무시되는 것은 의도된 동작이다.
    /// [근거] 16b 명세 KB29 · KB35 · KB40 (시작 로그) · KB41 (실제 확인은 16c — 미니맵은 4~12 씬에만)
    /// </summary>
    public class BlackoutMapBlocker : MonoBehaviour
    {
        private const string CloseMethodName = "closeFull";

        // 팀원 비공개 함수 — 이름이 바뀌면 null (위험 6). 씬과 상관없이 타입만으로 찾을 수 있다
        private static readonly MethodInfo closeMethod =
            typeof(MinimapController).GetMethod(CloseMethodName, BindingFlags.Instance | BindingFlags.NonPublic, null, Type.EmptyTypes, null);

        private MinimapController minimap;
        private bool searched;
        private bool blocking;
        private bool disabledController;
        private CanvasGroup group;
        private bool addedGroup;
        private float savedAlpha;
        private bool savedBlocksRaycasts;
        private bool savedInteractable;

        /// <summary>팀원 지도 닫기 함수를 찾았는가</summary>
        public static bool CloseMethodFound => closeMethod != null;

        /// <summary>닫기 함수 없이 투명하게만 가렸는데 지도가 열려 있다 → "지도 닫기: M" 안내 (KB29 대체)</summary>
        public bool FallbackMapOpen => blocking && closeMethod == null && MinimapController.IsFullMapOpen;

        /// <summary>디버그 패널 (KB40): 씬에 없음 / 막음 / 대체(투명만) / 평소</summary>
        public string StatusLabel
        {
            get
            {
                if (FindMinimap() == null)
                    return "씬에 없음";

                if (!blocking)
                    return closeMethod != null ? "평소" : "평소 (닫기 함수 못 찾음)";

                return closeMethod != null ? "막음" : "대체(투명만)";
            }
        }

        private void Start()
        {
            FindMinimap();
            Debug.Log($"[BlackoutMapBlocker] 미니맵: {(minimap != null ? "있음" : "씬에 없음")} · 전체 지도 닫기 함수({CloseMethodName}): " +
                      (closeMethod != null ? "찾음" : "못 찾음 — 암전 중 투명하게만 가림"));
        }

        /// <summary>규칙의 결과대로 지도를 막거나 되돌린다</summary>
        public void Apply(BlackoutVisualState state)
        {
            if (state.Active && state.BlockMap)
                Block();
            else
                Restore();
        }

        /// <summary>KB35: CanvasGroup 을 원래대로 (붙인 것이면 지움), 끈 컴포넌트를 다시 켠다</summary>
        public void Restore()
        {
            if (!blocking)
                return;

            blocking = false;

            if (group != null)
            {
                if (addedGroup)
                {
                    Destroy(group);
                }
                else
                {
                    group.alpha = savedAlpha;
                    group.blocksRaycasts = savedBlocksRaycasts;
                    group.interactable = savedInteractable;
                }
            }

            group = null;
            addedGroup = false;

            if (disabledController && minimap != null)
                minimap.enabled = true;

            disabledController = false;
        }

        private void OnDisable()
        {
            Restore();
        }

        private void Block()
        {
            if (FindMinimap() == null)
                return;

            if (!blocking)
            {
                blocking = true;

                // 팀원이 이미 CanvasGroup 을 쓰고 있으면 값만 기억했다가 되돌린다
                group = minimap.GetComponent<CanvasGroup>();
                addedGroup = group == null;

                if (addedGroup)
                {
                    group = minimap.gameObject.AddComponent<CanvasGroup>();
                }
                else
                {
                    savedAlpha = group.alpha;
                    savedBlocksRaycasts = group.blocksRaycasts;
                    savedInteractable = group.interactable;
                }
            }

            group.alpha = 0f;
            group.blocksRaycasts = false;
            group.interactable = false;

            if (closeMethod == null)
                return;

            if (MinimapController.IsFullMapOpen)
            {
                try
                {
                    closeMethod.Invoke(minimap, null);
                }
                catch (Exception exception)
                {
                    Debug.LogWarning($"[BlackoutMapBlocker] 전체 지도 닫기 실패: {exception.InnerException?.Message ?? exception.Message}");
                }
            }

            if (minimap.enabled)
            {
                minimap.enabled = false;
                disabledController = true;
            }
        }

        private MinimapController FindMinimap()
        {
            // 미니맵은 씬에 놓인 오브젝트라 한 번만 찾는다 (4~12 씬에만 있음 — 조사 1-3)
            if (minimap == null && !searched)
            {
                minimap = FindFirstObjectByType<MinimapController>();
                searched = true;
            }

            return minimap;
        }
    }
}
