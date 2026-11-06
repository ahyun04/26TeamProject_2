using UnityEngine;

/// <summary>
/// 로컬 플레이어의 1인칭 아이템 표시 
/// </summary>
public class PlayerFirstPersonItemView : MonoBehaviour
{
    [SerializeField] private Transform holdPoint;
    [SerializeField] private PlayerFirstPersonArmsComponent armsPrefab; //본인 시점의 팔과 손
    [SerializeField] private Animator playerAnimator; //기존 Hold와 공격 자세의 원본
    [SerializeField] private Vector3 armsPosition = new Vector3(-0.08f, -1.05f, 0.25f); //카메라 기준 팔 모델 위치
    [SerializeField] private Vector3 twoHandedArmsPosition = new Vector3(0f, -0.70f, 1.35f); //어깨를 숨기고 양손 도구를 화면 아래에 표시할 위치
    [SerializeField] private Vector3 twoHandedArmsRotation = new Vector3(-65f, 0f, 0f); //어깨 절단면을 카메라 뒤로 향하게 할 양손 팔 각도
    [SerializeField] private ItemData knifeData; //칼만 적용할 화면 위치 설정
    [SerializeField] private Vector3 knifeArmsPosition = new Vector3(0.12f, -1.1f, 0.25f); //칼과 팔을 함께 오른쪽 아래로 이동
    [SerializeField] private ItemData electricShockBatonData; //긴 전기충격기의 전용 표시 설정
    [SerializeField] private Vector3 electricShockBatonArmsPosition = new Vector3(-0.08f, -1.3f, 0.65f); //전극이 화면 안에 보이도록 팔과 도구를 함께 배치
    [SerializeField] private Vector3 gestureArmsPosition = new Vector3(0.02f, -0.68f, 1.2f); //엄지척을 보여줄 팔 위치
    [SerializeField] private Vector3 gestureArmsRotation = new Vector3(-60f, 0f, 0f); //팔이 엄지를 가리지 않도록 본인 표시만 기울이는 각도
    [SerializeField] private Vector3 missionBaseArmsPosition = new Vector3(-0.08f, -1.05f, 0.25f); //미션 버튼·밸브 동작을 보여줄 팔 위치 (14b — 도구 들기와 같은 오른쪽 아래)
    [SerializeField] private Vector3 missionBaseArmsRotation = Vector3.zero; //미션 동작 팔 각도 (14b — 기울이지 않아 보는 곳으로 손을 뻗음, 엄지척처럼 위로 세우면 하늘을 찌름)
    [SerializeField] private bool aimMissionHand = true; //미션 동작 중 손끝을 화면 가운데(조준점)에 맞춤 (14b — 3인칭 클립이라 고정 각도로는 보는 버튼과 안 맞음)
    [SerializeField] private float missionAimMaxAngle = 60f; //조준점 맞춤으로 팔을 돌리는 최대 각도 (14b)
    [SerializeField] private Vector3 missionShoulderPoint = new Vector3(0.2f, -0.3f, 0.25f); //미션 동작 중 오른쪽 어깨를 둘 카메라 기준 자리 (14b — 화면 아래 바로 밖, 손이 0.7m 앞까지 나와 팔이 보이게. 절단면은 화면 끝에 조금 보일 수 있음)
    [SerializeField] private float missionAimFallbackReach = 0.8f; //손이 조준선에 닿지 않을 때 향할 조준선 위 거리 (14b)

    private GameObject currentView;
    private PlayerFirstPersonArmsComponent arms; //로컬 플레이어에게만 생성하는 팔 표시
    private ItemData displayedData; //제스처 종료 시 복원할 도구 위치
    private bool showingGesture; //빈손에서도 제스처를 표시하는 상태
    private bool showingMission; //미션 버튼·밸브 동작을 표시하는 상태 (14b)
    private bool missionTurning; //밸브·안테나 돌리기 동작 (양손 가운데를 조준점에 맞춤)
    private Transform missionFingerTip; //버튼 동작에서 조준점에 맞출 오른손 검지 끝
    private Transform missionLeftHand; //돌리기 동작에서 맞출 왼손
    private Transform missionRightHand; //돌리기 동작에서 맞출 오른손
    private Transform missionShoulder; //팔을 돌리는 중심 (오른쪽 어깨)
    private bool missionBonesFound; //팔 본을 한 번만 찾음
    private bool gestureEntered; //Animator 제스처 진입 확인
    private float gestureStartTime; //제스처 진입 대기 제한
    private PlayerHealth health; //관전 전환 시 남은 제스처 표시 해제
    private static readonly int gestureHash = Animator.StringToHash("IsThumbsUp"); //원본 제스처 상태


