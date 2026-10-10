using System.Collections.Generic;
using System.Text;
using Fusion;
using UnityEngine;

namespace TrustNoOne.Missions
{
    /// <summary>
    /// [역할] 대형 방화문 (단체 미션 TG005 "대형 방화문 열기" · FireDoorOpened). 신규. 3d 명세 3-4.
    ///  레버 2개를 방화문 주변에 놓고, 다른 단체 미션이 모두 끝나면 양쪽 패널에 레버를 끼울 수 있다.
    ///  두 레버를 끼운 순간 30초 → 그 안에 두 레버를 내리면 문이 열리며 완료(잠금). 못 내리면 레버가 튕겨 나온다.
    ///
    /// [기획서] 단체 미션 기획서 · 전체 기획서 (2): "모든 단체 미션을 완료해야 방화문 개방을 시도 / 레버를 주워 양쪽 레버 장착 위치에 끼운다 /
    ///  모든 레버가 장착되면 30초의 제한 시간 / 제한 시간 안에 장착된 레버를 모두 아래로 / 성공하면 잠금 해제 → 방화문이 열린다 / 실패 시 레버는 튕겨져 나옴".
    /// [해금 — FD2 · FD11] AreOtherTeamMissionsCompleted() — 호스트는 목표 목록(규칙), 다른 PC 는 공개 데이터(LOCKED / READY 표시).
    /// [30초 — FD9] 장치가 직접 센다 (시작 시각만 동기화). 미션 틀의 제한 시간은 "모든 레버를 끼운 순간"부터 셀 수 없다. TG005 = 개수 1, 문이 열릴 때 CompleteBy.
    /// [자격 — FD7] 끼우기 · 내리기 순간마다 공통 TryBeginOperation(시민 · TG005 · 행동 가능 · 범위)만 하고 바로 해제 (3b LS12 와 같은 방식).
    /// [동기화 — FD13] 패널별 끼움 · 내린 시각, 30초 시작 · 튕겨 낸 · 열린 시각만. 움직임은 각 PC 가 Object.RenderTime 으로 계산.
    /// [탈출 — FD15] 이 미션 완료 → 미션 틀의 탈출 해금(escapeMissionId = TG005). 팀원 탈출 코드 연결은 6단계.
    /// </summary>
    public class FireDoorStation : MissionStation
    {
        /// <summary>레버 자리 후보 묶음 이름 (ItemSpots, FD6).</summary>
        public const string LeverSpotGroup = "FireDoorLever";

        public const string LockedText = "LOCKED";
        public const string ReadyText = "READY";
        public const string WaitText = "WAIT";
        public const string FailedText = "FAILED";
        public const string OpenText = "OPEN";

        /// <summary>화면에 뜨는 가장 긴 문구 — 변환기가 글자 크기를 이 문구에 맞춘다.</summary>
        public const string LongestScreenText = LockedText;

        [Header("레버 · 패널")]
        [Tooltip("레버 아이템 프리팹 (FireDoorLever 가 붙은 NetworkObject)")]
        [SerializeField] private NetworkObject leverPrefab;

        [Tooltip("양쪽 패널 (번호 순서 0 · 1)")]
        [SerializeField] private FireDoorPanel[] panels = new FireDoorPanel[FireDoorRules.PanelCount];

        [Header("시간 (초)")]
        [Tooltip("모든 레버를 끼운 뒤 내려야 하는 시간 — 기획서 \"30초의 제한 시간\"")]
        [SerializeField] private float countdown = 30f;

        [Tooltip("남은 시간이 이 초 이하면 빨간 숫자")]
        [SerializeField] private int warningSeconds = 10;

        [SerializeField] private float pullDuration = 0.3f;
        [SerializeField] private float popDuration = 0.4f;

        [Tooltip("튕겨 나갈 때 포물선 꼭대기 높이 (m)")]
        [SerializeField] private float popPeak = 0.5f;

        [SerializeField] private float failedScreenTime = 2f;
        [SerializeField] private float openDuration = 2f;

