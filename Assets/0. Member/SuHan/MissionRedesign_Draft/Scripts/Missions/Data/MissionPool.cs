using System.Collections.Generic;
using UnityEngine;

namespace TrustNoOne.Missions
{
    /// <summary>
    /// [역할] 전체 미션 정의 목록(Pool)과 "이번 판에 몇 개 뽑을지" 설정을 담는 데이터 에셋.
    ///
    /// [기획서 근거] "게임 시작 시 서버가 단체 미션 Pool에서 설정된 개수만큼 랜덤으로 선택한다"
    ///  (예: Pool 7개 중 4개 사용), 개인 미션은 역할 배정 후 Pool에서 플레이어별로 선택.
    ///
    /// [왜 문자열 ID 대신 "인덱스"로 네트워크에 싣는가]
    ///  모든 클라이언트가 같은 빌드의 같은 Pool 에셋을 갖고 있으므로 리스트 순서가 같다.
    ///  인덱스(int)는 문자열보다 패킷이 작고 비교가 빠르다.
    ///  ⚠ 그래서 **Pool 리스트의 순서를 배포 후에 바꾸면 호스트/클라이언트 빌드 간 불일치가 생긴다.** 같은 빌드끼리만 접속하게 할 것.
    /// </summary>
    [CreateAssetMenu(menuName = "TrustNoOne/Mission/Mission Pool", fileName = "MissionPool")]
    public class MissionPool : ScriptableObject
    {
        [Header("전체 미션 정의")]
        [SerializeField] private List<MissionDefinition> definitions = new List<MissionDefinition>();

        [Header("이번 판에 뽑을 개수")]
        [Min(0)] [SerializeField] private int teamMissionCount = 3;
        [Min(0)] [SerializeField] private int personalMissionsPerCitizen = 2;

        [Tooltip("기획서에 살인마 미션 본문이 없어 기본값 0. 기획이 채워지면 올린다.")]
        [Min(0)] [SerializeField] private int killerMissionsPerKiller = 0;

        [Tooltip("행동 목표는 기본적으로 플레이어당 1개.")]
        [Min(0)] [SerializeField] private int actionGoalsPerPlayer = 1;

        public int TeamMissionCount => teamMissionCount;
        public int PersonalMissionsPerCitizen => personalMissionsPerCitizen;
        public int KillerMissionsPerKiller => killerMissionsPerKiller;
        public int ActionGoalsPerPlayer => actionGoalsPerPlayer;
        public int Count => definitions.Count;

        private Dictionary<MissionDefinition, int> indexLookup;

        /// <summary>네트워크에서 받은 인덱스로 정의를 찾는다. 범위를 벗어나면 null.</summary>
        public MissionDefinition Get(int index)
        {
            if (index < 0 || index >= definitions.Count)
                return null;

            return definitions[index];
        }

        /// <summary>정의의 Pool 내 인덱스. 없으면 -1.</summary>
        public int IndexOf(MissionDefinition definition)
        {
            if (definition == null)
                return -1;

            if (indexLookup == null)
                BuildLookup();

            return indexLookup.TryGetValue(definition, out int index) ? index : -1;
        }

        /// <summary>
        /// 카테고리에 맞는 정의를 result 에 채운다 (기존 내용은 지움).
        /// null 과 같은 ID 중복은 걸러낸다 (기존 MissionAssignment 의 중복 방지와 같은 안전장치).
        /// </summary>
        public void GetByCategory(MissionCategory category, List<MissionDefinition> result)
        {
            result.Clear();

            HashSet<string> seenIds = new HashSet<string>();

            foreach (MissionDefinition definition in definitions)
            {
                if (definition == null || definition.Category != category)
                    continue;

                if (!seenIds.Add(definition.Id))
                    continue;

                result.Add(definition);
            }
        }

        private void BuildLookup()
        {
            indexLookup = new Dictionary<MissionDefinition, int>();

            for (int i = 0; i < definitions.Count; i++)
            {
                if (definitions[i] != null && !indexLookup.ContainsKey(definitions[i]))
                    indexLookup.Add(definitions[i], i);
            }
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            indexLookup = null;

            HashSet<string> seenIds = new HashSet<string>();

            foreach (MissionDefinition definition in definitions)
            {
                if (definition == null)
                    continue;

                if (!seenIds.Add(definition.Id))
                    Debug.LogWarning($"[MissionPool] 중복 ID 발견: {definition.Id} ({definition.name}). 생성 시 하나만 사용됩니다.", this);
            }
        }
#endif
    }
}
