using System.Collections.Generic;
using System.Text;
using Fusion;
using UnityEngine;

namespace TrustNoOne.Missions
{
    /// <summary>
    /// [역할] 생명 유지 장치 본체 한 대 (단체 미션 TG004 "생명 유지 장치 복구" · LifeSupportRestored). 3b 명세 3-3 → 7d 명세.
    ///  본체 3대가 각각 이 장치 하나다 (발전기처럼 — 7d L3B1). 산소통을 우클릭으로 받아 5초 뒤 판정한다.
    ///  정상 → 이 본체 성공 · 잠김(진행 +1, 3대면 미션 완료), 위험 → 이 본체만 폭발(반경 피해) 후 바로 다시 넣을 수 있다 — 다른 본체 · 진행은 그대로 (L3B4).
    ///
    /// [기획서 (3)] "본체에 하나씩 넣고 넣은후 5초후에 터져 실패이거나 성공여부가 결정 / 본체는 총 3개이고 본체 하나씩 성공 /
    ///  본체 하나 성공 후 그 다음 본체가 실패한다해도 성공한 본체는 그대로 유지 / 실패하면 다른 산소통으로 교체후 재시도".
    /// [개수 · 시간은 미션 데이터가 센다 — LS10 · L3B5] TG004 = 개수 3(성공한 본체 수) · 첫 진행부터 120초. 시간 초과면 공통 알림
    ///  (OnMissionReset → ResetStation → OnResetHost)으로 3대 모두 처음부터 — 잠긴 본체도 풀린다.
    /// [시간 초과 때 산소통 — 7e RT1 ~ RT3] 치우지 않는다 (옮겨 둔 통 · 손에 든 통 그대로). 본체마다 자기가 놓은 통 중 넣어서 사라진 만큼만
    ///  원래 구성(정상 1 · 위험 1)이 되도록 새로 놓는다. 피드백 "옮겨 놓고 오래 두면 제자리로 리스폰" — 전부 치우고 새로 놓던 것이 원인이었다.
    /// [판정 중 화면 — L3B3 · L3B9] 정상 · 위험 구분 없이 CHECK 5 → 1. 결과는 폭발 또는 SUCCESS 로만 드러난다 (피드백).
    /// [산소통 — L3B6 · L3B7] 본체마다 2개(정상 1 · 위험 1)를 놓고, 넣기는 어느 본체가 놓은 통이든 받는다.
    /// [넣은 사람 — LS12] 넣는 순간 공통 자격 검사(TryBeginOperation: 이번 판 미션 · 행동 가능 · 범위 · 다른 행동 중 아님)만 하고 바로 해제한다.
    ///  7b-2 부터 살인마도 넣는다. 넣은 뒤 떠나도(위험하면 피해도) 판정은 진행된다.
    /// [동기화 — LS14] 시각 공식. 넣은 시각 · 넣은 통의 위험 여부 · 폭발 시각만 동기화하고, 카운트다운 · 섬광은
    ///  각 PC 가 Object.RenderTime 으로 계산한다. 산소통 목록 · 넣은 사람은 호스트만 안다 (만들고 지우는 것도 호스트뿐).
    /// [팀원 코드 — LS5 · LS13] PlayerItemController.DropCurrentItem(손에서 내려놓기 — 공용 도우미 MissionItems) · PlayerHealth.ApplyDamage 를 부르기만 한다.
    /// [IItemUseTarget] 아이템 우클릭 대상이라는 표시 — 변환기의 조준 검증이 이 장치를 "입력을 받는 대상"으로 본다 (2c F10).
    /// [근거] 3b 명세 LS5 · LS12 · LS14, 7d 명세 L3B1 ~ L3B11, 7e (7d 명세 7장) RT1 ~ RT4
    /// </summary>
    public class LifeSupportStation : MissionStation, IItemUseTarget
    {
        public const string CheckText = "CHECK";
        public const string EmptyText = "EMPTY";
        public const string DamagedText = "DAMAGED";
        public const string SuccessText = "SUCCESS";

        /// <summary>화면에 뜨는 가장 긴 문구 — 변환기가 글자 크기를 이 문구에 맞춘다.</summary>
        public const string LongestScreenText = SuccessText;

        [Header("산소통")]
        [Tooltip("산소통 아이템 프리팹 (OxygenTank 가 붙은 NetworkObject)")]
        [SerializeField] private NetworkObject tankPrefab;

        [Tooltip("이 본체가 맵에 놓을 산소통 수 (7d L3B6 — 본체 3대면 6개)")]
        [Min(1)]
        [SerializeField] private int tankCount = 2;

        [Tooltip("그중 정상 산소통 수 (7d L3B6). 본체 한 대는 정상 통 1개로 성공한다")]
        [Min(1)]
        [SerializeField] private int safeTanks = 1;

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

        [Header("산소통 배치 (1층 NavMesh 위 무작위 — 6b 보강 NS6)")]
        [Tooltip("산소통끼리 최소 거리 (m)")]
        [Min(0f)]
        [SerializeField] private float tankSpacing = 4f;