        [Header("연출")]
        [SerializeField] private FireDoorVisual visual;

        [Networked, Capacity(FireDoorRules.PanelCount)] private NetworkArray<NetworkBool> Mounted => default;
        [Networked, Capacity(FireDoorRules.PanelCount)] private NetworkArray<float> PulledTime => default;
        [Networked] private float CountdownStart { get; set; }
        [Networked] private float FailStart { get; set; }
        [Networked] private float OpenStart { get; set; }

        // 호스트 전용 (동기화하지 않음 — FD13)
        private readonly List<FireDoorLever> levers = new List<FireDoorLever>();
        private readonly bool[] mountedBuffer = new bool[FireDoorRules.PanelCount];
        private readonly float[] pulledBuffer = new float[FireDoorRules.PanelCount];
        private bool popSpawned = true;
        private bool validSetup;

        // 화면 문구는 바뀔 때만 만든다
        private readonly int[] shownKey = { int.MinValue, int.MinValue };
        private readonly string[] shownText = new string[FireDoorRules.PanelCount];

        protected override void OnStationSpawned()
        {
            base.OnStationSpawned();

            validSetup = ValidateSetup();

            if (!validSetup || !HasStateAuthority)
                return;

            // Networked 기본값 0 이면 "0초에 시작 · 튕김 · 열림"으로 보인다 → 없음(−1)으로 시작
            ClearPanels();
            CountdownStart = FireDoorRules.NotStarted;
            FailStart = FireDoorRules.NotStarted;
            OpenStart = FireDoorRules.NotStarted;
            PlaceLevers();
        }

        /// <summary>호스트: 레버가 우클릭으로 패널에 끼우기를 요청한다 (FireDoorLever.UseAsStateAuthority). 받아들이면 true.</summary>
        public bool RequestMount(PlayerRef actor, FireDoorLever lever, int panelIndex)
        {
            if (!HasStateAuthority || !validSetup || Completed || lever == null || actor.IsNone)
                return false;

            if (panelIndex < 0 || panelIndex >= FireDoorRules.PanelCount || Mounted[panelIndex])
                return false;

            float now = Runner.SimulationTime;

            // 30초 중 · 튕겨 내는 중에는 끼울 수 없다
            if (CountdownStart >= 0f || (FailStart >= 0f && now < FailStart + popDuration))
                return false;

            if (!levers.Contains(lever) || !IsHeldBy(lever, actor))
                return false;

            // 해금 전 시도는 거부만 한다 (FD2 — 기획서 실패 조건, 벌 없음)
            if (!AreOtherTeamMissionsCompleted())
                return false;

            // 공통 자격 검사만 하고 바로 해제 (FD7): 범인 · TG005 없음 · 행동 불가 · 범위 밖이면 거부
            if (!TryBeginOperation(actor))
                return false;

            CancelOperation();

            Mounted.Set(panelIndex, true);
            PulledTime.Set(panelIndex, FireDoorRules.NotStarted);
            levers.Remove(lever);
            MissionItems.Despawn(Runner, lever);

            // 기획서 "모든 레버가 장착되면 30초의 제한 시간이 시작된다"
            if (FireDoorRules.AllMounted(ReadMounted()))
                CountdownStart = now;

            return true;
        }

        /// <summary>호스트: 꽂힌 레버 클릭 = 내리기 (FD4). 30초 중 · 끼움 · 아직 안 내림일 때만. 둘 다 내리면 문이 열린다.</summary>
        protected override void OnPartPressed(PlayerRef actor, int partIndex)
        {
            if (!validSetup || Completed || partIndex < 0 || partIndex >= FireDoorRules.PanelCount)
                return;

            float now = Runner.SimulationTime;

            if (!FireDoorRules.IsCounting(now, CountdownStart, countdown) || !Mounted[partIndex] || PulledTime[partIndex] >= 0f)
                return;

            if (!TryBeginOperation(actor))
                return;

            CancelOperation();
            PulledTime.Set(partIndex, now);

            if (!FireDoorRules.AllPulled(ReadPulled()))
                return;

            // 성공: 잠금 해제 → 방화문이 열린다 (기획서)
            OpenStart = now;
            CountdownStart = FireDoorRules.NotStarted;
            Debug.Log($"[FireDoorStation] {name}: 30초 안에 레버를 모두 내림 → 방화문 열림");
            CompleteBy(actor);
        }

