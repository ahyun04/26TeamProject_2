using System.Collections.Generic;
using System.Text;
using Fusion;
using UnityEngine;

namespace TrustNoOne.Missions
{
    /// <summary>
    /// [역할] 고장난 장비 (단체 미션 TG003 "고장난 장비 조립" · EquipmentAssembled). 신규. 3c 명세 3-3.
    ///  부품 4개(색마다 1개)를 맵에 놓고, 부품을 든 사람이 맞는 색 자리를 조준하고 우클릭하면 그 자리에 붙인다 (7c — 자동 배치 · 바닥 원 없앰).
    ///  붙을 때마다 진행 1 (패널 TG003 n/4), 4번째에 완료(잠금) — 붙은 부품이 초록으로 빛난다.
    ///
    /// [기획서] 단체 미션 기획서 · 전체 기획서 (2): "필요한 부품은 총 4개 / 조립 위치에 부품을 가져오면 해당 부품이 장비에 자동으로 배치 /
    ///  모든 부품이 모이면 장비가 자동으로 조립 / 잘못된 부품을 가져와도 인정되지 않음 / 제한 시간이 있는 경우 … 실패 시 초기화".
    /// [끼우기 — 7c AR1 · AR4 · AR5] 부품 우클릭(EquipmentPart) → 조준한 자리(AssemblySlot) → RequestAttach: 이 장비의 부품 · 든 사람 · 같은 색 빈 자리(CanAttach) · 공통 자격.
    /// [자격 — EA8] 붙이는 순간 공통 자격 검사(TryBeginOperation: 이번 판 미션 · 행동 가능 · 범위 · 다른 행동 중 아님)만 하고 바로 해제한다. 7b-2 부터 살인마도 끼운다.
    ///  범위 기준점 = 장비 루트, 범위 4m (7c AR6 — 자리에서 3m 인 팀원 검사와 맞춤). 거부되면 부품은 손에 남는다.
    /// [제한 시간 — EA3] 기본 없음. TG003 데이터에 시간을 넣으면 초과 시 공통 알림(OnMissionReset → ResetStation → OnResetHost)으로
    ///  붙은 부품이 사라지고 부품을 다시 흩는다.
    /// [동기화 — EA12] 자리별 붙은 시각 4개만. 날아오기는 각 PC 가 Object.RenderTime 으로 계산한다. 부품 목록은 호스트만 안다.
    /// [배치 — EA4 → 6b 보강 NS7] 1층 NavMesh 위 무작위: 무작위 1색은 장비에서 3.5 ~ 6m(7c — 원 조건 없앰, AR10), 나머지는 장비에서 10m 이상 · 서로 4m 이상.
    /// </summary>
    public class AssemblyStation : MissionStation
    {
        private static readonly string[] ColorNames = { "파랑", "회색", "빨강", "노랑" };

        [Header("부품")]
        [Tooltip("부품 아이템 프리팹 4개 (색 순서: 파랑 · 회색 · 빨강 · 노랑 — EquipmentPart 의 색 번호와 같아야 한다)")]
        [SerializeField] private NetworkObject[] partPrefabs = new NetworkObject[AssemblyRules.PartCount];

        [Header("자리 (7c)")]
        [Tooltip("자리 4개 (색 순서: 파랑 · 회색 · 빨강 · 노랑) — 조준해서 우클릭하면 그 색 부품이 끼워진다")]
        [SerializeField] private AssemblySlot[] slots = new AssemblySlot[AssemblyRules.PartCount];

        [Header("연출")]
        [SerializeField] private AssemblyVisual visual;

        [Tooltip("부품이 자리로 날아가는 시간 (초, EA6)")]
        [Min(0f)]
        [SerializeField] private float flyDuration = 0.4f;

        [Header("부품 배치 (1층 NavMesh 위 무작위 — 6b 보강 NS7)")]
        [Tooltip("장비 근처에 놓을 부품 1개: 장비에서 최소 거리 (m)")]
        [Min(0f)]
        [SerializeField] private float nearMin = 3.5f;

        [Tooltip("장비 근처에 놓을 부품 1개: 장비에서 최대 거리 (m)")]
        [Min(0f)]
        [SerializeField] private float nearMax = 6f;

        [Tooltip("나머지 부품: 장비에서 최소 거리 (m)")]
        [Min(0f)]
        [SerializeField] private float farMin = 10f;

