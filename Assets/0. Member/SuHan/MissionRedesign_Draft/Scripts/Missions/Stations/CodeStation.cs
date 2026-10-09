using Fusion;
using UnityEngine;

namespace TrustNoOne.Missions
{
    /// <summary>
    /// [역할] 코드 순서 맞추기 (단체 미션 TG002 · SecurityCodeEntered). 신규 미니게임. 3a 명세 3-2.
    ///  빨간 버튼 → 숫자 4개가 하나씩 파란불로 점등 → 모두 꺼지면 조작자가 같은 순서로 숫자 입력 → 파란 버튼으로 판정.
    ///  맞으면 완료(잠금, 초록불), 틀리면 1초 빨간불 후 대기 — 누구든 빨간 버튼으로 새 순서를 시작한다.
    ///
    /// [판정 시점 — C1] 파란 버튼에서 한 번에. 입력 중에는 맞고 틀림을 알려 주지 않는다
    ///  (기획서 실패 조건 "입력 순서를 틀린 상태에서 파란색 버튼을 누른 경우" — 틀린 채로 파란 버튼까지 갈 수 있다).
    /// [조작자 — C2] 빨간 버튼을 누른 시민이 끝까지. 숫자 · 파란 버튼은 조작자만. 다른 시민은 불빛을 같이 보고 도와준다.
    ///  조작자가 다시 빨간 버튼을 누르면 새 순서로 처음부터 (C6).
    /// [동기화 — C3] 켜진 숫자는 보내지 않는다. 순서 4개와 점등 시작 시각만 동기화하고, 모든 PC 가 CodeRules.LitIndexAt 으로
    ///  Object.RenderTime(이 오브젝트가 지금 그려지는 시각) 기준 켜진 숫자를 계산한다 → 게스트도 점등 간격이 일정하고 늦게 들어와도 같은 장면.
    /// [실패 — C5] FailStart 를 기록하고 CancelOperation(조작자 해제 + 되돌리기). 되돌리기는 FailStart 를 남겨서 조작자가 풀려도 빨간불이 보인다.
    /// [중단 — C7] 범위 이탈 · 행동 불가는 공통 틀이 CancelOperation 을 부른다 → 빨간불 없이 대기.
    /// [완료 — C8] CompleteBy → SecurityCodeEntered 발행 + 잠금(Lock). 숫자 버튼 전부 초록불.
    /// [화면 — C13] 패널 화면에 READY / (점등 중 빈 화면) / 입력한 숫자 "3 8 _ _" / ERROR / SUCCESS.
    ///  이미 동기화되는 값(입력 숫자 · 시각)으로 그리므로 동기화 값을 늘리지 않고, 같이 보는 시민에게도 똑같이 보인다.
    ///  입력 중에는 맞고 틀림을 알려 주지 않는다 (C1 그대로 — 누른 숫자만 보여 준다).
    /// </summary>
    public class CodeStation : MissionStation
    {
        public const int RedButton = CodeRules.DigitCount;
        public const int BlueButton = CodeRules.DigitCount + 1;

        public const string ReadyText = "READY";
        public const string ErrorText = "ERROR";
        public const string SuccessText = "SUCCESS";

        [Header("연출")]
        [SerializeField] private CodeKeypadVisual keypad;
        [Tooltip("화면 글자. 비어 있으면 화면 없이 동작")]
        [SerializeField] private LcdDisplay display;

        [Header("점등 시간 (초)")]
        [Tooltip("빨간 버튼을 누른 뒤 첫 숫자가 켜지기 전 쉬는 시간")]
        [SerializeField] private float leadIn = 0.5f;
        [Tooltip("숫자 하나가 켜져 있는 시간")]
        [SerializeField] private float onTime = 0.7f;
        [Tooltip("숫자와 숫자 사이 꺼져 있는 시간")]
        [SerializeField] private float gapTime = 0.3f;
        [Tooltip("틀렸을 때 빨간불이 켜져 있는 시간")]
        [SerializeField] private float failDuration = 1f;

        [Networked, Capacity(CodeRules.SequenceLength)] private NetworkArray<int> Sequence => default;
        [Networked, Capacity(CodeRules.SequenceLength)] private NetworkArray<int> Entered => default;
        [Networked] private int EnteredCount { get; set; }
        [Networked] private float ShowStart { get; set; }
        [Networked] private float FailStart { get; set; }

        private bool validSetup;
        private readonly int[] sequenceBuffer = new int[CodeRules.SequenceLength];
        private readonly int[] enteredBuffer = new int[CodeRules.SequenceLength];

        // 입력 글자 "3 8 _ _" 는 입력이 바뀔 때만 다시 만든다 (매 Render 문자열 생성 방지)
        private readonly char[] enteredChars = new char[CodeRules.SequenceLength * 2 - 1];
        private int enteredKey = -1;
        private string enteredText = string.Empty;

        protected override void OnStationSpawned()
        {
            base.OnStationSpawned();

            validSetup = keypad != null && keypad.IsValid;

            if (!validSetup)
            {
                Debug.LogError($"[CodeStation] {name}: 숫자 버튼 불빛(CodeKeypadVisual)에 렌더러 {CodeRules.DigitCount}개가 연결돼야 합니다.");
                return;
            }

            // Networked 기본값 0 이면 "0초에 시작한 점등 · 빨간불"로 보인다 → 대기(NotStarted)로 시작
            if (HasStateAuthority)
            {
                ClearRound();
                FailStart = CodeRules.NotStarted;
            }
        }

