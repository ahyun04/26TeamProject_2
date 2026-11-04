using UnityEngine;

namespace TrustNoOne.Minimap
{
    /// <summary>
    /// 맵 하나의 미니맵 데이터 (기획서: CurrentMapID에 대응하는 맵 이미지와 월드 좌표 범위).
    /// - sceneName : 이 데이터를 쓰는 게임 씬 이름 (예: "4~12")
    /// - mapImage  : 위에서 내려다본 맵 이미지 (에디터 메뉴 Tools/Minimap/Bake Map Image 로 자동 생성 가능)
    /// - worldMin / worldMax : 이미지가 덮는 월드 좌표 범위 (x, z). 이미지 왼쪽 아래 = worldMin, 오른쪽 위 = worldMax
    /// </summary>
    [CreateAssetMenu(menuName = "Minimap/Map Data", fileName = "MinimapMapData")]
    public class MinimapMapData : ScriptableObject
    {
        [SerializeField] private string sceneName = "4~12";
        [SerializeField] private Sprite mapImage;
        [SerializeField] private Vector2 worldMin = new Vector2(-50f, -50f);
        [SerializeField] private Vector2 worldMax = new Vector2(50f, 50f);

        public string SceneName => sceneName;
        public Sprite MapImage => mapImage;
        public Vector2 WorldMin => worldMin;
        public Vector2 WorldMax => worldMax;
        public Vector2 WorldSize => worldMax - worldMin;

        public bool IsValid => mapImage != null && worldMax.x > worldMin.x && worldMax.y > worldMin.y;

        /// <summary>월드 위치(x, z)를 이미지 기준 0~1 좌표로 바꾼다 (왼쪽 아래 = 0,0).</summary>
        public Vector2 WorldToNormalized(Vector3 world)
        {
            Vector2 size = WorldSize;
            return new Vector2((world.x - worldMin.x) / size.x, (world.z - worldMin.y) / size.y);
        }

        /// <summary>에디터 베이크 도구가 값을 채울 때 사용.</summary>
        public void Configure(string scene, Sprite image, Vector2 min, Vector2 max)
        {
            sceneName = scene;
            mapImage = image;
            worldMin = min;
            worldMax = max;
        }
    }
}