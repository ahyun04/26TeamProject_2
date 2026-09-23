using System.Collections.Generic;
using Fusion;

namespace TrustNoOne.Missions
{
    /// <summary>Custom 목표 생성 함수의 모양.</summary>
    public delegate IMissionObjective ObjectiveBuilder(
        MissionDefinition definition, PlayerRef owner, IReadOnlyCollection<PlayerRef> participants);

    /// <summary>
    /// [역할] MissionDefinition → IMissionObjective 변환. Kind 에 따라 알맞은 구현체를 만든다.
    ///
    /// [확장 지점] "특정 플레이어와 3분 이상 함께 이동" 같은 특수 목표는
    ///   factory.Register("WalkTogether", (def, owner, participants) => new WalkTogetherObjective(...));
    ///   처럼 키로 등록하고, MissionDefinition 에서 Kind=Custom + customObjectiveKey 로 가리킨다.
    ///   → MissionGenerator/Router/Manager 는 수정할 필요가 없다.
    ///
    /// [왜 static 이 아닌가] static 레지스트리는 도메인 리로드 비활성화 설정에서 이전 판의 등록이 남는다.
    ///  인스턴스로 두고 MissionManager 가 소유하게 해서 수명을 명확히 했다.
    /// </summary>
    public class ObjectiveFactory
    {
        private readonly Dictionary<string, ObjectiveBuilder> customBuilders = new Dictionary<string, ObjectiveBuilder>();

        public void Register(string key, ObjectiveBuilder builder)
        {
            customBuilders[key] = builder;
        }

        /// <summary>만들 수 없으면 null (Custom 키 미등록 등). 호출한 쪽이 경고를 남긴다.</summary>
        public IMissionObjective Create(
            MissionDefinition definition, PlayerRef owner, IReadOnlyCollection<PlayerRef> participants)
        {
            switch (definition.Kind)
            {
                case ObjectiveKind.Count:
                    return new CountObjective(definition, owner, participants);

                case ObjectiveKind.Avoid:
                    return new AvoidObjective(definition, owner, participants);

                case ObjectiveKind.Custom:
                    if (!string.IsNullOrEmpty(definition.CustomObjectiveKey) &&
                        customBuilders.TryGetValue(definition.CustomObjectiveKey, out ObjectiveBuilder builder))
                    {
                        return builder(definition, owner, participants);
                    }

                    return null;

                default:
                    return null;
            }
        }
    }
}
