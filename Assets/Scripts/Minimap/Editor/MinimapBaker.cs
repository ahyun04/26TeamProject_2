#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

namespace TrustNoOne.Minimap.EditorTools
{
    /// <summary>
    /// 메뉴 Tools/Minimap/Bake Map Image — 지금 열려 있는 씬을 위에서 내려다보고 PNG 로 저장하고,
    /// 월드 좌표 범위까지 계산해 MinimapMapData 에셋을 만든다. (반드시 Editor 폴더 안에 넣을 것)
    /// 사용법: 맵 씬을 열고 → 메뉴 실행 → 만들어진 MinimapMapData 를 MinimapController 의 Map Datas 에 넣는다.
    /// 지붕/천장이 가려서 안 보이면 Camera Height 를 낮추거나 Culling 레이어를 조정한다.
    /// </summary>
    public class MinimapBaker : EditorWindow
    {
        private float padding = 2f;
        private int pixelsPerMeter = 16;
        private float cameraHeight = 200f;
        private float ceilingCutHeight = 6f; // 이 높이 위쪽(지붕)은 찍지 않는다
        private LayerMask layers = ~0;
        private string folder = "Assets/Minimap";
        private bool manualBounds;
        private Vector2 manualMin = new Vector2(-30f, -30f);
        private Vector2 manualMax = new Vector2(30f, 30f);

        [MenuItem("Tools/Minimap/Bake Map Image")]
        private static void Open() { GetWindow<MinimapBaker>("Minimap Baker"); }

        private void OnGUI()
        {
            padding = EditorGUILayout.FloatField("여백(m)", padding);
            pixelsPerMeter = EditorGUILayout.IntSlider("해상도(px/m)", pixelsPerMeter, 4, 64);
            cameraHeight = EditorGUILayout.FloatField("카메라 높이", cameraHeight);
            ceilingCutHeight = EditorGUILayout.FloatField("지붕 제외 높이(바닥 기준 위)", ceilingCutHeight);
            layers = EditorGUILayout.MaskField("찍을 레이어", layers, UnityEditorInternal.InternalEditorUtility.layers);
            folder = EditorGUILayout.TextField("저장 폴더", folder);

            manualBounds = EditorGUILayout.Toggle("범위 직접 지정", manualBounds);
            if (manualBounds)
            {
                manualMin = EditorGUILayout.Vector2Field("최소 (x, z)", manualMin);
                manualMax = EditorGUILayout.Vector2Field("최대 (x, z)", manualMax);
            }

            if (GUILayout.Button("Bake")) Bake();
        }

        private void Bake()
        {
            Bounds b = default;
            bool has = false;
            foreach (Renderer r in FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            {
                if (!r.enabled || r is ParticleSystemRenderer) continue;
                if (!has) { b = r.bounds; has = true; }
                else b.Encapsulate(r.bounds);
            }
            if (!has) { Debug.LogError("[MinimapBaker] 씬에 Renderer 가 없습니다."); return; }

            Vector2 min = new Vector2(b.min.x - padding, b.min.z - padding);
            Vector2 max = new Vector2(b.max.x + padding, b.max.z + padding);
            if (manualBounds) { min = manualMin; max = manualMax; }
            Vector2 size = max - min;
            int w = Mathf.Clamp(Mathf.RoundToInt(size.x * pixelsPerMeter), 64, 4096);
            int h = Mathf.Clamp(Mathf.RoundToInt(size.y * pixelsPerMeter), 64, 4096);

            GameObject camGo = new GameObject("MinimapBakeCam");
            try
            {
                Camera cam = camGo.AddComponent<Camera>();
                cam.orthographic = true;
                cam.orthographicSize = size.y * 0.5f;
                cam.aspect = size.x / size.y;
                cam.transform.position = new Vector3((min.x + max.x) * 0.5f, b.min.y + cameraHeight, (min.y + max.y) * 0.5f);
                cam.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
                cam.nearClipPlane = Mathf.Max(0.1f, cameraHeight - ceilingCutHeight);
                cam.farClipPlane = cameraHeight + b.size.y + 10f;
                cam.cullingMask = layers;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.06f, 0.07f, 0.09f, 1f);

                RenderTexture rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32);
                cam.targetTexture = rt;
                cam.Render();

                RenderTexture prev = RenderTexture.active;
                RenderTexture.active = rt;
                Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
                tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                tex.Apply();
                RenderTexture.active = prev;
                cam.targetTexture = null;
                rt.Release();

                Directory.CreateDirectory(folder);
                string scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
                string safe = scene.Replace("~", "_");
                string pngPath = $"{folder}/Map_{safe}.png";
                File.WriteAllBytes(pngPath, tex.EncodeToPNG());
                DestroyImmediate(tex);
                AssetDatabase.ImportAsset(pngPath);

                TextureImporter imp = (TextureImporter)AssetImporter.GetAtPath(pngPath);
                imp.textureType = TextureImporterType.Sprite;
                imp.spriteImportMode = SpriteImportMode.Single;
                imp.mipmapEnabled = false;
                imp.maxTextureSize = 4096;
                imp.SaveAndReimport();

                Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(pngPath);
                string dataPath = $"{folder}/MapData_{safe}.asset";
                MinimapMapData data = AssetDatabase.LoadAssetAtPath<MinimapMapData>(dataPath);
                if (data == null)
                {
                    data = ScriptableObject.CreateInstance<MinimapMapData>();
                    AssetDatabase.CreateAsset(data, dataPath);
                }
                data.Configure(scene, sprite, min, max);
                EditorUtility.SetDirty(data);
                AssetDatabase.SaveAssets();
                Selection.activeObject = data;
                Debug.Log($"[MinimapBaker] 완료: {pngPath} / {dataPath} (월드 범위 {min} ~ {max})");
            }
            finally
            {
                DestroyImmediate(camGo);
            }
        }
    }
}
#endif