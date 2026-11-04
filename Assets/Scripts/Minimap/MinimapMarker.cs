using System.Collections.Generic;
using UnityEngine;

namespace TrustNoOne.Minimap
{
    public enum MinimapMarkerKind
    {
        Mission, //미션 장치 위치 (missionId = 미션 정의의 Id, 예: "TM005")
        Exit,    //탈출구 위치
        Room     //방·주요 시설 이름 (전체 지도에만 표시)
    }

    /// <summary>
    /// 씬의 오브젝트에 붙여 미니맵·지도에 표시할 위치를 알려 준다.
    /// - 미션 장치: kind = Mission, Mission Id 에 미션 정의 Id 를 적는다. 같은 Id 를 여러 장치에 붙여도 된다 (모두 표시).
    ///   미션 정보는 이 PC 에 공개된 것(내 미션 + 단체 미션)만 화면에 나오므로 남의 개인 미션 위치는 표시되지 않는다.
    /// - 탈출구: kind = Exit
    /// - 방 이름: kind = Room, Label 에 "중앙 홀" 같은 이름
    /// </summary>
    public class MinimapMarker : MonoBehaviour
    {
        private static readonly List<MinimapMarker> all = new List<MinimapMarker>();
        public static IReadOnlyList<MinimapMarker> All => all;

        [SerializeField] private MinimapMarkerKind kind = MinimapMarkerKind.Mission;
        [Tooltip("kind = Mission 일 때: 미션 정의의 Id (MissionListUI 목록에 나오는 미션의 Definition.Id)")]
        [SerializeField] private string missionId;
        [Tooltip("kind = Room 일 때: 지도에 표시할 이름")]
        [SerializeField] private string label;

        public MinimapMarkerKind Kind => kind;
        public string MissionId => missionId;
        public string Label => label;
        public Vector3 Position => transform.position;

        private void OnEnable()
        {
            if (!all.Contains(this)) all.Add(this);
        }

        private void OnDisable()
        {
            all.Remove(this);
        }
    }
}