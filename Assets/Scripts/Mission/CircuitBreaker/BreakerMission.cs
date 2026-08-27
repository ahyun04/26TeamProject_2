using Fusion;
using UnityEngine;

/// <summary>
/// [ 차단기 미션 전체 관리 ]
///
/// 레버 6개의 On/Off 상태를 네트워크로 관리하고,
/// 모든 레버가 On 상태가 되면 MissionMiniGameBase에 완료 요청
/// </summary>
public class BreakerMission : MissionMiniGameBase
{
    private const int LeverCount = 6;

    [SerializeField] private BreakerLever[] levers;

    [Networked, Capacity(LeverCount)]
    private NetworkArray<NetworkBool> LeverStates => default;

    private readonly bool[] previousStates = new bool[LeverCount];
    private bool isReady;


    public override void Spawned()
    {
        isReady = ValidateLevers();

        if (isReady)
            ApplyLeverStates(true);
    }


    public override void StartMission()
    {
        if (isReady)
            ApplyLeverStates(true);
    }


    /// <summary>
    /// 플레이어가 특정 레버를 눌렀을 때 호출
    /// Host라면 직접 상태를 변경하고,
    /// Guest라면 RPC로 Host에게 변경을 요청
    /// </summary>
    public void RequestToggle(int index)
    {
        if (!IsValidIndex(index) || IsCompleted)
            return;

        if (Object.HasStateAuthority)
            ToggleLever(index);
        else
            RPC_RequestToggle(index);
    }


    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_RequestToggle(int index)
    {
        ToggleLever(index);
    }


    /// <summary>
    /// Host가 실제 Networked 레버 상태를 변경
    /// </summary>
    private void ToggleLever(int index)
    {
        if (!Object.HasStateAuthority || !IsValidIndex(index) || IsCompleted)
            return;

        // 현재 상태의 반대 값으로 변경
        LeverStates.Set(index, !LeverStates.Get(index));

        // 변경 후 6개가 모두 On인지 확인
        CheckCompleted();
    }


    /// <summary>
    /// 6개의 레버가 모두 On인지 검사
    /// 하나라도 false이면 아직 완료하지 않고,
    /// 모두 true이면 부모 클래스에 완료 요청
    /// </summary>
    private void CheckCompleted()
    {
        for (int i = 0; i < LeverCount; i++)
        {
            if (!LeverStates.Get(i))
                return;
        }

        // MissionMiniGameBase가 실제 미션 완료를 처리
        RequestComplete();
    }


    public override void Render()
    {
        base.Render();

        if (isReady)
            ApplyLeverStates(false);
    }


    /// <summary>
    /// Networked 상태를 실제 BreakerLever에 적용
    /// 
    /// force = true → 모든 레버를 강제로 적용
    /// force = false → 상태가 변경된 레버만 적용
    /// </summary>
    private void ApplyLeverStates(bool force)
    {
        for (int i = 0; i < LeverCount; i++)
        {
            bool isOn = LeverStates.Get(i);

            if (!force && previousStates[i] == isOn)
                continue;

            // 현재 상태를 이전 상태로 저장
            previousStates[i] = isOn;

            // 실제 레버에게 회전 + Shader 변경 요청
            levers[i].SetState(isOn);
        }
    }


    private bool IsValidIndex(int index)
    {
        return index >= 0 && index < LeverCount;
    }


    /// <summary>
    /// Inspector에 레버 6개가 정상적으로 등록되어 있는지 확인
    /// Spawned에서 한 번 확인하고 결과를 isReady에 저장
    /// </summary>
    private bool ValidateLevers()
    {
        if (levers == null || levers.Length != LeverCount)
            return false;

        for (int i = 0; i < LeverCount; i++)
        {
            if (levers[i] == null)
                return false;
        }

        return true;
    }
}