        /// <summary>호스트: 30초 끝이면 실패(튕겨 냄), 튕겨 나가는 움직임이 끝나면 그 자리에 레버 아이템을 만든다.</summary>
        protected override void OnHostTickAlways()
        {
            base.OnHostTickAlways();

            if (!validSetup || Completed)
                return;

            float now = Runner.SimulationTime;

            if (FireDoorRules.IsTimeUp(now, CountdownStart, countdown))
                Fail(now);

            if (!popSpawned && FailStart >= 0f && now >= FailStart + popDuration)
                SpawnPoppedLevers();
        }

        /// <summary>호스트: 미션 초기화 (TG005 에 시간이 없어 평소엔 오지 않는다 — 안전용). 패널 · 시각을 비우고 레버를 다시 놓는다.</summary>
        protected override void OnResetHost()
        {
            base.OnResetHost();

            if (!validSetup)
                return;

            ClearPanels();
            CountdownStart = FireDoorRules.NotStarted;
            FailStart = FireDoorRules.NotStarted;
            OpenStart = FireDoorRules.NotStarted;
            popSpawned = true;

            for (int i = levers.Count - 1; i >= 0; i--)
                MissionItems.Despawn(Runner, levers[i]);

            levers.Clear();
            PlaceLevers();
        }

        protected override void OnStationRender()
        {
            base.OnStationRender();

            if (!validSetup || Object == null || !Object.IsValid)
                return;

            float t = Object.RenderTime;
            bool unlocked = AreOtherTeamMissionsCompleted();
            bool counting = FireDoorRules.IsCounting(t, CountdownStart, countdown);
            bool failedShowing = FireDoorRules.IsShowing(t, FailStart, failedScreenTime);
            int seconds = FireDoorRules.CountdownSeconds(t, CountdownStart, countdown);
            float pop = FireDoorRules.Progress01(t, FailStart, popDuration);
            bool popping = pop >= 0f && pop < 1f;

            for (int i = 0; i < panels.Length; i++)
            {
                FireDoorRules.PanelScreen screen = FireDoorRules.ScreenFor(Completed, unlocked, Mounted[i], counting, failedShowing);
                ShowScreen(i, screen, seconds);

                float pull = PulledTime[i] >= 0f ? FireDoorRules.Progress01(t, PulledTime[i], pullDuration) : 0f;
                panels[i].ShowLever(Mounted[i], pull, popping ? pop : -1f, popPeak);
            }

            visual.ShowOpen(FireDoorRules.Progress01(t, OpenStart, openDuration));
        }

        /// <summary>호스트: 30초 안에 못 내림 — 끼운 레버(내린 것 포함)를 모두 튕겨 낸다 (FD5). 레버 아이템은 움직임이 끝난 뒤 만든다.</summary>
        private void Fail(float now)
        {
            FailStart = now;
            CountdownStart = FireDoorRules.NotStarted;
            ClearPanels();
            popSpawned = false;
            Debug.Log($"[FireDoorStation] {name}: 30초 안에 레버를 모두 내리지 못함 → 레버가 튕겨 나온다");
        }

        private void SpawnPoppedLevers()
        {
            popSpawned = true;

            foreach (FireDoorPanel panel in panels)
            {
                Transform at = panel.Landing != null ? panel.Landing : panel.transform;
                NetworkObject spawned = Runner.Spawn(leverPrefab, at.position, at.rotation);
                FireDoorLever lever = spawned != null ? spawned.GetComponent<FireDoorLever>() : null;

                if (lever != null)
                    levers.Add(lever);
            }
        }

