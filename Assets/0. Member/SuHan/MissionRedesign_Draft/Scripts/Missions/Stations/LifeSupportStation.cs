using System.Collections.Generic;
using System.Text;
using Fusion;
using UnityEngine;

namespace TrustNoOne.Missions
{
    /// <summary>
    /// [역할] 생명 유지 장치 본체 (단체 미션 TG004 "생명 유지 장치 복구" · LifeSupportRestored). 신규. 3b 명세 3-3.
    ///  산소통 6개(정상 3 · 위험 3)를 맵에 놓고, 플레이어가 든 산소통을 우클릭으로 받아 5초 뒤 판정한다.
    ///  정상 → 공급 1개 (3번째면 완료 · 잠금), 위험 → 폭발(반경 피해) + 미션 실패(처음부터, 산소통을 새로 놓음).
    ///
    /// [기획서] 전체 기획서 (2) 생명 유지 장치: "산소통마다 정상 또는 위험 상태가 무작위 / 본체에 하나씩 넣고 넣은후 5초후에 터져
    ///  실패이거나 성공여부가 결정 / 제한 시간 내 안전한 산소통을 모두 정상화 / 실패하면 다른 산소통으로 교체후 재시도".
    /// [개수 · 시간은 미션 데이터가 센다 — LS10] TG004 = 개수 3 · 첫 진행부터 120초. 이 장치는 공급마다 진행 이벤트(PublishProgress)를 보내고
    ///  3번째에 완료(CompleteBy)한다. 제한 시간 초과와 폭발은 같은 알림(OnMissionReset → ResetStation → OnResetHost)으로 초기화된다 (LS4 · LS11).
    /// [넣은 사람 — LS12] 넣는 순간 공통 자격 검사(TryBeginOperation: 시민 · TG004 · 행동 가능 · 범위 · 다른 행동 중 아님)만 하고 바로 해제한다.
    ///  넣은 뒤 떠나도(위험하면 피해도) 판정은 진행된다.
    /// [동기화 — LS14] 시각 공식. 공급 수 · 넣은 시각 · 넣은 통의 위험 여부 · 폭발 시각만 동기화하고, 카운트다운 · 깜빡임 · 섬광은
    ///  각 PC 가 Object.RenderTime 으로 계산한다. 산소통 목록 · 넣은 사람은 호스트만 안다 (만들고 지우는 것도 호스트뿐).
    /// [팀원 코드 — LS5 · LS13] PlayerItemController.DropCurrentItem(손에서 내려놓기 — 공용 도우미 MissionItems) · PlayerHealth.ApplyDamage 를 부르기만 한다.
    /// [IItemUseTarget] 아이템 우클릭 대상이라는 표시 — 변환기의 조준 검증이 이 장치를 "입력을 받는 대상"으로 본다 (2c F10).
    /// </summary>
    public class LifeSupportStation : MissionStation, IItemUseTarget
    {
        public const string CheckText = "CHECK";
        public const string WarningText = "WARNING";
        public const string DamagedText = "DAMAGED";
        public const string SuccessText = "SUCCESS";

        /// <summary>화면에 뜨는 가장 긴 문구 — 변환기가 글자 크기를 이 문구에 맞춘다.</summary>
        public const string LongestScreenText = WarningText + " 5";

        /// <summary>산소통 자리 후보 묶음 이름 (ItemSpots, LS7).</summary>
        public const string TankSpotGroup = "OxygenTank";

        private const int ScreenIdle = 0;
        private const int ScreenCheck = 1;
        private const int ScreenWarning = 2;
        private const int ScreenDamaged = 3;
        private const int ScreenSuccess = 4;

        [Header("산소통")]
        [Tooltip("산소통 아이템 프리팹 (OxygenTank 가 붙은 NetworkObject)")]
        [SerializeField] private NetworkObject tankPrefab;

        [Tooltip("맵에 놓을 산소통 수 (LS2)")]
        [Min(1)]
        [SerializeField] private int tankCount = 6;

        [Tooltip("필요한 공급 수 = 정상 산소통 수. TG004 미션 데이터의 개수와 같아야 한다 (LS2 · LS10)")]
        [Min(1)]
        [SerializeField] private int requiredSupplies = 3;

        [Header("판정 · 폭발")]
        [Tooltip("넣은 뒤 판정까지 (초) — 기획서 \"넣은후 5초후\"")]
        [Min(0.1f)]
        [SerializeField] private float countdown = 5f;

        [Tooltip("폭발 피해 반경 (m, LS5)")]
        [Min(0f)]
        [SerializeField] private float blastRadius = 3f;

        [Tooltip("폭발 피해 = 최대 체력 × 이 비율 (LS5 — 일단 30%, 플레이 후 조정)")]
        [Range(0f, 1f)]
        [SerializeField] private float blastDamageRatio = 0.3f;

        [Header("연출")]
        [SerializeField] private LifeSupportVisual visual;

        [Tooltip("본체 화면 글자. 비어 있으면 화면 없이 동작")]
        [SerializeField] private LcdDisplay display;

