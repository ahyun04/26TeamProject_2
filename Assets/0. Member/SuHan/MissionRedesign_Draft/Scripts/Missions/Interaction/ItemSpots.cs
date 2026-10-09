using UnityEngine;

namespace TrustNoOne.Missions
{
    /// <summary>
    /// [역할] 미션 아이템 자리 후보 목록 (씬에 묶음마다 하나). 자식 Transform 들이 후보 자리다. 3c 명세 3-5.
    ///  장치가 실행 중에 묶음 이름으로 찾아, 판마다 일부를 무작위로 골라 아이템을 놓는다.
    ///  묶음: "OxygenTank"(생명 유지 장치 산소통, 3b LS7) · "EquipmentNear" / "EquipmentFar"(장비 부품 — 장비 주변 / 맵 곳곳, 3c EA4)
    /// [이유] 기획서 "시설 내에 랜덤하게 배치" · "맵 곳곳에 배치" — 완전 무작위면 벽 속 · 공중에 생길 수 있어 자리 후보를 찍어 둔다.
    ///  맵에서는 레벨 담당이 묶음 오브젝트 아래에 빈 오브젝트로 자리만 찍으면 된다.
    /// [네트워크 오브젝트가 아닌 이유] 위치만 알려 준다. 아이템을 만들고 지우는 것은 호스트의 장치가 한다 (PlayerHealthActorGate 처럼 씬에 둔다).
    /// [이름] 3b 에서 OxygenTankSpots 로 만들었다가 3c 에서 함께 쓰려고 바꿨다 (파일 · 메타를 같이 바꿈).
    /// </summary>
    public class ItemSpots : MonoBehaviour
    {
        [Tooltip("묶음 이름 — 장치가 이 이름으로 찾는다 (예: OxygenTank, EquipmentNear, EquipmentFar)")]
        [SerializeField] private string group;

        public string Group => group;

        /// <summary>후보 자리 수 (자식 수).</summary>
        public int Count => transform.childCount;

        /// <summary>index 번째 후보 자리.</summary>
        public Transform Get(int index)
        {
            return transform.GetChild(index);
        }

        /// <summary>씬에서 묶음 이름이 같은 자리 목록 (없으면 null). 장치가 생길 때 · 초기화 때만 부른다.</summary>
        public static ItemSpots Find(string group)
        {
            foreach (ItemSpots spots in FindObjectsByType<ItemSpots>(FindObjectsSortMode.None))
            {
                if (spots.group == group)
                    return spots;
            }

            return null;
        }
    }
}
