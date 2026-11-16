using System.Collections.Generic;
using UnityEngine;

[DefaultExecutionOrder(-25)]
public class PlayerFirstPersonArmsComponent : MonoBehaviour
{
    [SerializeField] private Transform rigRoot; //기존 캐릭터와 같은 구조의 팔 본
    [SerializeField] private Transform handPoint; //도구를 잡을 손 위치
    [SerializeField] private SkinnedMeshRenderer armsRenderer; //팔과 손만 포함한 메시

    private readonly List<Transform> sourceBones = new List<Transform>(); //기존 애니메이션이 움직이는 본
    private readonly List<Transform> viewBones = new List<Transform>(); //본인 시점에 복사할 본
    private readonly List<(Transform bone, Vector3 position, Quaternion rotation)> groundedTorso = new(); //점프 중 유지할 몸통 자세
    private Animator animator; //들기 레이어와 점프 재생 상태 확인
    private int holdLayer; //기존 한손·양손 들기 레이어
    private bool holdingItem; //미션과 제스처를 제외한 도구 표시
    private SkinnedMeshRenderer sourceRenderer; //본인 캐릭터의 색상 복사 원본
    private PlayerHealth health; //사망과 탈출 시 표시 해제
    private MaterialPropertyBlock properties; //선택한 캐릭터 색상 복사
    private int materialCount; //팔 재질 슬롯 수
    private static readonly int groundedHash = Animator.StringToHash("IsGrounded"); //캐릭터의 접지 여부
    private static readonly int jumpHash = Animator.StringToHash("ANM_Player_Jump"); //몸통을 흔드는 점프 동작

    internal bool initialize(Animator sourceAnimator) //기존 애니메이션 본과 팔 전용 본 연결
    {
        if (sourceAnimator == null || rigRoot == null || handPoint == null || armsRenderer == null)
            return false;

        sourceBones.Clear();
        viewBones.Clear();
        groundedTorso.Clear();
        animator = sourceAnimator;
        holdLayer = animator.GetLayerIndex("Item Hold");
        Transform spine = rigRoot.Find("Armature_Player/Hips/Spine/Spine1/Spine2"); //팔이 붙어 있는 몸통
        foreach (Transform bone in rigRoot.GetComponentsInChildren<Transform>(true))
        {
            if (bone == rigRoot) continue;
            string path = getBonePath(bone); //모델 루트 기준 동일한 본 경로
            Transform source = sourceAnimator.transform.Find(path); //원래 캐릭터의 대응 본
            if (source == null) continue;
            sourceBones.Add(source);
            viewBones.Add(bone);
            if (spine != null && (bone == spine || spine.IsChildOf(bone)))
                groundedTorso.Add((bone, bone.localPosition, bone.localRotation));
        }

        sourceRenderer = sourceAnimator.GetComponentInChildren<SkinnedMeshRenderer>(true);
        health = sourceAnimator.GetComponentInParent<PlayerHealth>();
        properties = new MaterialPropertyBlock();
        materialCount = armsRenderer.sharedMaterials.Length;
        copyPose();
        return sourceBones.Count > 0;
    }

    private string getBonePath(Transform bone) //팔 프리팹 안의 상대 본 경로
    {
        string path = bone.name; //현재 본부터 부모 방향으로 구성하는 경로
        while (bone.parent != rigRoot)
        {
            bone = bone.parent;
            path = bone.name + "/" + path;
        }
        return path;
    }

    internal void showItem(GameObject item) //팔 표시와 기존 도구 손 추적 연결
    {
        show();
        ItemWorldView view = item.GetComponent<ItemWorldView>(); //1인칭 도구의 장착 보정
        if (view != null) view.setHeld(handPoint, true);
    }

    internal void setHoldingItem(bool holding) //제스처와 미션 중에는 기존 팔 모션 사용
    {
        holdingItem = holding;
    }

    internal void show() //빈손 제스처와 장착에 공통인 팔 표시
    {
        gameObject.SetActive(true);
        copyPose();
    }

    internal void hide() //빈손과 사망 시 팔 표시 해제
    {
        gameObject.SetActive(false);
    }

    private void LateUpdate() //Animator 평가 후 같은 Hold와 공격 자세 복사
    {
        if (health != null && health.Object != null && health.Object.IsValid && !health.canActWithoutCctv)
        {
            hide();
            return;
        }
        copyPose();
    }

    private void copyPose() //본 회전과 위치 및 캐릭터 선택 색상 반영
    {
        for (int index = 0; index < sourceBones.Count; index++)
        {
            if (sourceBones[index] == null || viewBones[index] == null) continue;
            viewBones[index].SetLocalPositionAndRotation(sourceBones[index].localPosition, sourceBones[index].localRotation);
        }
        stabilizeHeldTorso();
        if (sourceRenderer == null || properties == null) return;
        for (int index = 0; index < materialCount; index++)
        {
            sourceRenderer.GetPropertyBlock(properties, index);
            armsRenderer.SetPropertyBlock(properties, index);
        }
    }

    private void stabilizeHeldTorso() //뛰는 동작에서 몸통이 1인칭 소매를 카메라 앞으로 밀어 올리지 않도록 보정
    {
        if (!holdingItem || animator == null || holdLayer < 0 || animator.GetLayerWeight(holdLayer) < 0.5f) return;
        bool jumping = !animator.GetBool(groundedHash) || animator.GetCurrentAnimatorStateInfo(0).shortNameHash == jumpHash ||
            (animator.IsInTransition(0) && animator.GetNextAnimatorStateInfo(0).shortNameHash == jumpHash);
        for (int i = 0; i < groundedTorso.Count; i++)
        {
            var pose = groundedTorso[i];
            if (jumping) pose.bone.SetLocalPositionAndRotation(pose.position, pose.rotation);
            else groundedTorso[i] = (pose.bone, pose.bone.localPosition, pose.bone.localRotation);
        }
    }
}
