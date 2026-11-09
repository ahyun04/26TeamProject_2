using Fusion;
using TMPro;
using UnityEngine;
using Random = UnityEngine.Random;

namespace TrustNoOne.Missions
{
    /// <summary>
    /// [역할] 좌표 설정 장치 (단체 미션 TG013 "좌표 설정" · CoordinateSet). 13b 명세 CO2 ~ CO26.
    ///  두 사람이 초록(X) · 노랑(Y) 버튼을 하나씩 맡는다. F 를 누르고 있는 동안 그 축으로 조작 원이 움직이고, 떼면 멈춘다 (다음에 누르면 반대 방향).
    ///  조작 원이 목표에 맞은 채 두 버튼을 모두 떼고 1초가 지나면 「설정 완료」 → 잠김 · TG013 완료.
    ///
    /// [기획서] "두 명의 플레이어가 각각 초록색 버튼과 노란색 버튼을 하나씩 담당한다" / "초록색 버튼: X축(가로) · 노란색 버튼: Y축(세로) 방향으로 조작 원을 이동" /
    ///  "가까워질수록 … 신호음의 재생 속도가 빨라진다 · 조작 원 내부에 색상이 점점 채워진다" / "정확하게 맞추면 설정 완료" / "별도의 실패 조건 없이".
    /// [담당 자리 — CO3 · CO20 · CO21] 조작자(Operator)는 쓰지 않는다. 공통 틀의 자리 도우미(TryClaimSeat — 조작자와 같은 자격 검사 + busy)로 색마다 담당을 둔다.
    ///  처음 잡은 사람이 담당, 떼도 유지, 멀어지거나 행동 불가 · 완료 · 판 다시 시작이면 풀림. busy 라서 한 사람은 한 색만 (테스트용 allowSoloOperation 이면 다른 색을 풀고 옮겨 잡음).
    /// [움직임 — CO14] 호스트가 매 틱 CoordinateRules.StepAxis (끝에서 튕김) · 뗄 때 ReleasedDirection (반대 방향).
    /// [완료 — CO6 · CO23] 맞음 + 둘 다 뗀 시간 1초 → 마지막으로 뗀 담당자 이름으로 CompleteBy, 완료음은 다른 담당자에게도 (PlayCompletionSoundFor).
    ///  (CO37 · CO38 — 계획 실행 중 보충) 목표는 x · y 모두 0 에서 떨어지게 뽑고, 완료 순간 초록 · 노랑 담당이 서로 다른 두 사람으로 모두 있어야 한다
    ///  (한 색만으로 · 혼자 멀어졌다 돌아와 번갈아서는 못 깨게). 맞았는데 한 색이 비어 있으면 화면에 "… 담당이 필요합니다".
    /// [표시 — CO18 · CO24] 담당이 있으면 화면 켜짐(CoordinateScreen), 버튼 조준 문구는 내 PC 기준. 신호음은 칸이 있을 때만 (CO5 — 지금은 비어 있어 소리 없음).
    ///  (15 NM9) 누르는 중인 버튼은 윗면 방향으로 들어간다 — 받은 모델의 버튼이 따로 떨어진 부품이라, 누르는 중 비트(PressMask)로 모든 PC 에서 보인다.
    /// [동기화 — CO19] 원 · 목표 · 방향 · 담당 · 누르는 중은 [Networked] — 늦게 들어온 사람도 같은 화면. 멈춤 1초는 호스트만 센다.
    /// [근거] 13b 명세 CO2 ~ CO26 · CO32, 기획서 "좌표 설정"
    /// </summary>
    public class CoordinateStation : MissionStation, IHoldPartReceiver
    {
        public const int GreenPart = 0;
        public const int YellowPart = 1;

        public const string MoveXText = "[F] X축 이동";
        public const string MoveYText = "[F] Y축 이동";
        public const string TakenText = "다른 사람이 담당 중";
        public const string OneColorText = "한 사람은 한 색만 — 다른 사람이 필요합니다";
        public const string SoloSuffix = " (혼자 테스트)";

        /// <summary>버튼 문구 중 가장 긴 것 — 변환기가 글자 크기를 이 문구에 맞춘다.</summary>
        public const string LongestPromptText = OneColorText;

        [Header("움직임 (13b CO14)")]
        [Tooltip("초당 이동 (탐색 영역 반지름 = 1). 0.4 = 끝에서 끝까지 5초")]
        [SerializeField] private float moveSpeed = 0.4f;