        [Tooltip("본체에서 최소 거리 (m)")]
        [Min(0f)]
        [SerializeField] private float tankMinFromStation = 3f;

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
            InsertStart = LifeSupportRules.NotStarted;
            ExplodeStart = LifeSupportRules.NotStarted;
        }

        /// <summary>호스트: 미션 배정 직후 — 이번 판에 생명 유지 장치 미션이 뽑혔을 때만 산소통을 놓는다 (7a 명세 TC11 · TC12). 다시 배정되면 먼저 치운다.</summary>
        protected override void OnMissionsAssignedHost(bool usedThisRound)
        {
            base.OnMissionsAssignedHost(usedThisRound);

            if (!validSetup)
                return;

            ClearTanks();

            if (usedThisRound)
            {
                // 판 시작: 원래 구성 전부 (정상 safeTanks · 위험 나머지 — 7d L3B6)
                LifeSupportRules.MissingTanks(tankCount, safeTanks, 0, 0, out int safe, out int dangerous);
                PlaceTanks(safe, dangerous);
            }
            else
                Debug.Log($"[LifeSupportStation] {name}: 이번 판에 뽑히지 않아 산소통을 놓지 않습니다.");
        }

        /// <summary>호스트: 산소통이 우클릭으로 넣기를 요청한다 (OxygenTank.UseAsStateAuthority). 받아들이면 true. 어느 본체가 놓은 통이든 받는다 (7d L3B7).</summary>
        public bool RequestInsert(PlayerRef actor, OxygenTank tank)
        {
            if (!HasStateAuthority || !validSetup || Completed || tank == null || actor.IsNone)
                return false;

            // 한 번에 하나 · 성공한 본체는 받지 않음 (L3B8). 어느 본체가 놓은 통이든 그 사람이 들고 있으면 된다 (L3B7)
            if (!LifeSupportRules.CanInsert(Completed, InsertStart >= 0f) || !IsHeldBy(tank, actor))
                return false;

            // 공통 자격 검사만 하고 바로 해제 (LS12): TG004 없음 · 행동 불가 · 범위 밖 · 다른 행동 중이면 거부
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

            // 정상 → 이 본체 성공 · 잠김 (진행 +1 — 3대면 TG004 완료, L3B1)
            CompleteBy(actor);
        }

        /// <summary>
        /// 호스트: 시간 초과 초기화 (7d L3B5 — 3대 모두 처음부터; 폭발은 더 이상 여기로 오지 않는다). 폭발 시각은 남긴다 — 초기화 뒤에도 섬광 · DAMAGED 가 보이게.
        /// 산소통은 치우지 않고 모자란 만큼만 채운다 (7e RT1 ~ RT3). 판정 중이던 통은 넣을 때 이미 사라졌으므로 채우는 쪽에 들어간다.
        /// </summary>
        protected override void OnResetHost()
        {
            base.OnResetHost();

            InsertStart = LifeSupportRules.NotStarted;
            insertedBy = PlayerRef.None;

            if (!validSetup)
                return;

            RefillTanks();
        }

        protected override void OnStationRender()
        {
            base.OnStationRender();

            if (!validSetup || Object == null || !Object.IsValid)
                return;

            float t = Object.RenderTime;

            float flash = LifeSupportRules.FlashProgress(t, ExplodeStart, flashDuration);
            visual.ShowFlash(flash >= 0f ? flash * blastRadius * 2f : 0f);

            bool counting = LifeSupportRules.IsCounting(t, InsertStart, countdown);
            bool damaged = LifeSupportRules.IsShowing(t, ExplodeStart, damagedScreenTime);

            // 꽂힌 산소통: 판정 중 + 성공한 본체 (L3B10 — "성공한 본체는 그대로"가 보이게). 압력계 색이 없는 모델이라 결과가 드러나지 않는다
            visual.ShowInserted(counting || Completed);

            // 판정 중에는 정상 · 위험 구분 없이 CHECK n (L3B3 · L3B9)
            LifeSupportScreen screen = LifeSupportRules.ScreenFor(Completed, counting, damaged);
            ShowOnScreen(screen, screen == LifeSupportScreen.Check ? LifeSupportRules.CountdownSeconds(t, InsertStart, countdown) : 0);
        }

