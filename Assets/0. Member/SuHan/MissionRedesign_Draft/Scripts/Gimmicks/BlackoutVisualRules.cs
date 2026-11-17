namespace TrustNoOne.Gimmicks
{
    /// <summary>암전 중 팔을 어디서 가져와 보일지 (16b KB34)</summary>
    public enum BlackoutArmsSource
    {
        /// <summary>팔을 보이지 않음 (암전이 아니거나 내 화면에 적용되지 않음)</summary>
        None,

        /// <summary>팀원 1인칭 팔(+ 든 물건)을 팔 레이어로 옮겨 보임 — 물건을 들었을 때</summary>
        Teammate,

        /// <summary>같은 팔 프리팹으로 만든 내 복사본을 보임 — 빈손일 때</summary>
        Copy
    }

    /// <summary>이번 프레임 내 화면을 어떻게 가릴지 (BlackoutVisualRules.Decide 의 결과)</summary>
    public readonly struct BlackoutVisualState
    {
        /// <summary>암전 화면이 켜져 있는가 (어두움 &gt; 0 이고 내 화면에 적용됨)</summary>
        public readonly bool Active;

        /// <summary>
        /// 어두움 0 ~ 1 — 가까운 시야 패스의 진하기에 곱한다 (16d KB60). 시야 재질이 없을 때는 대체 검은 판의 진하기 (16d KB62).
        /// (16d — KB32 바뀜) 어두움 1 이어도 3D 는 그대로 그린다
        /// </summary>
        public readonly float PlaneAlpha;

        /// <summary>팔 전용 카메라를 켜는가 (어두워지는 1초 동안에도 — KB28 · KB33)</summary>
        public readonly bool ArmsCamera;

        /// <summary>팔 출처</summary>
        public readonly BlackoutArmsSource Arms;

        /// <summary>미니맵 · 전체 지도를 막는가 (KB29)</summary>
        public readonly bool BlockMap;

        /// <summary>조준점 아래 안내 글자 진하기 0 ~ 1 (= 어두움 — KB36)</summary>
        public readonly float LabelAlpha;

        public BlackoutVisualState(bool active, float planeAlpha, bool armsCamera, BlackoutArmsSource arms, bool blockMap, float labelAlpha)
        {
            Active = active;
            PlaneAlpha = planeAlpha;
            ArmsCamera = armsCamera;
            Arms = arms;
            BlockMap = blockMap;
            LabelAlpha = labelAlpha;
        }

        /// <summary>평소 화면 — 아무것도 가리지 않음</summary>
        public static BlackoutVisualState Off => new BlackoutVisualState(false, 0f, false, BlackoutArmsSource.None, false, 0f);
    }

    /// <summary>
    /// [역할] 살인마 암전의 화면 상태 규칙 (엔진 참조 없음 — 엔진 밖 dotnet 으로 테스트).
    ///  Decide: 어두움 · 적용 여부 · 팀원 팔이 켜져 있는지 → 진하기 · 팔 카메라 · 팔 출처 · 지도 막기 · 글자 진하기.
    ///  PickLabel: 조준점 아래에 띄울 글자 — (16d) 지도 대체 안내만.
    ///  NearVisionAlpha: (16d) 카메라에서의 거리 → 그 점을 덮을 검은 진하기 (셰이더 KillerBlackoutNearVision 과 같은 식).
    /// [이유] 화면 부품 네 개(BlackoutScreenRig · BlackoutArms · BlackoutMapBlocker · BlackoutPromptLabel)가 같은 기준으로 켜지고 꺼지게
    ///  판단을 한곳에 모은다. 부품은 이 결과대로 팀원 오브젝트를 바꾸고 되돌리기만 한다.
    /// [근거] 16b 명세 KB28 (1초 동안 팔은 밝게) · KB29 (지도 막기) · KB33 · KB34 · KB38, 16d 명세 KB55 · KB56 · KB59 · KB61
    ///  (16d — 기획 피드백 "바로 앞까지만 보이도록") KB32 (어두움 1 에서 3D 끄기) · KB27 · KB36 (장치 문구를 화면 글자로) 는 바뀌었다
    /// </summary>
    public static class BlackoutVisualRules
    {
        /// <summary>닫기 함수를 못 찾아 지도를 투명하게만 가렸는데 지도가 열려 있을 때 띄우는 글자 (KB29 대체)</summary>
        public const string MapCloseHint = "지도 닫기: M";

        /// <summary>
        /// 이번 프레임 화면 상태. 어두움은 0 ~ 1 로 자른다 (NaN 은 0 — 꺼짐).
        /// 적용되지 않으면(시민 · 사망 · 탈출 · 판 시작 전) 어두움과 상관없이 모두 꺼진다 (16a KB14 · 16b KB35).
        /// </summary>
        public static BlackoutVisualState Decide(float darkness, bool appliesToMe, bool teammateArmsShown)
        {
            // !(x > 0) 은 0 이하와 NaN 을 함께 거른다
            if (!appliesToMe || !(darkness > 0f))
                return BlackoutVisualState.Off;

            float alpha = darkness >= 1f ? 1f : darkness;

            // 물건을 들어 팀원 팔이 켜져 있으면 그 팔(+ 물건)을, 빈손이면 내 복사본을 (KB26 · KB34)
            BlackoutArmsSource arms = teammateArmsShown ? BlackoutArmsSource.Teammate : BlackoutArmsSource.Copy;

            // (16d KB55) 어두움 1 이어도 3D 는 끄지 않는다 — 가까운 시야 패스가 거리만큼 칠한다
            return new BlackoutVisualState(true, alpha, true, arms, true, alpha);
        }

        /// <summary>
        /// 조준점 아래 글자: 지도 닫기 함수를 못 찾았는데 지도가 열려 있으면 "지도 닫기: M", 아니면 없음(null).
        /// (16d KB56) 장치 조준 문구는 띄우지 않는다 — 조준 거리(2m)가 보이는 거리 안이라 장치의 3D 글자 · 외곽선이 평소대로 보인다.
        /// </summary>
        public static string PickLabel(bool mapOpenWithoutClose)
        {
            return mapOpenWithoutClose ? MapCloseHint : null;
        }

        /// <summary>
        /// (16d KB59 · KB61) 카메라에서 distance(m) 떨어진 점을 덮을 검은 진하기 0 ~ 1.
        /// start 까지 0 (그대로 보임), end 부터 1 (완전 검정), 사이는 부드러운 계단 smoothstep — 셰이더 KillerBlackoutNearVision 과 같은 식.
        /// start ≥ end (잘못 넣은 값)이면 end 에서 0 → 1 로 딱 끊긴다 (셰이더의 step 과 같음). 음수 · NaN 거리는 0.
        /// </summary>
        public static float NearVisionAlpha(float distance, float start, float end)
        {
            // !(x > 0) 은 0 이하와 NaN 을 함께 거른다
            if (!(distance > 0f))
                return 0f;

            if (!(end > start))
                return distance >= end ? 1f : 0f;

            float t = (distance - start) / (end - start);

            if (t <= 0f)
                return 0f;

            if (t >= 1f)
                return 1f;

            return t * t * (3f - 2f * t);
        }
    }
}