        [Tooltip("부품끼리 최소 거리 (m)")]
        [Min(0f)]
        [SerializeField] private float partSpacing = 4f;

        [Networked, Capacity(AssemblyRules.PartCount)] private NetworkArray<float> AttachTime => default;

        // 호스트 전용 (동기화하지 않음 — EA12)
        private readonly List<EquipmentPart> parts = new List<EquipmentPart>();
        private readonly float[] attachBuffer = new float[AssemblyRules.PartCount];
        private bool validSetup;

        protected override void OnStationSpawned()
        {
            base.OnStationSpawned();

            validSetup = ValidateSetup();

            if (!validSetup || !HasStateAuthority)
                return;

            // Networked 기본값 0 이면 "0초에 붙은 자리"로 보인다 → 빈 자리(−1)로 시작
            ClearAttached();
        }

        /// <summary>호스트: 미션 배정 직후 — 이번 판에 장비 조립 미션이 뽑혔을 때만 부품을 놓는다 (7a 명세 TC11 · TC12). 다시 배정되면 먼저 치운다.</summary>
        protected override void OnMissionsAssignedHost(bool usedThisRound)
        {
            base.OnMissionsAssignedHost(usedThisRound);

            if (!validSetup)
                return;

            for (int i = parts.Count - 1; i >= 0; i--)
                MissionItems.Despawn(Runner, parts[i]);

            parts.Clear();

            if (usedThisRound)
                PlaceParts();
            else
                Debug.Log($"[AssemblyStation] {name}: 이번 판에 뽑히지 않아 부품을 놓지 않습니다.");
        }

        /// <summary>
        /// 호스트: 부품 우클릭(EquipmentPart.UseAsStateAuthority)이 조준한 자리에 끼우기를 요청한다 (7c 명세 AR4 · AR5). 받아들이면 true.
        /// 이 장비의 부품 · 그 사람이 들고 있음 · 같은 색 빈 자리 · 공통 자격 검사. 하나라도 어긋나면 조용히 거부 — 부품은 손에 남는다.
        /// </summary>
        public bool RequestAttach(PlayerRef actor, EquipmentPart part, int slotColor)
        {
            if (!HasStateAuthority || !validSetup || Completed || part == null || actor.IsNone)
                return false;

            if (slotColor < 0 || slotColor >= AssemblyRules.PartCount)
                return false;

            if (!parts.Contains(part) || part.HolderObject == null || part.HolderObject.InputAuthority != actor)
                return false;

            if (!AssemblyRules.CanAttach(part.ColorIndex, slotColor, AttachTime[slotColor] < 0f))
                return false;

            // 공통 자격 검사만 하고 바로 해제 (EA8): 이번 판 미션 · 행동 가능 · 범위(루트 4m) · 다른 행동 중 아님
            if (!TryBeginOperation(actor))
                return false;

            CancelOperation();
            Attach(slotColor, actor, part);
            return true;
        }

        /// <summary>호스트: 시간 초과 초기화 (TG003 에 시간이 있을 때만, EA3). 붙은 부품을 비우고 부품을 다시 흩는다.</summary>
        protected override void OnResetHost()
        {
            base.OnResetHost();

            if (!validSetup)
                return;

            ClearAttached();

            for (int i = parts.Count - 1; i >= 0; i--)
                MissionItems.Despawn(Runner, parts[i]);

            parts.Clear();
            PlaceParts();
        }

        protected override void OnStationRender()
        {
            base.OnStationRender();

            if (!validSetup || Object == null || !Object.IsValid)
                return;

            float t = Object.RenderTime;

            for (int c = 0; c < AssemblyRules.PartCount; c++)
                visual.ShowPart(c, AssemblyRules.FlyProgress(t, AttachTime[c], flyDuration));

            // 자리 조준: 내 미션 && 빈 자리일 때만 (7c AR8 — 붙은 자리 · 완료 · 내 미션 아님이면 조준되지 않게)
            for (int c = 0; c < AssemblyRules.PartCount; c++)
                slots[c].SetColliderEnabled(LocalInteractionAllowed && AttachTime[c] < 0f);

            visual.ShowDone(Completed);
        }