        [Header("판정 (13b CO6 · CO17)")]
        [Tooltip("목표에서 이 거리 안이면 맞음 (탐색 영역 반지름 = 1)")]
        [SerializeField] private float alignTolerance = 0.08f;

        [Tooltip("맞은 채 두 버튼을 모두 떼고 이만큼 지나면 설정 완료 (초)")]
        [SerializeField] private float holdStillSeconds = 1f;

        [Tooltip("색 채움이 0 이 되는 거리 — 채움 = 1 − 거리 ÷ 이 값")]
        [SerializeField] private float fillRange = 1f;

        [Header("목표 (13b CO15)")]
        [Tooltip("목표는 가운데(조작 원 시작 자리)에서 이 거리 이상 — 시작하자마자 맞는 일 방지")]
        [SerializeField] private float targetMinRadius = 0.45f;

        [Tooltip("목표는 가운데에서 이 거리 이하")]
        [SerializeField] private float targetMaxRadius = 0.8f;

        [Tooltip("목표의 x · y 가 각각 0 에서 이만큼 이상 (CO37 — 한 축 위에 있으면 한 색만으로 끝나서). 허용 거리보다 커야 한다")]
        [SerializeField] private float targetMinAxis = 0.2f;

        [Header("신호음 (13b CO5 · CO16 — 소리 파일은 기획자가 정하면 끼운다)")]
        [Tooltip("비어 있으면 소리가 나지 않는다")]
        [SerializeField] private AudioClip beepClip;
        [SerializeField] private AudioSource beepSource;

        [Tooltip("채움 0 일 때 간격 (초)")]
        [SerializeField] private float beepSlowest = 1.2f;

        [Tooltip("채움 1 일 때 간격 (초)")]
        [SerializeField] private float beepFastest = 0.15f;

        [Header("표시")]
        [SerializeField] private CoordinateScreen screen;
        [SerializeField] private TMP_Text greenPrompt;
        [SerializeField] private TMP_Text yellowPrompt;

        [Header("버튼 들어감 (15 NM9 — 변환 도구가 채운다)")]
        [Tooltip("초록 버튼 부품 (받은 모델의 button_green)")]
        [SerializeField] private Transform greenButton;
        [Tooltip("노랑 버튼 부품 (받은 모델의 button_yellow)")]
        [SerializeField] private Transform yellowButton;
        [Tooltip("누르는 동안 버튼이 윗면 방향으로 들어가는 깊이 (m)")]
        [SerializeField] private float buttonPressDepth = 0.015f;

        [Header("테스트 (13b CO26)")]
        [Tooltip("켬 = 한 사람이 두 색을 번갈아 맡을 수 있다 (혼자 테스트 씬만). 실제 게임은 꺼짐 — 두 사람이 필요")]
        [SerializeField] private bool allowSoloOperation;

        [Networked] private float PosX { get; set; }
        [Networked] private float PosY { get; set; }
        [Networked] private float TargetX { get; set; }
        [Networked] private float TargetY { get; set; }
        [Networked] private int DirX { get; set; }
        [Networked] private int DirY { get; set; }
        [Networked] private PlayerRef GreenSeat { get; set; }
        [Networked] private PlayerRef YellowSeat { get; set; }
        [Networked] private int PressMask { get; set; }

        // 호스트만: 맞은 채 둘 다 뗀 시간 · 마지막으로 뗀 담당자 (완료 이벤트 이름)
        private float stillSeconds;
        private PlayerRef lastReleaser;

        // 각 PC: 신호음 시간 · 문구 캐시
        private float beepTimer;
        private string shownGreenPrompt;
        private string shownYellowPrompt;

        // 각 PC: 버튼 제자리 (처음 Render 때 읽음) · 보여 준 누르는 중 비트 (바뀔 때만 움직인다)
        private Vector3 greenRest;
        private Vector3 yellowRest;
        private bool buttonRestRead;
        private int shownPressMask = -1;

        // ═════════════════════════════════════════════════════════════
        //  호스트: 판 시작 · 담당 자리 · 움직임 · 완료
        // ═════════════════════════════════════════════════════════════

        protected override void OnMissionsAssignedHost(bool usedThisRound)
        {
            base.OnMissionsAssignedHost(usedThisRound);
            ResetBoard(usedThisRound);
        }