        [Tooltip("폭발 섬광이 반경까지 커지는 시간 (초)")]
        [SerializeField] private float flashDuration = 0.5f;

        [Tooltip("폭발 뒤 화면에 DAMAGED 를 띄우는 시간 (초)")]
        [SerializeField] private float damagedScreenTime = 2f;

        [Tooltip("위험한 통 판정 중 경고 램프가 깜빡이는 간격 (초)")]
        [SerializeField] private float blinkPeriod = 0.25f;

        [Networked] private int SuppliedCount { get; set; }
        [Networked] private float InsertStart { get; set; }
        [Networked] private NetworkBool InsertedDangerous { get; set; }
        [Networked] private float ExplodeStart { get; set; }

        // 호스트 전용 (동기화하지 않음 — LS14)
        private readonly List<OxygenTank> tanks = new List<OxygenTank>();
        private PlayerRef insertedBy;

        private bool validSetup;

        // 화면 문구는 바뀔 때만 만든다 (매 Render 문자열 생성 방지)
        private int shownKey = int.MinValue;
        private string shownText = string.Empty;

        /// <summary>대기 화면 문구 "O2 1/3".</summary>
        public static string IdleText(int supplied, int required)
        {
            return $"O2 {supplied}/{required}";
        }

        protected override void OnStationSpawned()
        {
            base.OnStationSpawned();

            validSetup = tankPrefab != null && tankPrefab.GetComponent<OxygenTank>() != null && visual != null;

            if (!validSetup)
            {
                Debug.LogError($"[LifeSupportStation] {name}: 산소통 프리팹(OxygenTank) 또는 연출(LifeSupportVisual)이 연결되지 않았습니다.");
                return;
            }

            if (!HasStateAuthority)
                return;

            // Networked 기본값 0 이면 "0초에 넣은 통 · 0초에 폭발"로 보인다 → 없음(−1)으로 시작
            SuppliedCount = 0;
            InsertStart = LifeSupportRules.NotStarted;
            ExplodeStart = LifeSupportRules.NotStarted;
            PlaceTanks();
        }

        /// <summary>호스트: 산소통이 우클릭으로 넣기를 요청한다 (OxygenTank.UseAsStateAuthority). 받아들이면 true.</summary>
        public bool RequestInsert(PlayerRef actor, OxygenTank tank)
        {
            if (!HasStateAuthority || !validSetup || Completed || tank == null || actor.IsNone)
                return false;

            // 한 번에 하나 (LS2 — 꽂는 자리 1개). 이 장치가 놓은 통을 그 사람이 들고 있어야 한다
            if (InsertStart >= 0f || !tanks.Contains(tank) || !IsHeldBy(tank, actor))
                return false;

            // 공통 자격 검사만 하고 바로 해제 (LS12): 범인 · TG004 없음 · 행동 불가 · 범위 밖 · 다른 행동 중이면 거부
            if (!TryBeginOperation(actor))
                return false;

            CancelOperation();

            InsertedDangerous = tank.Dangerous;
            insertedBy = actor;
            InsertStart = Runner.SimulationTime;
            tanks.Remove(tank);
            MissionItems.Despawn(Runner, tank);
            return true;
        }

        /// <summary>호스트: 넣은 지 5초가 되면 판정 (사람과 무관하게 매 틱 — LS12).</summary>
        protected override void OnHostTickAlways()
        {
            base.OnHostTickAlways();

            if (!validSetup || !LifeSupportRules.IsResolved(Runner.SimulationTime, InsertStart, countdown))
                return;

            bool dangerous = InsertedDangerous;
            PlayerRef actor = insertedBy;
            InsertStart = LifeSupportRules.NotStarted;
            insertedBy = PlayerRef.None;

            if (dangerous)
            {
                Explode();
                return;
            }

            SuppliedCount++;

            if (SuppliedCount >= requiredSupplies)
                CompleteBy(actor);
            else
                PublishProgress(actor);
        }

        /// <summary>호스트: 시간 초과 · 폭발 공통 초기화 (LS4). 폭발 시각은 남긴다 — 초기화 뒤에도 섬광 · DAMAGED 가 보이게.</summary>
        protected override void OnResetHost()
        {
            base.OnResetHost();

            InsertStart = LifeSupportRules.NotStarted;
            insertedBy = PlayerRef.None;
            SuppliedCount = 0;

            if (!validSetup)
                return;

            ClearTanks();
            PlaceTanks();
        }