    public void Show(ItemData data)
    {
        Clear();

        if (data == null)
            return;

        if (data.FirstPersonPrefab == null)
            return;

        if (holdPoint == null)
            return;

        if (armsPrefab != null && !prepareArms())
            return;

        currentView =
            Instantiate(data.FirstPersonPrefab, arms != null ? arms.transform : holdPoint);

        currentView.transform.localPosition = Vector3.zero;

        currentView.transform.localRotation = Quaternion.identity;

        currentView.transform.localScale = data.FirstPersonPrefab.transform.localScale;
        displayedData = data;
        if (arms != null) arms.showItem(currentView);
        refreshPresentation();
    }


    public void Clear()
    {
        if (currentView != null)
        {
            currentView.SetActive(false);
            Destroy(currentView);
        }
        currentView = null;
        displayedData = null;
        refreshPresentation();
    }

    internal void showThumbsUp() //승인된 원본 애니메이션을 본인 팔에도 표시
    {
        if (armsPrefab == null || holdPoint == null || !prepareArms()) return;
        showingGesture = true;
        gestureEntered = false;
        gestureStartTime = Time.time;
        refreshPresentation();
    }

    internal void setMissionAction(bool active, bool turning) //미션 버튼·밸브 동작 동안 본인 팔 표시 (14b — PlayerAnimation 이 켜고 끔)
    {
        missionTurning = turning;
        if (showingMission == active) return;
        if (active && (armsPrefab == null || holdPoint == null || !prepareArms())) return;
        showingMission = active;
        refreshPresentation();
    }

    internal void stopGesture() //공격과 사망 시 제스처 전용 표시 중단
    {
        if (!showingGesture) return;
        showingGesture = false;
        refreshPresentation();
    }

    private void LateUpdate() //제스처 종료와 공격 중단에 맞춰 장착 표시 복원
    {
        if (showingMission && !showingGesture) alignMissionHand();
        if (!showingGesture) return;
        if (health != null && health.Object != null && health.Object.IsValid && !health.CanAct)
        {
            showingGesture = false;
            refreshPresentation();
            if (!health.canActWithoutCctv && arms != null) arms.hide();
            return;
        }
        bool playing = playerAnimator != null &&
            (playerAnimator.GetCurrentAnimatorStateInfo(0).shortNameHash == gestureHash ||
             (playerAnimator.IsInTransition(0) && playerAnimator.GetNextAnimatorStateInfo(0).shortNameHash == gestureHash));
        if (playing) gestureEntered = true;
        if (playing || (!gestureEntered && Time.time - gestureStartTime < 0.5f)) return;
        showingGesture = false;
        refreshPresentation();
    }

    private void alignMissionHand() //팔 자세 복사(실행 순서 -25) 뒤 손끝이 조준점 방향에 오도록 팔 전체를 어깨 중심으로 돌림 (14b)
    {
        if (!aimMissionHand || arms == null || !arms.gameObject.activeInHierarchy || holdPoint == null || holdPoint.parent == null) return;
        findMissionBones();
        arms.transform.localPosition = missionBaseArmsPosition; //매 프레임 기본 자리에서 다시 계산 (누적 없음)
        arms.transform.localRotation = Quaternion.Euler(missionBaseArmsRotation);
        Transform view = holdPoint.parent; //팔 위치의 기준과 같은 카메라 기준
        if (missionShoulder != null) arms.transform.position += view.TransformPoint(missionShoulderPoint) - missionShoulder.position; //어깨를 화면 밖 자리로 옮김
        Vector3 hand; //조준점에 맞출 손 위치
        if (missionTurning && missionLeftHand != null && missionRightHand != null) hand = (missionLeftHand.position + missionRightHand.position) * 0.5f;
        else if (missionFingerTip != null) hand = missionFingerTip.position;
        else return;
        Vector3 pivot = missionShoulder != null ? missionShoulder.position : arms.transform.position; //팔을 돌리는 중심
        Vector3 fromPivot = hand - pivot;
        float reach = fromPivot.magnitude; //어깨에서 손까지 거리는 그대로 둠
        if (reach < 0.01f) return;
        Vector3 toPivot = pivot - view.position;
        float along = Vector3.Dot(view.forward, toPivot);
        float inside = along * along - (toPivot.sqrMagnitude - reach * reach); //조준선과 손이 닿는 구의 교차 판정
        float distance = inside >= 0f ? along + Mathf.Sqrt(inside) : missionAimFallbackReach; //팔을 굽혀 닿지 않으면 조준선 앞쪽 점을 향하기만 함 (카메라 쪽으로 꺾이지 않게)
        if (distance <= 0f) return;
        Vector3 target = view.position + view.forward * distance; //화면 가운데 방향의 목표 손 위치
        Quaternion turn = Quaternion.RotateTowards(Quaternion.identity, Quaternion.FromToRotation(fromPivot, target - pivot), missionAimMaxAngle);
        arms.transform.SetPositionAndRotation(pivot + turn * (arms.transform.position - pivot), turn * arms.transform.rotation);
    }

