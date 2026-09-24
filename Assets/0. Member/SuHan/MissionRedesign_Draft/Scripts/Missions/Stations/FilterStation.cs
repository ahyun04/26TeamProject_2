using Fusion;
using UnityEngine;

namespace TrustNoOne.Missions
{
    /// <summary>
    /// [역할] 필터 청소 (개인 미션 PS006 "필터 청소하기" · FilterCleaned). 옛 FilterCleaningMission + VacuumDust 의 상태 · 규칙. 2c 명세 3-4.
    ///  청소기(VacuumTool)를 든 채 먼지를 우클릭 → RequestSuction → 0.6초 흡입 → 치움 → 전부 치우면 완료 → 다시 생김(ResetForNext)
    ///
    /// [상태를 여기서 모두 갖는 이유 — F2] 먼지 5개가 각자 조작자 · 중단 규칙을 들면 규칙이 흩어진다. 먼지는 주소 + 모습만(FilterDust).
    /// [한 번에 하나 — F3] SuckingIndex 하나만 둔다.
    /// [취소 — F4] 흡입 중 청소기를 놓치면(들고 있는 사람이 바뀜) 그 먼지만 취소.
    ///  범위 이탈 · 행동 불가는 베이스가 조작을 취소 → 먼지 전부 원래대로 ("중단하면 처음부터", 차단기 · 전선과 같음).
    /// [조작자] 첫 흡입에서 TryBeginOperation (권한 · 범위 · 내 미션), 이후는 조작자 본인만. 조작 중인 사람은 busy (차단기와 같음).
    /// [먼지 콜라이더 — F8] "내 미션(LocalInteractionAllowed) && 안 치운 먼지"일 때만 켠다. 베이스 interactionColliders 는 쓰지 않는다.
    /// </summary>
    public class FilterStation : MissionStation
    {
        public const int MaxDusts = 8;

        [Header("먼지 (인덱스 = 먼지 번호)")]
        [SerializeField] private FilterDust[] dusts;

        [Networked, Capacity(MaxDusts)]
        private NetworkArray<NetworkBool> Cleaned => default;

        [Networked] private int SuckingIndex { get; set; }
        [Networked] private float SuctionProgress { get; set; }
        [Networked] private float SuctionDuration { get; set; }
        [Networked] private NetworkObject SuctionTool { get; set; }

        private bool validSetup;
        private bool[] buffer;
        private NetworkObject cachedToolObject;
        private VacuumTool cachedTool;

        private int DustCount => dusts == null ? 0 : Mathf.Min(dusts.Length, MaxDusts);

        protected override void OnStationSpawned()
        {
            base.OnStationSpawned();

            validSetup = ValidateSetup();

            if (!validSetup)
            {
                Debug.LogError($"[FilterStation] {name}: 먼지는 1~{MaxDusts}개이고 빈칸이 없어야 합니다.");
                return;
            }

            if (HasStateAuthority)
                ResetDusts();

            ApplyVisuals();
        }

        /// <summary>모든 피어: 이 먼지를 지금 빨아들일 수 있어 보이는가 (청소기 외곽선 판단용 — 실제 허용은 RequestSuction).</summary>
        public bool CanSuckLocally(int dustIndex)
        {
            if (!validSetup || Object == null || !Object.IsValid)
                return false;

            return FilterRules.CanStartSuction(ReadCleaned(), SuckingIndex, dustIndex);
        }

        /// <summary>호스트: 청소기가 우클릭으로 흡입을 요청한다 (VacuumTool.UseAsStateAuthority). 받아들이면 true.</summary>
        public bool RequestSuction(PlayerRef actor, int dustIndex, VacuumTool tool, float duration)
        {
            if (!HasStateAuthority || !validSetup || Completed || tool == null || actor.IsNone)
                return false;

            if (!FilterRules.CanStartSuction(ReadCleaned(), SuckingIndex, dustIndex))
                return false;

            if (!IsHeldBy(tool, actor))
                return false;

            if (Operator.IsNone)
            {
                if (!TryBeginOperation(actor))
                    return false;
            }
            else if (Operator != actor)
            {
                return false;
            }

            SuckingIndex = dustIndex;
            SuctionProgress = 0f;
            SuctionDuration = Mathf.Max(0.05f, duration);
            SuctionTool = tool.Object;
            return true;
        }

        protected override void OnHostTick(PlayerRef actor)
        {
            base.OnHostTick(actor);

            if (!validSetup || SuckingIndex == FilterRules.NoDust)
                return;

            VacuumTool tool = ResolveTool();

            // F4: 청소기를 놓치면 그 먼지만 취소 (치운 먼지는 그대로)
            if (tool == null || !IsHeldBy(tool, actor))
            {
                StopSuction();
                return;
            }

            SuctionProgress = Mathf.Min(1f, SuctionProgress + Runner.DeltaTime / Mathf.Max(0.05f, SuctionDuration));

            if (SuctionProgress < 1f)
                return;

            Cleaned.Set(SuckingIndex, true);
            StopSuction();

            if (FilterRules.AllCleaned(ReadCleaned()))
                CompleteBy(actor);
        }

        protected override void OnOperationCanceled(PlayerRef actor)
        {
            base.OnOperationCanceled(actor);
            ResetDusts();
        }

        protected override void OnResetHost()
        {
            base.OnResetHost();
            ResetDusts();
        }

        protected override void OnStationRender()
        {
            base.OnStationRender();
            ApplyVisuals();
        }

        private void ResetDusts()
        {
            if (!validSetup)
                return;

            for (int i = 0; i < DustCount; i++)
                Cleaned.Set(i, false);

            StopSuction();
        }

        private void StopSuction()
        {
            SuckingIndex = FilterRules.NoDust;
            SuctionProgress = 0f;
            SuctionTool = null;
        }

        private void ApplyVisuals()
        {
            if (!validSetup || Object == null || !Object.IsValid)
                return;

            Transform mouth = null;

            if (SuckingIndex != FilterRules.NoDust)
            {
                VacuumTool tool = ResolveTool();

                if (tool != null)
                    mouth = tool.GetLocalMouth();
            }

            float t = Mathf.SmoothStep(0f, 1f, SuctionProgress);

            for (int i = 0; i < DustCount; i++)
            {
                bool cleaned = Cleaned[i];
                dusts[i].ApplyVisual(cleaned, SuckingIndex == i, t, mouth);
                dusts[i].SetColliderEnabled(LocalInteractionAllowed && !cleaned);
            }
        }

        private bool IsHeldBy(VacuumTool tool, PlayerRef actor)
        {
            return Runner.TryGetPlayerObject(actor, out NetworkObject playerObject)
                && playerObject != null
                && tool.HolderObject == playerObject;
        }

        private VacuumTool ResolveTool()
        {
            if (cachedToolObject != SuctionTool)
            {
                cachedToolObject = SuctionTool;
                cachedTool = cachedToolObject != null ? cachedToolObject.GetComponent<VacuumTool>() : null;
            }

            return cachedTool;
        }

        private bool[] ReadCleaned()
        {
            if (buffer == null || buffer.Length != DustCount)
                buffer = new bool[DustCount];

            for (int i = 0; i < buffer.Length; i++)
                buffer[i] = Cleaned[i];

            return buffer;
        }

        private bool ValidateSetup()
        {
            if (dusts == null || dusts.Length == 0 || dusts.Length > MaxDusts)
                return false;

            foreach (FilterDust dust in dusts)
            {
                if (dust == null)
                    return false;
            }

            return true;
        }
    }
}
