using Fusion;
using UnityEngine;

public class WiringMission : MissionMiniGameBase
{
    private const int WireCount = 4;

    [Header("전선")]
    [SerializeField] private WireStartPoint[] startPoints;
    [SerializeField] private WireEndPoint[] endPoints;

    [Header("완료 레버")]
    [SerializeField] private WiringLever lever;

    [Networked, Capacity(WireCount)]
    private NetworkArray<int> StartColors => default;

    [Networked, Capacity(WireCount)]
    private NetworkArray<int> ConnectedEnds => default;

    [Networked]
    private NetworkBool SetupCompleted { get; set; }

    private readonly int[] previousColors = { -1, -1, -1, -1 };
    private readonly int[] previousConnections = { -2, -2, -2, -2 };

    public bool IsReady
    {
        get
        {
            if (!SetupCompleted)
                return false;

            for (int i = 0; i < WireCount; i++)
            {
                if (ConnectedEnds[i] < 0)
                    return false;
            }

            return true;
        }
    }

    public override void Spawned()
    {
        if (!HasValidSetup())
            return;

        if (HasStateAuthority && !SetupCompleted)
            InitializeWires();
    }

    public override void Render()
    {
        base.Render();

        if (!SetupCompleted || !HasValidSetup())
            return;

        RefreshWireVisuals();

        if (IsCompleted && lever != null)
            lever.SetCompleted();
    }

    public bool IsConnected(int startIndex)
    {
        if (!IsValidIndex(startIndex))
            return true;

        return ConnectedEnds[startIndex] >= 0;
    }

    public void RequestConnect(int startIndex, int endIndex)
    {
        if (IsCompleted || !SetupCompleted)
            return;

        if (HasStateAuthority)
        {
            TryConnect(startIndex, endIndex);
            return;
        }

        RPC_RequestConnect(startIndex, endIndex);
    }

    public void RequestLever()
    {
        if (IsCompleted || !IsReady)
            return;

        if (HasStateAuthority)
        {
            TryUseLever(Runner.LocalPlayer);
            return;
        }

        RPC_RequestLever();
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_RequestConnect(int startIndex, int endIndex)
    {
        TryConnect(startIndex, endIndex);
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_RequestLever(RpcInfo info = default)
    {
        TryUseLever(info.Source);
    }

    private void InitializeWires()
    {
        int[] colors =
        {
            (int)WireColor.Red,
            (int)WireColor.Blue,
            (int)WireColor.Green,
            (int)WireColor.Yellow
        };

        for (int i = colors.Length - 1; i > 0; i--)
        {
            int randomIndex = Random.Range(0, i + 1);

            int temp = colors[i];
            colors[i] = colors[randomIndex];
            colors[randomIndex] = temp;
        }

        for (int i = 0; i < WireCount; i++)
        {
            StartColors.Set(i, colors[i]);
            ConnectedEnds.Set(i, -1);
        }

        SetupCompleted = true;
    }

    private void TryConnect(int startIndex, int endIndex)
    {
        if (!HasStateAuthority || IsCompleted || !SetupCompleted)
            return;

        if (!IsValidIndex(startIndex) || !IsValidIndex(endIndex))
            return;

        if (ConnectedEnds[startIndex] >= 0)
            return;

        if (IsEndConnected(endIndex))
            return;

        WireColor startColor = (WireColor)StartColors[startIndex];
        WireColor endColor = endPoints[endIndex].Color;

        if (startColor != endColor)
            return;

        ConnectedEnds.Set(startIndex, endIndex);

        Debug.Log($"[WiringMission] 연결 성공 : Start {startIndex} → End {endIndex}");
    }

    private void TryUseLever(PlayerRef player)
    {
        if (!HasStateAuthority || IsCompleted || !IsReady)
            return;

        RequestComplete(player);
    }

    private bool IsEndConnected(int endIndex)
    {
        for (int i = 0; i < WireCount; i++)
        {
            if (ConnectedEnds[i] == endIndex)
                return true;
        }

        return false;
    }

    private void RefreshWireVisuals()
    {
        for (int i = 0; i < WireCount; i++)
        {
            int colorValue = StartColors[i];

            if (previousColors[i] != colorValue)
            {
                previousColors[i] = colorValue;
                startPoints[i].SetColor((WireColor)colorValue);
            }

            int endIndex = ConnectedEnds[i];

            if (previousConnections[i] == endIndex)
                continue;

            previousConnections[i] = endIndex;

            if (endIndex >= 0)
                startPoints[i].ShowConnection(endPoints[endIndex]);
            else
                startPoints[i].ClearConnection();
        }
    }

    private bool IsValidIndex(int index)
    {
        return index >= 0 && index < WireCount;
    }

    private bool HasValidSetup()
    {
        if (startPoints == null || startPoints.Length != WireCount)
            return false;

        if (endPoints == null || endPoints.Length != WireCount)
            return false;

        return true;
    }

    protected override void FinishMission()
    {
        base.FinishMission();

        if (lever != null)
            lever.SetCompleted();
    }
}