    private void findMissionBones() //팔 프리팹에서 조준점에 맞출 본을 이름으로 한 번 찾음 (14b — 캐릭터와 같은 본 이름)
    {
        if (missionBonesFound || arms == null) return;
        missionBonesFound = true;
        foreach (Transform bone in arms.GetComponentsInChildren<Transform>(true))
        {
            if (bone.name == "Index3.R_end" || (bone.name == "Index3.R" && missionFingerTip == null)) missionFingerTip = bone;
            else if (bone.name == "Hand.L") missionLeftHand = bone;
            else if (bone.name == "Hand.R") missionRightHand = bone;
            else if (bone.name == "Arm.Up.R") missionShoulder = bone;
        }
        if (missionFingerTip == null) missionFingerTip = missionRightHand;
        if (missionFingerTip == null) Debug.LogWarning("[PlayerFirstPersonItemView] 1인칭 팔에서 오른손 본을 찾지 못해 미션 동작 조준 맞춤을 건너뜁니다.", this);
    }

    private void refreshPresentation() //제스처와 도구에 맞는 팔 위치와 표시 선택
    {
        if (arms == null) return;
        bool twoHanded = displayedData != null && displayedData.usesTwoHandedHold; //양손 도구 전용 표시 여부
        bool missionPose = showingMission && !showingGesture; //엄지척 > 미션 동작 > 도구 (14b)
        arms.transform.localPosition = showingGesture ? gestureArmsPosition :
            missionPose ? missionBaseArmsPosition :
            displayedData != null && displayedData == knifeData ? knifeArmsPosition :
            displayedData != null && displayedData == electricShockBatonData ? electricShockBatonArmsPosition :
            twoHanded ? twoHandedArmsPosition : armsPosition;
        arms.transform.localRotation = showingGesture ? Quaternion.Euler(gestureArmsRotation) :
            missionPose ? Quaternion.Euler(missionBaseArmsRotation) :
            twoHanded ? Quaternion.Euler(twoHandedArmsRotation) : Quaternion.identity;
        if (currentView != null) currentView.SetActive(!showingGesture && !showingMission);
        if (showingGesture || showingMission || currentView != null) arms.show();
        else arms.hide();
    }

    private bool prepareArms() //최초 장착 시 카메라 아래에 팔을 한 번 생성
    {
        if (arms != null) return true;
        if (playerAnimator == null) playerAnimator = GetComponentInChildren<Animator>(true);
        health = GetComponent<PlayerHealth>();
        arms = Instantiate(armsPrefab, holdPoint.parent);
        arms.transform.localPosition = armsPosition;
        arms.transform.localRotation = Quaternion.identity;
        if (arms.initialize(playerAnimator)) return true;
        Debug.LogError("[PlayerFirstPersonItemView] 1인칭 팔 참조를 확인하세요.", this);
        Destroy(arms.gameObject);
        arms = null;
        return false;
    }

    private void OnDisable() //플레이어 비활성화 시 로컬 장착 표시 정리
    {
        showingGesture = false;
        showingMission = false;
        Clear();
    }

    private void OnDestroy() //씬 변경과 디스폰 시 생성한 팔 정리
    {
        if (arms != null) Destroy(arms.gameObject);
    }


    public T GetCurrentView<T>() where T : Component
    {
        if (currentView == null)
            return null;

        return currentView.GetComponentInChildren<T>(true);
    }
}
