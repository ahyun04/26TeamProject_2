using Fusion;
using Fusion.Addons.SimpleKCC;
using UnityEngine;
using UnityEngine.Rendering;

[DefaultExecutionOrder(-50)]
[RequireComponent(typeof(SimpleKCC), typeof(PlayerHealth))]
public class PlayerLookVisualComponent : NetworkBehaviour, IAfterTick
{
    [Header("캐릭터 모델")]
    [SerializeField] private Transform bodyPivot; //카메라와 분리해 회전할 캐릭터 모델
    [SerializeField] private Transform headBone; //시선을 반영할 머리 본

    [Header("시선 회전")]
    [SerializeField, Range(0f, 179f)] private float headYawLimit = 60f; //머리만 회전할 수 있는 좌우 각도
    [SerializeField, Min(1f)] private float bodyTurnSpeed = 180f; //몸이 시선을 따라가는 초당 회전 각도

    [Networked] private float bodyYaw { get; set; } //호스트가 결정한 모델의 수평 방향
    [Networked] private NetworkBool bodyTurning { get; set; } //제한 각도를 넘은 뒤 시선까지 회전하는 상태

    private SimpleKCC kcc; //기존 네트워크 시선과 이동 상태
    private PlayerHealth health; //사망과 탈출 등 표시 종료 상태
    private PlayerCameraController cameraController; //본인에게 활성화된 1인칭 카메라
    private SkinnedMeshRenderer characterRenderer; //1인칭 팔과 분리된 원래 캐릭터 메시
    private ShadowCastingMode originalShadows; //본인 카메라 종료 시 복원할 그림자 설정
    private bool bodyHidden; //본인 화면에서 원래 몸을 숨겼는지 여부
    private NetworkBehaviour.PropertyReader<float> bodyYawReader; //다른 참가자에게 복제된 몸 방향 보간
    private Quaternion bodyLocalRotation; //프리팹의 기본 모델 회전
    private Vector3 bodyLocalPosition; //시선을 돌려도 모델이 카메라 주위를 돌지 않도록 유지할 위치
    private Quaternion headAnimationRotation; //시선을 덧붙이기 전 애니메이션의 머리 회전
    private Vector3 headAnimationScale; //본인 시점에서 숨기기 전 머리 본의 크기
    private bool headHidden; //현재 프레임에서 본인 머리만 숨겼는지 여부
    private bool headApplied; //이전 프레임의 시선 회전 적용 여부
    private bool spawned; //네트워크 상태 접근 가능 여부

    private void Awake() //기존 구성 요소와 모델 기본 회전 준비
    {
        kcc = GetComponent<SimpleKCC>();
        health = GetComponent<PlayerHealth>();
        cameraController = GetComponent<PlayerCameraController>();
        if (bodyPivot != null)
        {
            bodyLocalRotation = bodyPivot.localRotation;
            bodyLocalPosition = bodyPivot.localPosition;
            characterRenderer = bodyPivot.GetComponentInChildren<SkinnedMeshRenderer>(true);
        }
    }

    public override void Spawned() //호스트의 초기 몸 방향과 네트워크 보간 연결
    {
        bodyYawReader = GetPropertyReader<float>(nameof(bodyYaw));
        if (HasStateAuthority)
        {
            bodyYaw = kcc.GetLookRotation().y;
            bodyTurning = false;
        }
        spawned = true;
        refreshLocalBody();
    }

    public void AfterTick() //이동과 시선 입력 처리가 끝난 뒤 호스트만 몸 방향 결정
    {
        if (!spawned || !HasStateAuthority) return;
        if (!health.CanAct)
        {
            bodyTurning = false;
            return;
        }

        float viewYaw = kcc.GetLookRotation().y; //이번 틱에 처리된 카메라의 수평 시선
        if (headYawLimit <= 0f)
        {
            bodyYaw = viewYaw;
            bodyTurning = false;
            return;
        }
        float difference = Mathf.DeltaAngle(bodyYaw, viewYaw); //180도 경계에서도 가장 짧은 회전 차이
        if (Mathf.Abs(difference) > Mathf.Clamp(headYawLimit, 0f, 179f)) bodyTurning = true;
        if (!bodyTurning) return;

        bodyYaw = Mathf.MoveTowardsAngle(bodyYaw, viewYaw, Mathf.Max(1f, bodyTurnSpeed) * Runner.DeltaTime);
        if (Mathf.Abs(Mathf.DeltaAngle(bodyYaw, viewYaw)) <= 0.01f)
        {
            bodyYaw = viewYaw;
            bodyTurning = false;
        }
    }

