using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(CanvasRenderer))]
public class DamageVignetteGraphic : MaskableGraphic
{
    [SerializeField, Range(0.05f, 0.5f)] private float edgeWidth = 0.2f; //화면 가장자리 그라데이션 폭

    protected override void OnPopulateMesh(VertexHelper vertices) //중앙은 투명하고 가장자리로 갈수록 빨간 UI 메시
    {
        vertices.Clear();
        Rect rect = GetPixelAdjustedRect();
        const int columns = 32;
        const int rows = 24;
        for (int y = 0; y <= rows; y++)
        {
            float vertical = y / (float)rows;
            for (int x = 0; x <= columns; x++)
            {
                float horizontal = x / (float)columns;
                float distanceFromEdge = Mathf.Min(horizontal, 1f - horizontal, vertical, 1f - vertical);
                float opacity = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(distanceFromEdge / edgeWidth));
                Color vertexColor = color;
                vertexColor.a *= opacity;
                vertices.AddVert(new Vector3(Mathf.Lerp(rect.xMin, rect.xMax, horizontal),
                    Mathf.Lerp(rect.yMin, rect.yMax, vertical)), vertexColor, new Vector2(horizontal, vertical));
            }
        }
        for (int y = 0; y < rows; y++)
        {
            for (int x = 0; x < columns; x++)
            {
                int bottomLeft = y * (columns + 1) + x;
                vertices.AddTriangle(bottomLeft, bottomLeft + columns + 1, bottomLeft + 1);
                vertices.AddTriangle(bottomLeft + 1, bottomLeft + columns + 1, bottomLeft + columns + 2);
            }
        }
    }
}
