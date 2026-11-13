using Fusion;
using UnityEngine;

namespace TrustNoOne.Gimmicks
{
    /// <summary>
    /// [역할] 이 PC 의 화면만 가린다 (살인마 전용 암전). KillerBlackoutManager 와 같은 오브젝트에 붙는다.
    ///  매 프레임: 관리자의 상태(Evaluate)를 읽고, 내가 살아 있는 · 탈출하지 않은 살인마인지 본 뒤,
    ///  순수 규칙 BlackoutVisualRules 로 이번 프레임 화면 상태를 정해 같은 오브젝트의 부품 네 개에 나눠 준다 —
    ///  BlackoutScreenRig (검은 판 · 팔 카메라 · 3D 끄기) · BlackoutArms (팔) · BlackoutMapBlocker (지도) · BlackoutPromptLabel (조준점 아래 글자).
    /// [이유] 각 PC 가 자기 화면만 가린다 (접근법 1 — KB9). 시민 · 관전자 화면은 그대로 (기획서 "살인마만 적용").
    ///  부품을 나눈 이유: 카메라 · 팔 · 지도 · 글자가 각자 다른 팀원 오브젝트를 실행 중에만 바꾸고 되돌린다 — 되돌리기를 부품마다 책임진다.
    ///  16a 의 임시 검은 캔버스(Overlay 순서 −100 — 팔까지 가리고 미니맵은 보였음)는 지웠다.
    /// [근거] 16a 명세 KB5 · KB14 · KB23, 16b 명세 KB31 · KB35 · KB40 (KB18 은 바뀜)
    /// </summary>
    [RequireComponent(typeof(KillerBlackoutManager))]
    [RequireComponent(typeof(BlackoutScreenRig), typeof(BlackoutArms), typeof(BlackoutMapBlocker))]
    [RequireComponent(typeof(BlackoutPromptLabel))]
    public class KillerBlackoutView : MonoBehaviour
    {
        private KillerBlackoutManager manager;
        private BlackoutScreenRig screenRig;
        private BlackoutArms arms;
        private BlackoutMapBlocker mapBlocker;
        private BlackoutPromptLabel promptLabel;
        private PlayerHealth myHealth;
        private BlackoutPhase lastPhase = BlackoutPhase.None;

        /// <summary>지금 내 화면에 적용되는가 (살아 있는 · 탈출하지 않은 살인마)</summary>
        public bool AppliesToMe { get; private set; }

        /// <summary>적용되지 않는 이유 (시민 · 사망 · 탈출 · 판 시작 전). 적용되면 빈 글자</summary>
        public string NotAppliedReason { get; private set; } = "판 시작 전";

        /// <summary>지금 내 화면의 어두움 0 ~ 1</summary>
        public float Darkness { get; private set; }

        /// <summary>디버그 패널 한 줄 (16b KB40) — 예) 팔 = 복사본(빈손) · 지도 = 씬에 없음 · 안내 = "[F]키를 눌러 상호작용"</summary>
        public string ScreenStatus
        {
            get
            {
                string armsText = arms == null ? "?" : arms.CurrentSource switch
                {
                    BlackoutArmsSource.Teammate => "팀원 팔(든 물건)",
                    BlackoutArmsSource.Copy => "복사본(빈손)",
                    _ => "없음",
                };
                string mapText = mapBlocker != null ? mapBlocker.StatusLabel : "?";
                string labelText = promptLabel != null && promptLabel.CurrentText != null ? $"\"{promptLabel.CurrentText}\"" : "없음";
                return $"팔 = {armsText} · 지도 = {mapText} · 안내 = {labelText}";
            }
        }

        private void Awake()
        {
            manager = GetComponent<KillerBlackoutManager>();
            screenRig = GetComponent<BlackoutScreenRig>();
            arms = GetComponent<BlackoutArms>();
            mapBlocker = GetComponent<BlackoutMapBlocker>();
            promptLabel = GetComponent<BlackoutPromptLabel>();
        }

        private void LateUpdate()
        {
            float darkness = 0f;
            AppliesToMe = false;

            if (manager != null && manager.Object != null && manager.Object.IsValid && manager.RoundStarted)
            {
                BlackoutState state = manager.Evaluate();
                AppliesToMe = CheckLocalPlayer(out string reason);
                NotAppliedReason = reason;

                if (AppliesToMe)
                    darkness = state.Darkness;

                LogPhaseChange(state.Phase);
            }
            else
            {
                NotAppliedReason = "판 시작 전";
            }

            Darkness = darkness;
            ApplyScreen(darkness);
        }

