using Fusion;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// [ 안테나 방향 조정 미션 ]
///
/// F키를 누르고 있는 동안 안테나를 회전시키고,
/// 랜덤 목표 회전량에 도달하면 준비완료 상태가 된다
///
/// 준비완료 후 고정 버튼을 누르면
/// MissionMiniGameBase에 최종 미션 완료를 요청한다
/// </summary>
public class AntennaMission : MissionMiniGameBase, ITargetable, IHoldInteractable
{
    private const int MinTargetAngle = 360;
    private const int MaxTargetAngle = 720;


    [Header("안테나")]
    [SerializeField] private Transform antennaTransform;
    [SerializeField] private Vector3 rotationAxis = Vector3.up;
    [SerializeField] private float rotationSpeed = 90f;


    [Header("스크린 UI")]
    [SerializeField] private Image gaugeFill;
    [SerializeField] private TMP_Text percentText;


    // 이번 게임에서 필요한 총 회전량
    // 360 ~ 720 사이에서 Host가 랜덤 결정
    [Networked]
    private int TargetAngle { get; set; }


    // 현재까지 누적해서 회전한 각도
    [Networked]
    private float CurrentAngle { get; set; }


    // 현재 안테나를 돌리고 있는 플레이어
    [Networked]
    private PlayerRef RotatingPlayer { get; set; }


    // PlayerTargetDetector가 현재 NetworkObject를 알 수 있도록 제공
    public NetworkObject TargetObject => Object;


    // 목표 각도까지 도달했는지 확인
    public bool IsReady => TargetAngle > 0 && CurrentAngle >= TargetAngle;


    // 현재 진행률 0 ~ 1
    private float Progress => TargetAngle <= 0 ? 0f : Mathf.Clamp01(CurrentAngle / TargetAngle);


    private Quaternion startRotation;


    private void Awake()
    {
        if (antennaTransform != null)
            startRotation = antennaTransform.localRotation;
    }


    public override void Spawned()
    {
        // 랜덤 목표 각도는 StateAuthority인 Host만 결정
        if (Object.HasStateAuthority && TargetAngle <= 0)
            TargetAngle = Random.Range(MinTargetAngle, MaxTargetAngle + 1);

        ApplyVisual();
    }


    public override void FixedUpdateNetwork()
    {
        // 실제 회전 진행도는 Host만 변경
        if (!Object.HasStateAuthority)
            return;

        if (IsCompleted || IsReady)
            return;

        if (RotatingPlayer == PlayerRef.None)
            return;

        // 현재 누적 회전량 증가
        CurrentAngle += rotationSpeed * Runner.DeltaTime;

        // 목표보다 넘어가지 않도록 제한
        if (CurrentAngle < TargetAngle)
            return;

        CurrentAngle = TargetAngle;

        // 목표에 도달하면 자동으로 회전 종료
        RotatingPlayer = PlayerRef.None;
    }


    public override void Render()
    {
        // MissionMiniGameBase의 미션 권한 Collider 처리
        base.Render();

        ApplyVisual();
    }


    /// <summary>
    /// 플레이어가 F키를 눌렀을 때 회전 시작 요청
    /// </summary>
    public void BeginHold()
    {
        if (IsCompleted || IsReady)
            return;

        if (Object.HasStateAuthority)
        {
            BeginRotate(Runner.LocalPlayer);

            return;
        }

        RPC_BeginRotate();
    }


    /// <summary>
    /// 플레이어가 F키를 떼거나 안테나에서 시선을 돌렸을 때 회전 종료
    /// </summary>
    public void EndHold()
    {
        if (Object.HasStateAuthority)
        {
            EndRotate(Runner.LocalPlayer);

            return;
        }

        RPC_EndRotate();
    }


    /// <summary>
    /// 고정 버튼에서 호출
    /// 준비완료 상태일 때만 최종 완료 요청
    /// </summary>
    public void RequestLock()
    {
        if (IsCompleted || !IsReady)
            return;

        if (Object.HasStateAuthority)
        {
            LockAntenna(Runner.LocalPlayer);

            return;
        }

        RPC_RequestLock();
    }


    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_BeginRotate(RpcInfo info = default)
    {
        BeginRotate(info.Source);
    }


    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_EndRotate(RpcInfo info = default)
    {
        EndRotate(info.Source);
    }


    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_RequestLock(RpcInfo info = default)
    {
        LockAntenna(info.Source);
    }


    /// <summary>
    /// Host가 실제 안테나 회전 시작을 결정
    /// </summary>
    private void BeginRotate(PlayerRef player)
    {
        if (!Object.HasStateAuthority || IsCompleted || IsReady)
            return;

        // 다른 플레이어가 이미 돌리고 있다면 접근 불가
        if (RotatingPlayer != PlayerRef.None && RotatingPlayer != player)
            return;

        RotatingPlayer = player;

        SetActivePlayer(player);
    }


    /// <summary>
    /// Host가 실제 안테나 회전을 종료
    /// </summary>
    private void EndRotate(PlayerRef player)
    {
        if (!Object.HasStateAuthority)
            return;

        // 실제 돌리고 있던 플레이어만 종료 가능
        if (RotatingPlayer != player)
            return;

        RotatingPlayer = PlayerRef.None;
    }


    /// <summary>
    /// Host가 준비완료 상태를 확인하고
    /// MissionMiniGameBase에 최종 완료 요청
    /// </summary>
    private void LockAntenna(PlayerRef player)
    {
        if (!Object.HasStateAuthority || IsCompleted || !IsReady)
            return;

        RotatingPlayer = PlayerRef.None;

        RequestComplete(player);
    }


    /// <summary>
    /// Networked 회전 진행도를 실제 안테나와 UI에 적용
    /// </summary>
    private void ApplyVisual()
    {
        ApplyAntennaRotation();

        ApplyUI();
    }


    /// <summary>
    /// 누적 회전량을 실제 안테나 Transform에 적용
    /// </summary>
    private void ApplyAntennaRotation()
    {
        if (antennaTransform == null)
            return;

        Vector3 axis = rotationAxis.sqrMagnitude > 0f ?
            rotationAxis.normalized :
            Vector3.up;

        Quaternion rotation = Quaternion.AngleAxis(CurrentAngle, axis);

        antennaTransform.localRotation = startRotation * rotation;
    }


    /// <summary>
    /// 현재 진행도를 게이지와 퍼센트 텍스트에 표시
    /// </summary>
    private void ApplyUI()
    {
        if (gaugeFill != null)
            gaugeFill.fillAmount = Progress;

        if (percentText != null)
            percentText.text = $"{Mathf.RoundToInt(Progress * 100f)}%";
    }


    /// <summary>
    /// 최종 미션 완료 후 회전 정지
    /// </summary>
    protected override void FinishMission()
    {
        if (Object.HasStateAuthority)
            RotatingPlayer = PlayerRef.None;

        ApplyVisual();
    }
}