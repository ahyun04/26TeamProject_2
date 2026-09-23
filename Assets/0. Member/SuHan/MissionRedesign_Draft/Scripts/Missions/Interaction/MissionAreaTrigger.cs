using System.Collections.Generic;
using Fusion;
using UnityEngine;

namespace TrustNoOne.Missions
{
    /// <summary>
    /// [역할] 플레이어가 이 구역에 "처음 들어온 순간" 방문 사실(MissionEvent)을 알린다.
    ///        예) 의료실 방문(PM004 → MedicalRoomVisited), 특정 지역 방문(행동 목표 → AreaVisited).
    ///
    /// [설계 근거] 설계 6-2의 "행동 이벤트 발행원" 확장 지점. 미션 코어는 수정하지 않고,
    ///  이 컴포넌트가 IMissionEventSink.Publish 만 호출한다. 실제 맵의 의료실에도 그대로 쓸 수 있다.
    ///
    /// [왜 OnTriggerEnter 가 아니라 위치 검사인가]
    ///  물리 트리거 이벤트는 한쪽에 Rigidbody 가 있어야 발생하는데, 플레이어는 SimpleKCC 로 움직여서
    ///  트리거 이벤트가 온다는 보장이 없다. 대신 호스트가 매 프레임 "플레이어 위치가 구역 안인가"를 직접 본다.
    ///  플레이어 수가 최대 10명이라 비용은 무시할 만하다.
    ///
    /// [판정 위치] 호스트(StateAuthority)에서만 검사한다. 클라이언트는 아무것도 하지 않는다.
    ///  수행자는 Runner.ActivePlayers 의 PlayerRef 라서 클라이언트가 조작할 수 없다.
    ///
    /// [한 번만] 같은 플레이어는 한 판에 한 번만 알린다 ("방문" 미션이므로). 미션이 다시 초기화되면 기록도 비운다.
    ///
    /// [NetworkObject 불필요] 상태를 동기화하지 않으므로 일반 MonoBehaviour 로 씬에 바로 배치한다
    ///  (MissionStation 처럼 Runner.Spawn 할 필요가 없다).
    /// </summary>
    public class MissionAreaTrigger : MonoBehaviour
    {
        [Tooltip("구역에 들어왔을 때 발행할 행동 종류. 예) MedicalRoomVisited, AreaVisited")]
        [SerializeField] private MissionEventType visitEvent = MissionEventType.AreaVisited;

        [Tooltip("MissionEvent.TargetId 로 실린다 (어느 구역인지 구분이 필요할 때).")]
        [SerializeField] private int areaId;

        [Tooltip("구역 크기 (가로, 높이, 세로). 이 오브젝트 위치가 바닥 중앙이다.")]
        [SerializeField] private Vector3 size = new Vector3(6f, 4f, 6f);

        [Tooltip("비워두면 씬에서 자동 검색.")]
        [SerializeField] private MissionManager manager;

        private readonly HashSet<PlayerRef> visited = new HashSet<PlayerRef>();

        private void Update()
        {
            if (manager == null)
            {
                manager = FindFirstObjectByType<MissionManager>();

                if (manager == null)
                    return;
            }

            NetworkObject managerObject = manager.Object;

            if (managerObject == null || !managerObject.IsValid || !managerObject.HasStateAuthority)
                return;

            if (!manager.Initialized)
            {
                visited.Clear();
                return;
            }

            NetworkRunner runner = manager.Runner;
            Bounds area = GetWorldBounds();

            foreach (PlayerRef player in runner.ActivePlayers)
            {
                if (visited.Contains(player))
                    continue;

                if (!runner.TryGetPlayerObject(player, out NetworkObject playerObject) || playerObject == null)
                    continue;

                if (!area.Contains(playerObject.transform.position))
                    continue;

                visited.Add(player);
                manager.Publish(new MissionEvent(visitEvent, player, areaId));
            }
        }

        private Bounds GetWorldBounds()
        {
            Vector3 center = transform.position + Vector3.up * (size.y * 0.5f);
            return new Bounds(center, size);
        }

        private void OnDrawGizmos()
        {
            Bounds area = GetWorldBounds();
            Gizmos.color = new Color(0.6f, 0.3f, 1f, 0.35f);
            Gizmos.DrawCube(area.center, area.size);
            Gizmos.color = new Color(0.6f, 0.3f, 1f, 1f);
            Gizmos.DrawWireCube(area.center, area.size);
        }
    }
}
