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

        /// <summary>검은 판 진하기 0 ~ 1 (= 어두움)</summary>
        public readonly float PlaneAlpha;

        /// <summary>내 카메라가 아무것도 그리지 않게 하는가 (어두움 1 — KB32)</summary>
        public readonly bool HideWorld;

        /// <summary>팔 전용 카메라를 켜는가 (어두워지는 1초 동안에도 — KB28 · KB33)</summary>
        public readonly bool ArmsCamera;

        /// <summary>팔 출처</summary>
        public readonly BlackoutArmsSource Arms;

        /// <summary>미니맵 · 전체 지도를 막는가 (KB29)</summary>
        public readonly bool BlockMap;

        /// <summary>조준점 아래 안내 글자 진하기 0 ~ 1 (= 어두움 — KB36)</summary>
        public readonly float LabelAlpha;

        public BlackoutVisualState(bool active, float planeAlpha, bool hideWorld, bool armsCamera, BlackoutArmsSource arms, bool blockMap, float labelAlpha)
        {
            Active = active;
            PlaneAlpha = planeAlpha;
            HideWorld = hideWorld;
            ArmsCamera = armsCamera;
            Arms = arms;
            BlockMap = blockMap;
            LabelAlpha = labelAlpha;
        }

        /// <summary>평소 화면 — 아무것도 가리지 않음</summary>
        public static BlackoutVisualState Off => new BlackoutVisualState(false, 0f, false, false, BlackoutArmsSource.None, false, 0f);
    }

    /// <summary>
    /// [역할] 살인마 암전의 화면 상태 규칙 (엔진 참조 없음 — 엔진 밖 dotnet 으로 테스트).
    ///  Decide: 어두움 · 적용 여부 · 팀원 팔이 켜져 있는지 → 검은 판 · 3D 끄기 · 팔 카메라 · 팔 출처 · 지도 막기 · 글자 진하기.
    ///  PickLabel: 조준점 아래에 띄울 글자 하나를 고른다.
    /// [이유] 화면 부품 네 개(BlackoutScreenRig · BlackoutArms · BlackoutMapBlocker · BlackoutPromptLabel)가 같은 기준으로 켜지고 꺼지게
    ///  판단을 한곳에 모은다. 부품은 이 결과대로 팀원 오브젝트를 바꾸고 되돌리기만 한다.
    /// [근거] 16b 명세 KB28 (1초 동안 팔은 밝게) · KB29 (지도 막기) · KB32 (어두움 1 에서 3D 끄기) · KB33 · KB34 · KB36 · KB38
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

            // 어두움 1 = 판이 화면을 완전히 덮음 → 그때부터 카메라가 3D 를 아예 그리지 않는다 (반투명 효과가 판 위로 비치지 않게 — KB32)
            bool hideWorld = alpha >= 1f;

            // 물건을 들어 팀원 팔이 켜져 있으면 그 팔(+ 물건)을, 빈손이면 내 복사본을 (KB26 · KB34)
            BlackoutArmsSource arms = teammateArmsShown ? BlackoutArmsSource.Teammate : BlackoutArmsSource.Copy;

            return new BlackoutVisualState(true, alpha, hideWorld, true, arms, true, alpha);
        }

        /// <summary>
        /// 조준점 아래 글자: 지도 대체 안내가 먼저 (지도가 열려 커서가 풀린 상태를 먼저 알려야 함), 없으면 켜진 조준 문구, 빈 문구면 없음(null).
        /// </summary>
        public static string PickLabel(string promptText, bool mapOpenWithoutClose)
        {
            if (mapOpenWithoutClose)
                return MapCloseHint;

            return string.IsNullOrWhiteSpace(promptText) ? null : promptText;
        }
    }
}