        /// <summary>호스트: 위험한 통 폭발 — 반경 안 피해. 이 본체만 처리하고 미션 실패는 요청하지 않는다 → 다른 본체 · 진행 수 유지, 바로 다시 넣을 수 있다 (7d L3B4 · LS5).</summary>
        private void Explode()
        {
            ExplodeStart = Runner.SimulationTime;
            int hit = DamagePlayersInBlast();
            Debug.Log($"[LifeSupportStation] {name}: 위험한 산소통 폭발 — 반경 {blastRadius:0.#}m 안 {hit}명 피해. 이 본체만 — 다른 본체 · 진행 수는 그대로");
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

        /// <summary>
        /// 호스트: 시간 초과 (7e RT1 ~ RT3) — 남은 산소통(옮겨 둔 통 · 손에 든 통 포함)은 그대로 두고,
        /// 이 본체가 놓은 통 중 넣어서 사라진 만큼만 원래 구성(정상 safeTanks · 위험 나머지)이 되도록 새로 놓는다.
        /// </summary>
        private void RefillTanks()
        {
            RemoveGoneTanks();

            int aliveSafe = 0;
            int aliveDangerous = 0;

            foreach (OxygenTank tank in tanks)
            {
                if (tank.Dangerous)
                    aliveDangerous++;
                else
                    aliveSafe++;
            }

            LifeSupportRules.MissingTanks(tankCount, safeTanks, aliveSafe, aliveDangerous, out int safe, out int dangerous);
            Debug.Log($"[LifeSupportStation] {name}: 시간 초과 — 남은 산소통 {tanks.Count}개 그대로, 새로 {safe + dangerous}개(정상 {safe} · 위험 {dangerous})");

            if (safe + dangerous > 0)
                PlaceTanks(safe, dangerous);
        }

        /// <summary>호스트: 넣어서(다른 본체에 넣은 것 포함 — 7d L3B7) 사라진 산소통을 목록에서 뺀다 (7e RT4).</summary>
        private void RemoveGoneTanks()
        {
            tanks.RemoveAll(tank => tank == null || tank.Object == null || !tank.Object.IsValid);
        }

        /// <summary>
        /// 호스트: 1층 NavMesh 위 무작위 지점에 정상 safeCount 개 · 위험 dangerousCount 개를 새로 만든다 (6b 보강 NS6).
        /// 판 시작엔 원래 구성 전부(7d L3B6), 시간 초과엔 모자란 만큼(7e RT3). 간격은 이 본체 통끼리만 — 남아 있는 통도 포함 (L3B11 · 7e RT3).
        /// </summary>
        private void PlaceTanks(int safeCount, int dangerousCount)
        {
            int wanted = safeCount + dangerousCount;

            if (!NavMeshSpawnArea.TryCreate(name, out NavMeshSpawnArea area))
                return;

            if (!area.TryGetOrigin(transform, out Vector3 origin))
            {
                Debug.LogError($"[LifeSupportStation] {name}: 본체 근처(3m)에 1층 NavMesh 가 없어 산소통을 놓지 못했습니다 (본체 위치 · NavMesh 범위 확인).");
                return;
            }

            Vector3[] stationOnly = { transform.position };
            List<Vector3> points = new List<Vector3>();

            // 간격 기준: 이 본체의 남은 산소통 + 이번에 고른 자리 (7e — 채운 통이 남은 통 옆에 붙지 않게)
            List<Vector3> occupied = new List<Vector3>();

            foreach (OxygenTank tank in tanks)
            {
                if (tank != null)
                    occupied.Add(tank.transform.position);
            }

            for (int i = 0; i < wanted; i++)
            {
                // 본체에서 3m · 다른 산소통에서 4m 이상, 본체에서 걸어갈 수 있는 곳 (NS5 · NS6)
                if (area.TryPickAnywhere(origin,
                        p => SpawnPointRules.IsFarFromAll(p, stationOnly, tankMinFromStation) && SpawnPointRules.IsFarFromAll(p, occupied, tankSpacing),
                        out Vector3 point))
                {
                    points.Add(point);
                    occupied.Add(point);
                }
            }

            int count = points.Count;

            if (count < wanted)
                Debug.LogWarning($"[LifeSupportStation] {name}: 산소통 자리를 {count}/{wanted}개만 찾았습니다 (NavMesh 가 좁거나 간격이 큼).");

            if (count < safeCount)
                Debug.LogWarning($"[LifeSupportStation] {name}: 정상 산소통이 {safeCount}개가 안 됩니다.");

            bool[] dangerous = new bool[count];
            LifeSupportRules.DrawDangers(dangerous, safeCount, n => Random.Range(0, n));

            StringBuilder log = new StringBuilder();

            for (int i = 0; i < count; i++)
            {
                bool danger = dangerous[i];

                NetworkObject spawned = Runner.Spawn(tankPrefab, points[i], NavMeshSpawnArea.RandomYaw(),
                    onBeforeSpawned: (runner, obj) => obj.GetComponent<OxygenTank>().HostSetDangerous(danger));

                OxygenTank tank = spawned != null ? spawned.GetComponent<OxygenTank>() : null;

                if (tank != null)
                    tanks.Add(tank);

                log.Append(NavMeshSpawnArea.Describe(points[i])).Append(danger ? "(위험) " : "(정상) ");
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

        private void ShowOnScreen(LifeSupportScreen screen, int number)
        {
            if (display == null)
                return;

            int key = (int)screen * 1000 + number;

            if (key != shownKey)
            {
                shownKey = key;
                shownText = screen switch
                {
                    LifeSupportScreen.Check => $"{CheckText} {number}",
                    LifeSupportScreen.Damaged => DamagedText,
                    LifeSupportScreen.Success => SuccessText,
                    _ => EmptyText,
                };
            }

            // 빨간 글자는 폭발 뒤에만 (판정 중에는 결과를 드러내지 않게 — L3B3)
            display.Show(shownText, screen == LifeSupportScreen.Damaged);
        }
    }
}