        /// <summary>호스트: 버튼 11개 (0 ~ 8 숫자, 9 빨강, 10 파랑).</summary>
        protected override void OnPartPressed(PlayerRef actor, int partIndex)
        {
            if (!validSetup || Completed || partIndex < 0 || partIndex > BlueButton)
                return;

            float now = Runner.SimulationTime;

            if (partIndex == RedButton)
            {
                // 새 조작자 또는 조작자 본인의 다시 시작 (다른 사람이 조작 중이면 거부됨)
                if (TryBeginOperation(actor))
                    StartRound(now);

                return;
            }

            // 숫자 · 파랑: 조작자만, 점등이 끝난 뒤에만 (C2 · C4)
            if (Operator != actor || !CodeRules.IsInputOpen(now, ShowStart, leadIn, onTime, gapTime))
                return;

            if (partIndex == BlueButton)
            {
                Judge(actor, now);
                return;
            }

            // 5번째 숫자부터는 무시 (C4)
            if (EnteredCount < CodeRules.SequenceLength)
            {
                Entered.Set(EnteredCount, partIndex);
                EnteredCount++;
            }
        }

        protected override void OnOperationCanceled(PlayerRef actor)
        {
            base.OnOperationCanceled(actor);
            ClearRound();
        }

        protected override void OnResetHost()
        {
            base.OnResetHost();
            ClearRound();
            FailStart = CodeRules.NotStarted;
        }

        protected override void OnStationRender()
        {
            base.OnStationRender();

            if (!validSetup || Object == null || !Object.IsValid)
                return;

            if (Completed)
            {
                keypad.ShowDone();
                ShowOnScreen(SuccessText);
                return;
            }

            float t = Object.RenderTime;

            if (CodeRules.IsFailFlash(t, FailStart, failDuration))
            {
                keypad.ShowFailed();
                ShowOnScreen(ErrorText, true);
                return;
            }

            int index = CodeRules.LitIndexAt(t, ShowStart, leadIn, onTime, gapTime);
            keypad.ShowLit(index >= 0 ? Sequence[index] : -1);

            // 화면 (C13): 대기 READY → 점등 중 빈 화면(시선을 자판 불빛에) → 입력 중 누른 숫자
            if (ShowStart < 0f)
                ShowOnScreen(ReadyText);
            else if (!CodeRules.IsInputOpen(t, ShowStart, leadIn, onTime, gapTime))
                ShowOnScreen(string.Empty);
            else
                ShowOnScreen(EnteredText());
        }

        private void ShowOnScreen(string message, bool error = false)
        {
            if (display != null)
                display.Show(message, error);
        }

        /// <summary>입력한 숫자를 "3 8 _ _" 처럼 (숫자는 버튼 표기 1 ~ 9). 입력이 바뀔 때만 새 문자열을 만든다.</summary>
        private string EnteredText()
        {
            int count = Mathf.Clamp(EnteredCount, 0, CodeRules.SequenceLength);

            // 입력 개수 + 숫자들로 만든 열쇠 — 같으면 지난번 문자열 그대로
            int key = count;

            for (int i = 0; i < count; i++)
                key = key * 10 + Entered[i];

            if (key == enteredKey)
                return enteredText;

            for (int i = 0; i < CodeRules.SequenceLength; i++)
            {
                enteredChars[i * 2] = i < count ? (char)('1' + Entered[i]) : '_';

                if (i > 0)
                    enteredChars[i * 2 - 1] = ' ';
            }

            enteredKey = key;
            enteredText = new string(enteredChars);
            return enteredText;
        }

        /// <summary>호스트: 새 순서를 뽑고 점등을 시작한다. 입력 · 빨간불은 지운다.</summary>
        private void StartRound(float now)
        {
            CodeRules.DrawSequence(sequenceBuffer, n => Random.Range(0, n));

            for (int i = 0; i < CodeRules.SequenceLength; i++)
                Sequence.Set(i, sequenceBuffer[i]);

            ShowStart = now;
            EnteredCount = 0;
            FailStart = CodeRules.NotStarted;
        }

        /// <summary>호스트: 파란 버튼 판정 (C1 · C5).</summary>
        private void Judge(PlayerRef actor, float now)
        {
            for (int i = 0; i < CodeRules.SequenceLength; i++)
            {
                sequenceBuffer[i] = Sequence[i];
                enteredBuffer[i] = Entered[i];
            }

            if (CodeRules.Matches(sequenceBuffer, enteredBuffer, EnteredCount))
            {
                CompleteBy(actor);
                return;
            }

            // 조작자 해제 → OnOperationCanceled 가 순서 · 입력을 비운다 (FailStart 는 남김)
            FailStart = now;
            CancelOperation();
        }

        /// <summary>호스트: 대기 상태로 (순서 · 입력만 비운다. 빨간불 시각은 부르는 쪽이 정한다).</summary>
        private void ClearRound()
        {
            ShowStart = CodeRules.NotStarted;
            EnteredCount = 0;
        }
    }
}