        protected override void OnResetHost()
        {
            base.OnResetHost();
            ResetBoard(true);
        }

        /// <summary>담당을 모두 풀고 조작 원을 가운데로, 이번 판에 TG013 이 있으면 목표를 새로 뽑는다 (CO15 · CO21).</summary>
        private void ResetBoard(bool roll)
        {
            ReleaseSeatOf(GreenPart);
            ReleaseSeatOf(YellowPart);
            PosX = 0f;
            PosY = 0f;
            DirX = 1;
            DirY = 1;
            PressMask = 0;
            stillSeconds = 0f;
            lastReleaser = PlayerRef.None;

            if (!roll)
            {
                TargetX = 0f;
                TargetY = 0f;
                Debug.Log($"[CoordinateStation] {name}: 이번 판에 좌표 미션이 없어 화면을 꺼 둡니다.");
                return;
            }

            CoordinateRules.PickTarget(Random.value, Random.value, targetMinRadius, targetMaxRadius, targetMinAxis, out float x, out float y);
            TargetX = x;
            TargetY = y;
            Debug.Log($"[CoordinateStation] {name}: 목표 ({x:0.00}, {y:0.00}) — 가운데에서 {CoordinateRules.Distance(0f, 0f, x, y):0.00}");
        }

        // StationHoldPart 가 F 누름 · 뗌을 부품 번호와 함께 넘긴다 (CO27). 클라이언트는 요청만 한다
        public void BeginHoldPart(int part)
        {
            if (Object == null || !Object.IsValid || Completed)
                return;

            RPC_BeginAxis(part);
        }

        public void EndHoldPart(int part)
        {
            if (Object == null || !Object.IsValid)
                return;

            RPC_EndAxis(part);
        }

        /// <summary>F 누름 (CO20): 내 담당이면 움직임, 비어 있으면 자격 검사 후 담당 + 움직임, 다른 사람 담당이면 거부.</summary>
        [Rpc(RpcSources.All, RpcTargets.StateAuthority, HostMode = RpcHostMode.SourceIsHostPlayer)]
        private void RPC_BeginAxis(int part, RpcInfo info = default)
        {
            PlayerRef actor = info.Source;

            if (actor.IsNone || Completed || (part != GreenPart && part != YellowPart))
                return;

            PlayerRef seat = SeatOf(part);

            if (seat == actor)
            {
                SetPressed(part, true);
                return;
            }

            if (!seat.IsNone)
                return;

            // 이미 다른 색 담당 — busy 라서 자격 검사에서도 거부되지만, 혼자 테스트면 그 색을 풀고 옮겨 잡는다
            if (SeatOf(1 - part) == actor)
            {
                if (!allowSoloOperation)
                    return;

                ReleaseSeatOf(1 - part);
            }

            if (!TryClaimSeat(actor))
                return;

            SetSeat(part, actor);
            SetPressed(part, true);
            Debug.Log($"[CoordinateStation] {name}: {ColorName(part)} 담당 = 플레이어 {actor.PlayerId}");
        }

        /// <summary>F 뗌: 담당 본인의 뗌만 받는다. 움직임만 멈추고 담당은 유지, 다음 방향은 반대 (CO14 · CO20).</summary>
        [Rpc(RpcSources.All, RpcTargets.StateAuthority, HostMode = RpcHostMode.SourceIsHostPlayer)]
        private void RPC_EndAxis(int part, RpcInfo info = default)
        {
            if ((part != GreenPart && part != YellowPart) || SeatOf(part) != info.Source || !IsPressed(part))
                return;

            SetPressed(part, false);

            if (part == GreenPart)
                DirX = CoordinateRules.ReleasedDirection(DirX);
            else
                DirY = CoordinateRules.ReleasedDirection(DirY);

            lastReleaser = info.Source;
        }

