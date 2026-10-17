using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

[DefaultExecutionOrder(-25)]
public class PlayerFirstPersonArmsComponent : MonoBehaviour
{
    [SerializeField] private Transform rigRoot; //기존 캐릭터와 같은 구조의 팔 본
    [SerializeField] private Transform handPoint; //도구를 잡을 손 위치
    [SerializeField] private SkinnedMeshRenderer armsRenderer; //팔과 손만 포함한 메시

    private readonly List<Transform> sourceBones = new List<Transform>(); //기존 애니메이션이 움직이는 본
    private readonly List<Transform> viewBones = new List<Transform>(); //본인 시점에 복사할 본
    private SkinnedMeshRenderer sourceRenderer; //본인 캐릭터의 색상과 그림자 표시
    private PlayerHealth health; //사망과 탈출 시 표시 해제
    private MaterialPropertyBlock properties; //선택한 캐릭터 색상 복사
    private ShadowCastingMode originalShadows; //장착 전 본인 모델의 그림자 설정
    private bool hidingBody; //본인 모델의 중복 표시를 숨겼는지 여부
    private int materialCount; //팔 재질 슬롯 수

    internal bool initialize(Animator sourceAnimator) //기존 애니메이션 본과 팔 전용 본 연결
    {
        if (sourceAnimator == null || rigRoot == null || handPoint == null || armsRenderer == null)
            return false;

        sourceBones.Clear();
        viewBones.Clear();
        foreach (Transform bone in rigRoot.GetComponentsInChildren<Transform>(true))
        {
            if (bone == rigRoot) continue;
            string path = getBonePath(bone); //모델 루트 기준 동일한 본 경로
            Transform source = sourceAnimator.transform.Find(path); //원래 캐릭터의 대응 본
            if (source == null) continue;
            sourceBones.Add(source);
            viewBones.Add(bone);
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
        gameObject.SetActive(true);
        copyPose();
        if (sourceRenderer != null && !hidingBody)
        {
            originalShadows = sourceRenderer.shadowCastingMode;
            sourceRenderer.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
            hidingBody = true;
        }
        ItemWorldView view = item.GetComponent<ItemWorldView>(); //1인칭 도구의 장착 보정
        if (view != null) view.setHeld(handPoint, true);
    }

    internal void hide() //빈손과 사망 시 팔 표시 해제
    {
        gameObject.SetActive(false);
        restoreBody();
    }

    private void LateUpdate() //Animator 평가 후 같은 Hold와 공격 자세 복사
    {
        if (health != null && health.Object != null && health.Object.IsValid && !health.CanAct)
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
        if (sourceRenderer == null || properties == null) return;
        for (int index = 0; index < materialCount; index++)
        {
            sourceRenderer.GetPropertyBlock(properties, index);
            armsRenderer.SetPropertyBlock(properties, index);
        }
    }

    private void restoreBody() //본인 모델의 원래 표시와 그림자 설정 복원
    {
        if (hidingBody && sourceRenderer != null)
            sourceRenderer.shadowCastingMode = originalShadows;
        hidingBody = false;
    }

    private void OnDisable() //카메라 비활성화와 관전 전환 처리
    {
        restoreBody();
    }

    private void OnDestroy() //씬 종료 시 남은 표시 상태 정리
    {
        restoreBody();
    }
}
