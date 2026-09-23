using System.Collections.Generic;
using UnityEngine;

namespace TrustNoOne.Missions
{
    /// <summary>
    /// [역할] 미션 하나의 "정의" (읽기 전용 원본 데이터). 런타임 진행도는 여기에 저장하지 않는다.
    ///        진행도는 IMissionObjective 인스턴스가 가진다 → 같은 정의가 플레이어마다 독립적으로 진행될 수 있다.
    ///
    /// [기획서 근거] 미션 ID(TM001/PM001), 미션 종류 표, 충돌 검사 예시
    ///  ("개인 미션: 아이템 전달" vs "행동 목표: 누구에게도 아이템을 주지 말아라" → 충돌).
    ///
    /// [설계 포인트]
    ///  - Requires/Forbids 는 "자동 유도 + 수동 보강" 구조다.
    ///      Count형 → Trigger 는 자동으로 Requires 에 포함
    ///      Avoid형 → Trigger 는 자동으로 Forbids 에 포함
    ///    인스펙터의 배열은 간접 관계(예: "의료실 방문"은 이동을 요구)에만 쓰면 된다.
    ///    → 기획자가 배열을 비워서 충돌 검사가 무력화되는 실수를 막는다.
    /// </summary>
    [CreateAssetMenu(menuName = "TrustNoOne/Mission/Mission Definition", fileName = "MissionDefinition")]
    public class MissionDefinition : ScriptableObject
    {
        [Header("식별")]
        [Tooltip("기획서 ID. 예) TM001, PM003. Pool 안에서 유일해야 한다.")]
        [SerializeField] private string id;

        [SerializeField] private string displayName;

        [Header("분류")]
        [SerializeField] private MissionCategory category;
        [SerializeField] private ObjectiveKind kind;

        [Header("판정 조건")]
        [Tooltip("이 미션이 반응하는 행동 종류. Count형=이 행동을 해야 함 / Avoid형=이 행동을 하면 안 됨.")]
        [SerializeField] private MissionEventType trigger;

        [Tooltip("Count형: 몇 번 해야 완료인가. Avoid형은 무시된다(위반 즉시 실패).")]
        [Min(1)]
        [SerializeField] private int requiredCount = 1;

        [Tooltip("Custom형일 때 ObjectiveFactory에 등록한 키.")]
        [SerializeField] private string customObjectiveKey;

        [Header("충돌 검사 (간접 관계만 수동 입력)")]
        [Tooltip("이 미션을 수행하려면 반드시 해야 하는 행동 (Count형의 Trigger는 자동 포함).")]
        [SerializeField] private MissionEventType[] extraRequires;

        [Tooltip("이 미션이 하지 말라고 하는 행동 (Avoid형의 Trigger는 자동 포함).")]
        [SerializeField] private MissionEventType[] extraForbids;

        public string Id => id;
        public string DisplayName => displayName;
        public MissionCategory Category => category;
        public ObjectiveKind Kind => kind;
        public MissionEventType Trigger => trigger;
        public int RequiredCount => Mathf.Max(1, requiredCount);
        public string CustomObjectiveKey => customObjectiveKey;

        /// <summary>이 미션을 하려면 event 를 반드시 해야 하는가? (충돌 검사용)</summary>
        public bool RequiresEvent(MissionEventType eventType)
        {
            if (eventType == MissionEventType.None)
                return false;

            if (kind == ObjectiveKind.Count && trigger == eventType)
                return true;

            return Contains(extraRequires, eventType);
        }

        /// <summary>이 미션은 event 를 하지 말라고 하는가? (충돌 검사용)</summary>
        public bool ForbidsEvent(MissionEventType eventType)
        {
            if (eventType == MissionEventType.None)
                return false;

            if (kind == ObjectiveKind.Avoid && trigger == eventType)
                return true;

            return Contains(extraForbids, eventType);
        }

        /// <summary>금지 행동 전체(자동 + 수동). MissionConflict 가 열거해서 상대의 Requires 와 교차 검사한다.</summary>
        public IEnumerable<MissionEventType> EnumerateForbids()
        {
            if (kind == ObjectiveKind.Avoid && trigger != MissionEventType.None)
                yield return trigger;

            if (extraForbids != null)
            {
                foreach (MissionEventType forbidden in extraForbids)
                    yield return forbidden;
            }
        }

        /// <summary>요구 행동 전체(자동 + 수동).</summary>
        public IEnumerable<MissionEventType> EnumerateRequires()
        {
            if (kind == ObjectiveKind.Count && trigger != MissionEventType.None)
                yield return trigger;

            if (extraRequires != null)
            {
                foreach (MissionEventType required in extraRequires)
                    yield return required;
            }
        }

        private static bool Contains(MissionEventType[] array, MissionEventType value)
        {
            if (array == null)
                return false;

            for (int i = 0; i < array.Length; i++)
            {
                if (array[i] == value)
                    return true;
            }

            return false;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (string.IsNullOrWhiteSpace(id))
                Debug.LogWarning($"[MissionDefinition] '{name}' 의 ID 가 비어 있습니다.", this);

            if (kind != ObjectiveKind.Custom && trigger == MissionEventType.None)
                Debug.LogWarning($"[MissionDefinition] '{name}' 의 Trigger 가 None 입니다. 어떤 행동에도 반응하지 않습니다.", this);

            if (kind == ObjectiveKind.Custom && string.IsNullOrWhiteSpace(customObjectiveKey))
                Debug.LogWarning($"[MissionDefinition] '{name}' 는 Custom 형인데 customObjectiveKey 가 비어 있습니다.", this);
        }
#endif
    }
}
