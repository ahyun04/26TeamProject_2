using Fusion;
using Fusion.Addons.SimpleKCC;
using LockdownProtocol.Networking;
using UnityEngine;

[RequireComponent(typeof(SimpleKCC))]
public class SpectatorVisualComponent : NetworkBehaviour
{
    [SerializeField] private Renderer[] spectatorRenderers; //관전자끼리 보이는 구체와 방향 표시
    [SerializeField] private Transform directionPivot; //상하 시선을 표시할 방향 축
    [SerializeField] private Renderer[] directionRenderers; //전방 표시의 색상을 적용할 렌더러
    [SerializeField] private Color directionColor = Color.white; //방향 표시 색상

    private SimpleKCC kcc; //복제된 시선 방향
    private bool localViewReady; //기존 플레이어 화면을 정리했는지 여부

    public override void Spawned() //표시 준비와 로컬 시점 전환
    {
        kcc = GetComponent<SimpleKCC>();
        MaterialPropertyBlock properties = new MaterialPropertyBlock(); //공유 머티리얼을 보존할 표시 색상
        properties.SetColor("_BaseColor", directionColor);
        foreach (Renderer directionRenderer in directionRenderers)
            if (directionRenderer != null) directionRenderer.SetPropertyBlock(properties);
        prepareLocalView();
    }

    public override void Render() //관전자 화면에서만 다른 관전자와 시선을 표시
    {
        if (!localViewReady) prepareLocalView();
        NetworkObject localPlayer = Runner.GetPlayerObject(Runner.LocalPlayer); //이 화면의 원래 플레이어
        SpectatorManager localSpectator = localPlayer != null ? localPlayer.GetComponent<SpectatorManager>() : null; //이 화면의 관전 상태
        bool visible = !HasInputAuthority && localSpectator != null && localPlayer.IsValid && localSpectator.IsSpectator; //생존자와 본인 화면에서는 숨김
        foreach (Renderer spectatorRenderer in spectatorRenderers)
            if (spectatorRenderer != null) spectatorRenderer.enabled = visible;
        if (directionPivot != null) directionPivot.localRotation = Quaternion.Euler(kcc.GetLookRotation(true, false));
    }

    private void prepareLocalView() //시체의 카메라·능력치 UI·중복 음소거 입력 해제
    {
        if (!HasInputAuthority)
        {
            localViewReady = true;
            return;
        }
        NetworkObject player = Runner.GetPlayerObject(Object.InputAuthority); //승패 판정에 남겨둔 사망 플레이어
        if (player == null || !player.IsValid) return;
        player.GetComponent<PlayerCameraController>()?.setCameraActive(false);
        player.GetComponent<PlayerFeedback>()?.setVisible(false);
        VoiceMuteController muteController = player.GetComponent<VoiceMuteController>(); //관전자 쪽에서만 V 입력 처리
        if (muteController != null) muteController.enabled = false;
        localViewReady = true;
    }
}