        /// <summary>호스트: 방화문 주변 자리 후보 중 2곳에 레버를 놓는다 (FD6).</summary>
        private void PlaceLevers()
        {
            ItemSpots spots = ItemSpots.Find(LeverSpotGroup);

            if (spots == null || spots.Count == 0)
            {
                Debug.LogError($"[FireDoorStation] {name}: 씬에 ItemSpots(묶음 \"{LeverSpotGroup}\" — 레버 자리 후보)가 없어 레버를 놓지 못했습니다.");
                return;
            }

            int[] picked = new int[FireDoorRules.PanelCount];
            int count = LifeSupportRules.PickSpots(spots.Count, picked, n => Random.Range(0, n));

            if (count < FireDoorRules.PanelCount)
                Debug.LogWarning($"[FireDoorStation] {name}: 자리 후보가 {spots.Count}곳뿐이라 레버를 {count}개만 놓았습니다.");

            StringBuilder log = new StringBuilder();

            for (int i = 0; i < count; i++)
            {
                Transform spot = spots.Get(picked[i]);
                NetworkObject spawned = Runner.Spawn(leverPrefab, spot.position, spot.rotation);
                FireDoorLever lever = spawned != null ? spawned.GetComponent<FireDoorLever>() : null;

                if (lever != null)
                    levers.Add(lever);

                log.Append(spot.name).Append(' ');
            }

            Debug.Log($"[FireDoorStation] {name}: 레버 {count}개 배치 — {log}");
        }

        private void ClearPanels()
        {
            for (int i = 0; i < FireDoorRules.PanelCount; i++)
            {
                Mounted.Set(i, false);
                PulledTime.Set(i, FireDoorRules.NotStarted);
            }
        }

        private bool[] ReadMounted()
        {
            for (int i = 0; i < FireDoorRules.PanelCount; i++)
                mountedBuffer[i] = Mounted[i];

            return mountedBuffer;
        }

        private float[] ReadPulled()
        {
            for (int i = 0; i < FireDoorRules.PanelCount; i++)
                pulledBuffer[i] = PulledTime[i];

            return pulledBuffer;
        }

        private bool IsHeldBy(FireDoorLever lever, PlayerRef actor)
        {
            return Runner.TryGetPlayerObject(actor, out NetworkObject playerObject)
                && playerObject != null
                && lever.HolderObject == playerObject;
        }

        private void ShowScreen(int panel, FireDoorRules.PanelScreen screen, int seconds)
        {
            int key = (int)screen * 1000 + (screen == FireDoorRules.PanelScreen.Countdown ? seconds : 0);

            if (key != shownKey[panel])
            {
                shownKey[panel] = key;
                shownText[panel] = screen switch
                {
                    FireDoorRules.PanelScreen.Locked => LockedText,
                    FireDoorRules.PanelScreen.Ready => ReadyText,
                    FireDoorRules.PanelScreen.Wait => WaitText,
                    FireDoorRules.PanelScreen.Countdown => seconds.ToString(),
                    FireDoorRules.PanelScreen.Failed => FailedText,
                    _ => OpenText,
                };
            }

            bool error = screen == FireDoorRules.PanelScreen.Locked
                || screen == FireDoorRules.PanelScreen.Failed
                || (screen == FireDoorRules.PanelScreen.Countdown && FireDoorRules.IsWarning(seconds, warningSeconds));

            panels[panel].ShowScreen(shownText[panel], error);
        }

        /// <summary>레버 프리팹(FireDoorLever) · 패널 2개(번호 = 순서) · 연출이 연결됐는가.</summary>
        private bool ValidateSetup()
        {
            bool ok = visual != null && leverPrefab != null && leverPrefab.GetComponent<FireDoorLever>() != null
                && panels != null && panels.Length == FireDoorRules.PanelCount;

            for (int i = 0; ok && i < FireDoorRules.PanelCount; i++)
                ok = panels[i] != null && panels[i].Index == i;

            if (!ok)
                Debug.LogError($"[FireDoorStation] {name}: 레버 프리팹(FireDoorLever) · 패널 2개(번호 순서) · 연출(FireDoorVisual)이 연결돼야 합니다.");

            return ok;
        }
    }
}