        protected override void OnStationRender()
        {
            base.OnStationRender();

            if (!validSetup || Object == null || !Object.IsValid)
                return;

            float t = Object.RenderTime;

            float flash = LifeSupportRules.FlashProgress(t, ExplodeStart, flashDuration);
            visual.ShowFlash(flash >= 0f ? flash * blastRadius * 2f : 0f);

            if (Completed)
            {
                visual.ShowInserted(false);
                visual.ShowLamp(LifeSupportVisual.DoneLamp);
                ShowOnScreen(ScreenSuccess, 0, false);
                return;
            }

            bool counting = LifeSupportRules.IsCounting(t, InsertStart, countdown);
            visual.ShowInserted(counting);

            if (counting)
            {
                bool danger = InsertedDangerous;
                int seconds = LifeSupportRules.CountdownSeconds(t, InsertStart, countdown);
                bool blinkOn = danger && LifeSupportRules.IsBlinkOn(t, InsertStart, blinkPeriod);
                visual.ShowLamp(blinkOn ? LifeSupportVisual.WarningLamp : MaterialSwapper.Original);
                ShowOnScreen(danger ? ScreenWarning : ScreenCheck, seconds, danger);
                return;
            }

            visual.ShowLamp(MaterialSwapper.Original);

            if (LifeSupportRules.IsShowing(t, ExplodeStart, damagedScreenTime))
                ShowOnScreen(ScreenDamaged, 0, true);
            else
                ShowOnScreen(ScreenIdle, SuppliedCount, false);
        }

        /// <summary>호스트: 위험한 통 폭발 — 반경 안 피해 → 미션 실패 요청 (LS4 · LS5).</summary>
        private void Explode()
        {
            ExplodeStart = Runner.SimulationTime;
            int hit = DamagePlayersInBlast();
            Debug.Log($"[LifeSupportStation] {name}: 위험한 산소통 폭발 — 반경 {blastRadius:0.#}m 안 {hit}명 피해 → 미션 실패 요청");

            // 받아들여지면 알림으로 ResetStation 이 이미 불렸다. 이번 판에 미션이 없거나 끝났으면 직접 초기화해 산소통이라도 새로 놓는다
            if (!RequestMissionFailure())
                ResetStation();
        }

        private int DamagePlayersInBlast()
        {
            int hit = 0;
            Vector3 center = transform.position;

            foreach (PlayerHealth health in FindObjectsByType<PlayerHealth>(FindObjectsSortMode.None))
            {
                if (health == null || !LifeSupportRules.InBlast(Vector3.Distance(health.transform.position, center), blastRadius))
                    continue;

                health.ApplyDamage(LifeSupportRules.BlastDamage(health.MaxHealth, blastDamageRatio));
                hit++;
            }

            return hit;
        }

        /// <summary>호스트: 자리 후보 중 tankCount 곳을 골라 산소통을 만든다 (정상 = requiredSupplies 개, LS2 · LS7).</summary>
        private void PlaceTanks()
        {
            ItemSpots spots = ItemSpots.Find(TankSpotGroup);

            if (spots == null || spots.Count == 0)
            {
                Debug.LogError($"[LifeSupportStation] {name}: 씬에 ItemSpots(묶음 \"{TankSpotGroup}\" — 산소통 자리 후보)가 없어 산소통을 놓지 못했습니다.");
                return;
            }

            int[] picked = new int[tankCount];
            int count = LifeSupportRules.PickSpots(spots.Count, picked, n => Random.Range(0, n));
            bool[] dangerous = new bool[count];
            LifeSupportRules.DrawDangers(dangerous, requiredSupplies, n => Random.Range(0, n));

            if (count < requiredSupplies)
                Debug.LogWarning($"[LifeSupportStation] {name}: 자리 후보가 {spots.Count}곳뿐이라 정상 산소통이 {requiredSupplies}개가 안 됩니다.");

            StringBuilder log = new StringBuilder();

            for (int i = 0; i < count; i++)
            {
                Transform spot = spots.Get(picked[i]);
                bool danger = dangerous[i];

                NetworkObject spawned = Runner.Spawn(tankPrefab, spot.position, spot.rotation,
                    onBeforeSpawned: (runner, obj) => obj.GetComponent<OxygenTank>().HostSetDangerous(danger));

                OxygenTank tank = spawned != null ? spawned.GetComponent<OxygenTank>() : null;

                if (tank != null)
                    tanks.Add(tank);

                log.Append(spot.name).Append(danger ? "(위험) " : "(정상) ");
            }

            Debug.Log($"[LifeSupportStation] {name}: 산소통 {count}개 배치 — {log}");
        }

        /// <summary>호스트: 이 장치가 놓은 산소통을 모두 지운다 (들고 있으면 손에서 먼저 — LS13).</summary>
        private void ClearTanks()
        {
            for (int i = tanks.Count - 1; i >= 0; i--)
                MissionItems.Despawn(Runner, tanks[i]);

            tanks.Clear();
        }

        private bool IsHeldBy(OxygenTank tank, PlayerRef actor)
        {
            return Runner.TryGetPlayerObject(actor, out NetworkObject playerObject)
                && playerObject != null
                && tank.HolderObject == playerObject;
        }

        private void ShowOnScreen(int screen, int number, bool error)
        {
            if (display == null)
                return;

            int key = screen * 1000 + number;

            if (key != shownKey)
            {
                shownKey = key;
                shownText = screen switch
                {
                    ScreenCheck => $"{CheckText} {number}",
                    ScreenWarning => $"{WarningText} {number}",
                    ScreenDamaged => DamagedText,
                    ScreenSuccess => SuccessText,
                    _ => IdleText(number, requiredSupplies),
                };
            }

            display.Show(shownText, error);
        }
    }
}
