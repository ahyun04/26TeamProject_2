using UnityEngine;

namespace TrustNoOne.Missions
{
    /// <summary>
    /// [역할] 청소기 1인칭 모델의 흡입구 위치를 알려 준다. 옛 FirstPersonVacuumView 와 같다. 2c 명세 3-7.
    ///  들고 있는 본인 화면에서 먼지가 이 위치로 날아간다 (VacuumTool.GetLocalMouth).
    /// [옮긴 이유] 옛 스크립트가 옛 미션 폴더(Scripts/Mission/Vacuum)에 있어, 교체 단계에서 폴더를 지우면 1인칭 모델이 깨진다.
    /// </summary>
    public class VacuumFirstPersonView : MonoBehaviour
    {
        [SerializeField] private Transform mouth;

        public Transform Mouth => mouth;
    }
}
