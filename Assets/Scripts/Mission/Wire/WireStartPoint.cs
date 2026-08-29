using Fusion;
using UnityEngine;


/// <summary>
/// 플레이어가 드래그를 시작하는 오른쪽 전선 시작점(WireStart)을 담당
/// 자신의 랜덤 색상을 화면에 표시하고, 드래그 입력을 받아 WiringMission에 연결을 요청한다
/// 연결 성공 여부를 직접 결정하지 않고 최종 검증은 StateAuthority의 WiringMission이 담당한다
/// </summary>
public class WireStartPoint : MonoBehaviour, ITargetable, IDragInteractable
{
    [SerializeField] private WiringMission mission;
    [SerializeField] private int index;

    [Header("위치")]
    [SerializeField] private Transform anchor;

    [Header("색상")]
    [SerializeField] private Renderer colorRenderer;
    [SerializeField] private int materialIndex;
    [SerializeField] private string colorProperty = "_BaseColor";

    [Header("전선")]
    [SerializeField] private WireConnectionVisual connectionVisual;

    private MaterialPropertyBlock propertyBlock;
    private int colorPropertyId;

    public NetworkObject TargetObject => mission != null ? mission.Object : null;
    public int Index => index;
    public Transform Anchor => anchor != null ? anchor : transform;
    public WireColor Color { get; private set; }

    private void Awake()
    {
        if (mission == null)
            mission = GetComponentInParent<WiringMission>();

        propertyBlock = new MaterialPropertyBlock();
        colorPropertyId = Shader.PropertyToID(colorProperty);
    }

    public void SetColor(WireColor color)
    {
        Color = color;

        if (colorRenderer == null)
            return;

        colorRenderer.GetPropertyBlock(propertyBlock, materialIndex);

        propertyBlock.SetColor(
            colorPropertyId,
            WireColorUtility.ToUnityColor(color)
        );

        colorRenderer.SetPropertyBlock(propertyBlock, materialIndex);
    }

    public void BeginDrag()
    {
        if (mission == null || connectionVisual == null)
            return;

        if (mission.IsCompleted || mission.IsConnected(index))
            return;

        connectionVisual.BeginPreview(Anchor, Color);
    }

    public void UpdateDrag(Ray aimRay)
    {
        if (connectionVisual != null)
            connectionVisual.UpdatePreview(aimRay);
    }

    public void EndDrag(ITargetable releaseTarget)
    {
        if (connectionVisual == null || !connectionVisual.IsPreviewing)
            return;

        if (releaseTarget is WireEndPoint endPoint &&
            endPoint.Mission == mission &&
            endPoint.Color == Color)
        {
            mission.RequestConnect(index, endPoint.Index);
        }

        connectionVisual.CancelPreview();
    }

    public void ShowConnection(WireEndPoint endPoint)
    {
        if (connectionVisual == null || endPoint == null)
            return;

        connectionVisual.ShowConnection(Anchor, endPoint.Anchor, Color);
    }

    public void ClearConnection()
    {
        if (connectionVisual != null)
            connectionVisual.Hide();
    }
}