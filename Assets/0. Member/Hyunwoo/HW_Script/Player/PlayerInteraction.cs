using Fusion;
using UnityEngine;

namespace HyunWoo
{
    /// <summary>
    /// 카메라 앞의 미션 오브젝트를 감지하고 호스트에게 상호작용을 요청한다.
    /// </summary>
    public class PlayerInteraction : NetworkBehaviour
    {
        [SerializeField] private Transform view;
        [SerializeField] private float range = 3f;
        [SerializeField] private LayerMask missionMask;

        private void Update()
        {
            if (Object == null || !Object.HasInputAuthority)
                return;

            Debug.DrawRay(
                view.position,
                view.forward * range,
                Color.red
            );

            if (!Input.GetKeyDown(KeyCode.F))
                return;

            if (!Runner.GetPhysicsScene().Raycast(
                    view.position,
                    view.forward,
                    out RaycastHit hit,
                    range,
                    missionMask))
            {
                Debug.Log("미션 오브젝트를 감지하지 못함");
                return;
            }

            MissionObject mission =
                hit.collider.GetComponentInParent<MissionObject>();

            if (mission == null)
            {
                Debug.LogWarning("감지한 오브젝트에 MissionObject가 없음");
                return;
            }

            if (mission.Object == null)
            {
                Debug.LogWarning("MissionObject에 NetworkObject가 연결되지 않음");
                return;
            }

            RPC_Interact(mission.Object);
        }

        /// <summary>
        /// 호스트에게 미션 상호작용을 요청한다.
        /// </summary>
        [Rpc(
            RpcSources.InputAuthority,
            RpcTargets.StateAuthority,
            HostMode = RpcHostMode.SourceIsHostPlayer)]
        private void RPC_Interact(
            NetworkObject target,
            RpcInfo info = default)
        {
            if (target == null)
            {
                Debug.LogWarning("RPC 대상 NetworkObject가 없음");
                return;
            }

            // NetworkObject의 자식에 MissionObject가 있어도 찾도록 변경
            MissionObject mission =
                target.GetComponentInChildren<MissionObject>(true);

            if (mission == null)
            {
                Debug.LogWarning("RPC 대상에서 MissionObject를 찾지 못함");
                return;
            }

            float distance = Vector3.Distance(
                transform.position,
                mission.transform.position
            );

            if (distance > range + 0.5f)
            {
                Debug.LogWarning($"상호작용 거리 초과: {distance:F1}");
                return;
            }

            Debug.Log($"호스트 상호작용 실행: {info.Source}");
            mission.Interact(info.Source);
        }
    }
}