    private float getRenderedBodyYaw() //몸 방향을 네트워크 스냅샷 사이에서 각도로 보간
    {
        if (headYawLimit <= 0f && HasInputAuthority) return kcc.GetLookRotation().y;
        if (TryGetSnapshotsBuffers(out var from, out var to, out float alpha))
            return Mathf.LerpAngle(bodyYawReader.Read(from), bodyYawReader.Read(to), alpha);
        return bodyYaw;
    }

    internal Vector2 getBodyLocalVelocity() //표시되는 몸 방향을 기준으로 이동 애니메이션 방향 계산
    {
        if (!spawned) return Vector2.zero;
        Vector3 velocity = kcc.RealVelocity; //기존 KCC의 실제 이동 속도
        velocity.y = 0f;
        if (velocity.sqrMagnitude < 0.0001f) return Vector2.zero;
        velocity = Quaternion.Inverse(Quaternion.Euler(0f, getRenderedBodyYaw(), 0f)) * velocity;
        return new Vector2(velocity.x, velocity.z);
    }

    private void restoreHead() //시선 회전이 누적되지 않도록 애니메이션 기준 자세 복원
    {
        restoreLocalHead();
        if (!headApplied || headBone == null) return;
        headBone.localRotation = headAnimationRotation;
        headApplied = false;
    }

    internal void restoreLocalHead() //사망·관전·카메라 종료 시 머리와 귀의 원래 크기 복원
    {
        if (headHidden && headBone != null) headBone.localScale = headAnimationScale;
        headHidden = false;
    }

    internal void refreshLocalBody() //본인 1인칭에서는 몸의 그림자만 유지하고 다른 화면에서는 원래 표시
    {
        bool hidden = spawned && isActiveAndEnabled && cameraController != null && cameraController.isLocalCameraActive;
        setBodyHidden(hidden);
    }

    private void setBodyHidden(bool hidden) //팔 장착과 무관하게 원래 몸의 로컬 표시만 변경
    {
        if (characterRenderer == null || bodyHidden == hidden) return;
        if (hidden)
        {
            originalShadows = characterRenderer.shadowCastingMode;
            characterRenderer.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
        }
        else characterRenderer.shadowCastingMode = originalShadows;
        bodyHidden = hidden;
    }

    internal void restoreLocalView() //관전·카메라 종료 시 머리와 몸의 원래 표시 복원
    {
        restoreLocalHead();
        setBodyHidden(false);
    }

    private void Update() //Animator가 평가되기 전에 이전 프레임의 시선 보정 제거
    {
        restoreHead();
    }

    private void LateUpdate() //Animator 이후 머리와 몸을 표시하고 아이템의 손 추적보다 먼저 실행
    {
        if (!spawned || Object == null || !Object.IsValid || bodyPivot == null || headBone == null) return;
        refreshLocalBody();
        float yaw = getRenderedBodyYaw(); //이번 화면에 표시할 몸 방향
        Quaternion bodyRotation = Quaternion.Euler(0f, yaw, 0f); //카메라 루트 회전과 분리된 모델의 수평 회전
        bodyPivot.SetPositionAndRotation(transform.position + bodyRotation * Vector3.Scale(bodyLocalPosition, transform.lossyScale), bodyRotation * bodyLocalRotation);
        if (!health.CanAct) return;

        Vector2 view = kcc.GetLookRotation(); //피격 흔들림을 제외한 기존 네트워크 시선
        float headYaw = Mathf.Clamp(Mathf.DeltaAngle(yaw, view.y), -headYawLimit, headYawLimit); //목의 좌우 제한
        headAnimationRotation = headBone.localRotation;
        Quaternion horizontal = Quaternion.AngleAxis(headYaw, Vector3.up); //몸 방향과 시선 사이의 수평 차이
        Quaternion vertical = Quaternion.AngleAxis(view.x, bodyPivot.right); //위아래 시선을 몸의 오른쪽 축에 반영
        headBone.rotation = horizontal * vertical * headBone.rotation;
        headApplied = true;
        if (cameraController != null && cameraController.isLocalCameraActive)
        {
            if (!headHidden) headAnimationScale = headBone.localScale;
            headBone.localScale = Vector3.zero;
            headHidden = true;
        }
    }

    public override void Despawned(NetworkRunner runner, bool hasState) //씬 전환과 관전자 전환 시 표시 상태 정리
    {
        restoreHead();
        setBodyHidden(false);
        spawned = false;
    }

    private void OnDisable() //비활성화 시 머리 보정과 본인 몸 숨김 해제
    {
        restoreHead();
        setBodyHidden(false);
    }
}