        protected override void OnHostTickAlways()
        {
            base.OnHostTickAlways();

            if (Completed)
                return;

            // 담당 검사 (CO21): 멀어짐 · 행동 불가 → 그 담당만 풀림
            for (int part = GreenPart; part <= YellowPart; part++)
            {
                PlayerRef seat = SeatOf(part);

                if (!seat.IsNone && !CanKeepSeat(seat))
                {
                    ReleaseSeatOf(part);
                    Debug.Log($"[CoordinateStation] {name}: {ColorName(part)} 담당이 풀렸습니다 (멀어짐 · 행동 불가)");
                }
            }

            float dt = Runner.DeltaTime;

            if (IsPressed(GreenPart))
            {
                float x = PosX;
                int dir = DirX;
                CoordinateRules.StepAxis(ref x, ref dir, moveSpeed, dt);
                PosX = x;
                DirX = dir;
            }

            if (IsPressed(YellowPart))
            {
                float y = PosY;
                int dir = DirY;
                CoordinateRules.StepAxis(ref y, ref dir, moveSpeed, dt);
                PosY = y;
                DirY = dir;
            }

            bool aligned = CoordinateRules.IsAligned(CoordinateRules.Distance(PosX, PosY, TargetX, TargetY), alignTolerance);
            // 두 사람이 모두 담당 중일 때만 시간이 쌓인다 (CO38)
            stillSeconds = CoordinateRules.AdvanceStill(stillSeconds, aligned && SeatsReady, PressMask != 0, dt);

            // 누군가 움직인 뒤에만 (조작 원은 목표에서 0.45 이상 떨어진 가운데에서 시작한다)
            if (stillSeconds >= holdStillSeconds && !lastReleaser.IsNone)
                CompleteSetting();
        }

        /// <summary>설정 완료 (CO6 · CO23): 담당을 풀고, 조작 원을 목표 위에 맞춰 보이고, 마지막으로 뗀 사람 이름으로 완료, 다른 담당자에게도 완료음.</summary>
        private void CompleteSetting()
        {
            PlayerRef green = GreenSeat;
            PlayerRef yellow = YellowSeat;

            // 마지막으로 뗀 사람이 그 뒤 담당을 놓았으면(멀어짐) 지금 담당자 이름으로 — 완료음이 지금 장치 앞의 두 사람에게 가게
            PlayerRef actor = lastReleaser == green || lastReleaser == yellow || (green.IsNone && yellow.IsNone)
                ? lastReleaser
                : !green.IsNone ? green : yellow;
            PlayerRef partner = actor == green ? yellow : green;

            ReleaseSeatOf(GreenPart);
            ReleaseSeatOf(YellowPart);
            PosX = TargetX;
            PosY = TargetY;

            Debug.Log($"[CoordinateStation] {name}: 설정 완료 — 마지막으로 뗀 플레이어 {actor.PlayerId}{(partner.IsNone || partner == actor ? "" : $", 함께 플레이어 {partner.PlayerId}")}");
            CompleteBy(actor);

            if (!partner.IsNone && partner != actor)
                PlayCompletionSoundFor(partner);
        }

        protected override void OnStationDespawned(bool hasState)
        {
            base.OnStationDespawned(hasState);

            if (!hasState)
                return;

            ReleaseSeat(GreenSeat);
            ReleaseSeat(YellowSeat);
        }

        private PlayerRef SeatOf(int part) => part == GreenPart ? GreenSeat : YellowSeat;

        private bool SeatsReady => CoordinateRules.SeatsReady(!GreenSeat.IsNone, !YellowSeat.IsNone, GreenSeat == YellowSeat, allowSoloOperation);

        private void SetSeat(int part, PlayerRef actor)
        {
            if (part == GreenPart)
                GreenSeat = actor;
            else
                YellowSeat = actor;
        }

        private bool IsPressed(int part) => (PressMask & (1 << part)) != 0;

        private void SetPressed(int part, bool pressed)
        {
            PressMask = pressed ? PressMask | (1 << part) : PressMask & ~(1 << part);
        }

        /// <summary>그 색 담당을 푼다: busy 해제 · 담당 비움 · 누르는 중 끔.</summary>
        private void ReleaseSeatOf(int part)
        {
            PlayerRef seat = SeatOf(part);

            if (seat.IsNone)
                return;

            ReleaseSeat(seat);
            SetSeat(part, PlayerRef.None);
            SetPressed(part, false);
        }

        private static string ColorName(int part) => part == GreenPart ? "초록(X)" : "노랑(Y)";

        // ═════════════════════════════════════════════════════════════
        //  모든 PC: 화면 · 문구 · 신호음
        // ═════════════════════════════════════════════════════════════