        /// <summary>호스트: 부품을 자리에 붙인다 — 손에서 지우고, 진행 1 (4번째면 완료).</summary>
        private void Attach(int color, PlayerRef actor, EquipmentPart part)
        {
            AttachTime.Set(color, Runner.SimulationTime);
            parts.Remove(part);
            MissionItems.Despawn(Runner, part);

            int attached = CountAttached();
            Debug.Log($"[AssemblyStation] {name}: {ColorNames[color]} 부품 붙음 ({attached}/{AssemblyRules.PartCount})");

            if (attached >= AssemblyRules.PartCount)
                CompleteBy(actor);
            else
                PublishProgress(actor);
        }

        /// <summary>호스트: 1층 NavMesh 위 무작위로 부품을 놓는다 — 무작위 1색은 장비 근처, 나머지는 멀리 (EA4 → 6b 보강 NS7).</summary>
        private void PlaceParts()
        {
            if (!NavMeshSpawnArea.TryCreate(name, out NavMeshSpawnArea area))
                return;

            if (!area.TryGetOrigin(transform, out Vector3 origin))
            {
                Debug.LogError($"[AssemblyStation] {name}: 장비 근처(3m)에 1층 NavMesh 가 없어 부품을 놓지 못했습니다 (장비 위치 · NavMesh 범위 확인).");
                return;
            }

            Vector3 machine = transform.position;
            Vector3[] machineOnly = { machine };
            List<Vector3> placed = new List<Vector3>();
            // 장비에 오면 무엇을 찾을지 보이게 — 근처에 둘 색 하나는 판마다 무작위 (EA4 와 같은 뜻)
            int nearColor = Random.Range(0, AssemblyRules.PartCount);
            StringBuilder log = new StringBuilder();

            for (int c = 0; c < AssemblyRules.PartCount; c++)
            {
                bool near = c == nearColor;
                Vector3 at;
                bool found;

                if (near)
                    found = area.TryPickNear(origin, machine, nearMin, nearMax,
                        p => SpawnPointRules.IsFarFromAll(p, placed, partSpacing),
                        out at);
                else
                    found = area.TryPickAnywhere(origin,
                        p => SpawnPointRules.IsFarFromAll(p, machineOnly, farMin) && SpawnPointRules.IsFarFromAll(p, placed, partSpacing),
                        out at);

                if (!found)
                {
                    Debug.LogWarning($"[AssemblyStation] {name}: {ColorNames[c]} 부품 자리를 찾지 못했습니다 ({(near ? "근처" : "멀리")}).");
                    continue;
                }

                placed.Add(at);

                NetworkObject spawned = Runner.Spawn(partPrefabs[c], at, NavMeshSpawnArea.RandomYaw());
                EquipmentPart part = spawned != null ? spawned.GetComponent<EquipmentPart>() : null;

                if (part != null)
                    parts.Add(part);

                log.Append(ColorNames[c]).Append(near ? ": 근처 " : ": 멀리 ").Append(NavMeshSpawnArea.Describe(at)).Append("  ");
            }

            Debug.Log($"[AssemblyStation] {name}: 부품 {placed.Count}개 배치 — {log}");
        }

        private void ClearAttached()
        {
            for (int c = 0; c < AssemblyRules.PartCount; c++)
                AttachTime.Set(c, AssemblyRules.NotAttached);
        }

        private int CountAttached()
        {
            for (int c = 0; c < AssemblyRules.PartCount; c++)
                attachBuffer[c] = AttachTime[c];

            return AssemblyRules.CountAttached(attachBuffer);
        }

        /// <summary>부품 프리팹 4개(색 번호 = 자리 순서) · 연출 · 자리 4개가 연결됐는가.</summary>
        private bool ValidateSetup()
        {
            bool ok = visual != null && partPrefabs != null && partPrefabs.Length == AssemblyRules.PartCount && slots != null && slots.Length == AssemblyRules.PartCount;

            for (int c = 0; ok && c < AssemblyRules.PartCount; c++)
            {
                EquipmentPart part = partPrefabs[c] != null ? partPrefabs[c].GetComponent<EquipmentPart>() : null;
                ok = part != null && part.ColorIndex == c && slots[c] != null && slots[c].ColorIndex == c;
            }

            if (!ok)
                Debug.LogError($"[AssemblyStation] {name}: 부품 프리팹 4개(색 순서) · 연출(AssemblyVisual) · 자리 4개(AssemblySlot, 색 순서)가 연결돼야 합니다.");

            return ok;
        }
    }
}