        private void OnDisable()
        {
            // KB35: 이 스크립트가 꺼지면 부품도 평소 화면으로 (부품도 각자 꺼질 때 되돌린다)
            ApplyParts(null, BlackoutVisualState.Off);
        }

        /// <summary>
        /// 16b KB38: 규칙에 이번 프레임 화면 상태를 묻고 부품에 나눠 준다. 팀원 팔이 켜져 있는지는 암전이 내 화면에 적용될 때만 찾는다
        /// (팔은 내 카메라 바로 아래 — 평소에는 찾지 않음).
        /// </summary>
        private void ApplyScreen(float darkness)
        {
            Camera cam = FindMyCamera();
            bool teammateArmsShown = darkness > 0f && AppliesToMe && cam != null && arms.IsTeammateArmsShown(cam);
            BlackoutVisualState state = BlackoutVisualRules.Decide(darkness, AppliesToMe, teammateArmsShown);
            ApplyParts(cam, state);
        }

        private void ApplyParts(Camera cam, BlackoutVisualState state)
        {
            if (screenRig != null)
                screenRig.Apply(cam, state);

            if (arms != null)
                arms.Apply(cam, state);

            if (mapBlocker != null)
                mapBlocker.Apply(state);

            if (promptLabel != null)
                promptLabel.Apply(state, mapBlocker != null && mapBlocker.FallbackMapOpen);
        }

        /// <summary>내 1인칭 카메라 — 팀원 PlayerCameraController 의 공개 값 (팔이 붙는 FirstPersonHoldPoint 의 부모와 같은 카메라 — 조사 1-1 · 1-2)</summary>
        private static Camera FindMyCamera()
        {
            Transform view = PlayerCameraController.LocalListenerTransform;
            return view != null ? view.GetComponent<Camera>() : null;
        }

        /// <summary>
        /// KB14: 내가 살인마 목록에 있고, 내 플레이어의 PlayerHealth 가 죽음 · 탈출이 아니면 적용.
        /// 내 플레이어는 입력 권한(InputAuthority)이 나인 PlayerHealth 로 찾는다 — 모든 PC 에서 같은 방법으로 찾을 수 있다.
        /// 아직 못 찾았으면 살아 있는 것으로 본다 (스폰 직후 한두 프레임).
        /// </summary>
        private bool CheckLocalPlayer(out string reason)
        {
            PlayerRef me = manager.Runner.LocalPlayer;

            if (!manager.IsKiller(me))
            {
                reason = "시민";
                return false;
            }

            PlayerHealth health = FindMyHealth(me);

            if (health != null && health.IsDead)
            {
                reason = "사망";
                return false;
            }

            if (health != null && health.IsEscaped)
            {
                reason = "탈출";
                return false;
            }

            reason = string.Empty;
            return true;
        }

        private PlayerHealth FindMyHealth(PlayerRef me)
        {
            // 찾아 둔 것이 그대로면 다시 찾지 않는다 (매 프레임 전체 검색 방지)
            if (myHealth != null && myHealth.Object != null && myHealth.Object.IsValid && myHealth.Object.InputAuthority == me)
                return myHealth;

            myHealth = null;

            foreach (PlayerHealth health in FindObjectsByType<PlayerHealth>(FindObjectsSortMode.None))
            {
                if (health.Object != null && health.Object.IsValid && health.Object.InputAuthority == me)
                {
                    myHealth = health;
                    break;
                }
            }

            return myHealth;
        }

        /// <summary>KB23: 단계가 바뀔 때 한 줄 — 시작(어두워지기 시작) · 끝. 각 PC 가 자기 로그에 남긴다 (적용 여부 함께)</summary>
        private void LogPhaseChange(BlackoutPhase phase)
        {
            if (phase == lastPhase)
                return;

            bool wasActive = lastPhase == BlackoutPhase.FadingIn || lastPhase == BlackoutPhase.Dark;
            bool isActive = phase == BlackoutPhase.FadingIn || phase == BlackoutPhase.Dark;
            string mine = AppliesToMe ? "적용" : $"미적용({NotAppliedReason})";

            if (!wasActive && isActive)
                Debug.Log($"[KillerBlackoutView] 암전 시작 — 내 화면: {mine}");
            else if (wasActive && !isActive)
                Debug.Log($"[KillerBlackoutView] 암전 끝 — 내 화면: {mine}");

            lastPhase = phase;
        }
    }
}