        protected override void OnStationRender()
        {
            base.OnStationRender();

            if (Object == null || !Object.IsValid)
                return;

            float distance = CoordinateRules.Distance(PosX, PosY, TargetX, TargetY);
            float fill = Completed ? 1f : CoordinateRules.Fill(distance, fillRange);
            bool on = Completed || !GreenSeat.IsNone || !YellowSeat.IsNone;
            bool aligned = Completed || CoordinateRules.IsAligned(distance, alignTolerance);
            // 맞았는데 한 색이 비어 있으면 안내 (CO38 — 맞췄는데 왜 안 끝나는지 보이게)
            bool waitingSeat = aligned && !Completed && !SeatsReady;

            if (screen != null)
            {
                screen.Show(new CoordinateView
                {
                    On = on,
                    Completed = Completed,
                    X = PosX,
                    Y = PosY,
                    TargetX = TargetX,
                    TargetY = TargetY,
                    Fill = fill,
                    Aligned = aligned,
                    DirX = DirX >= 0 ? 1 : -1,
                    DirY = DirY >= 0 ? 1 : -1,
                    PressX = IsPressed(GreenPart),
                    PressY = IsPressed(YellowPart),
                    Green = StateOf(GreenSeat),
                    Yellow = StateOf(YellowSeat),
                    NeedGreen = waitingSeat && GreenSeat.IsNone,
                    NeedYellow = waitingSeat && YellowSeat.IsNone,
                });
            }

            ShowPrompt(greenPrompt, GreenPart, ref shownGreenPrompt);
            ShowPrompt(yellowPrompt, YellowPart, ref shownYellowPrompt);
            UpdateButtons();
            UpdateBeep(on && !Completed, fill);
        }

        /// <summary>누르는 중인 버튼을 윗면 방향(버튼 부품의 위쪽 = 조작판 밖)으로 깊이만큼 넣는다 (NM9). 완료되면 모두 올라온다.</summary>
        private void UpdateButtons()
        {
            if (!buttonRestRead)
            {
                if (greenButton != null)
                    greenRest = greenButton.localPosition;

                if (yellowButton != null)
                    yellowRest = yellowButton.localPosition;

                buttonRestRead = true;
            }

            int mask = Completed ? 0 : PressMask;

            if (mask == shownPressMask)
                return;

            shownPressMask = mask;
            PressButton(greenButton, greenRest, (mask & (1 << GreenPart)) != 0);
            PressButton(yellowButton, yellowRest, (mask & (1 << YellowPart)) != 0);
        }

        private void PressButton(Transform button, Vector3 rest, bool pressed)
        {
            if (button == null)
                return;

            // 부모 기준 버튼 윗면 방향 = 버튼 회전 × 위 (받은 모델: 45° 기운 조작판 — 15 명세 1-1)
            button.localPosition = pressed ? rest - button.localRotation * Vector3.up * buttonPressDepth : rest;
        }

        private CoordinateSeatState StateOf(PlayerRef seat)
        {
            if (seat.IsNone)
                return CoordinateSeatState.Empty;

            return seat == Runner.LocalPlayer ? CoordinateSeatState.Mine : CoordinateSeatState.Taken;
        }

        /// <summary>버튼 조준 문구 (CO24 — 내 PC 기준, 연출만. 실제 허용은 호스트가 RPC_BeginAxis 에서).</summary>
        private void ShowPrompt(TMP_Text prompt, int part, ref string shownText)
        {
            if (prompt == null)
                return;

            PlayerRef local = Runner.LocalPlayer;
            PlayerRef seat = SeatOf(part);
            string text;

            if (!seat.IsNone && seat != local)
                text = TakenText;
            else if (seat != local && SeatOf(1 - part) == local && !allowSoloOperation)
                text = OneColorText;
            else
                text = (part == GreenPart ? MoveXText : MoveYText) + (allowSoloOperation ? SoloSuffix : string.Empty);

            if (text == shownText)
                return;

            shownText = text;
            prompt.text = text;
        }

        /// <summary>신호음 (CO16 · CO32): 화면이 켜진 동안 채움에 따라 간격이 짧아진다. 소리 칸이 비어 있으면 아무것도 하지 않는다.</summary>
        private void UpdateBeep(bool active, float fill)
        {
            if (!active || beepClip == null || beepSource == null)
            {
                beepTimer = 0f;
                return;
            }

            beepTimer += Time.deltaTime;

            if (beepTimer < CoordinateRules.BeepInterval(fill, beepSlowest, beepFastest))
                return;

            beepTimer = 0f;
            beepSource.PlayOneShot(beepClip, GameAudio.getEffectsVolume());
        }
    }
}
