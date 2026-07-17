using Fusion;
using UnityEngine;

namespace HyunWoo
{
    /// <summary>
    /// 상호작용 시 연결된 미션 진행도를 증가시킨다
    /// </summary>
    public class MissionObject : NetworkBehaviour
    {
        [SerializeField] private MissionSystem missionSystem;
        [SerializeField] private int missionId;
        [SerializeField] private GameObject visual;
        [SerializeField] private Collider hitbox;

        [Networked] public NetworkBool Used { get; set; }

        public override void Spawned()
        {
            Refresh();
        }

        public override void Render()
        {
            Refresh();
        }

        /// <summary>
        /// 호스트에서 미션 진행도를 증가시킨다
        /// </summary>
        public void Interact(PlayerRef player)
        {
            if (!Object.HasStateAuthority || Used)
                return;

            if (missionSystem == null)
                return;

            if (!missionSystem.AddProgress(missionId, player))
                return;

            Used = true;
        }

        // 사용된 오브젝트를 모든 플레이어 화면에서 숨긴다
        private void Refresh()
        {
            bool active = !Used;

            if (visual != null)
                visual.SetActive(active);

            if (hitbox != null)
                hitbox.enabled = active;
        }
    }

}
