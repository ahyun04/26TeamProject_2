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
    [SerializeField] private Vector3 gestureArmsPosition = new Vector3(0.02f, -0.68f, 1.2f); //엄지척을 보여줄 팔 위치
    [SerializeField] private Vector3 gestureArmsRotation = new Vector3(-60f, 0f, 0f); //팔이 엄지를 가리지 않도록 본인 표시만 기울이는 각도

    private GameObject currentView;
    private PlayerFirstPersonArmsComponent arms; //로컬 플레이어에게만 생성하는 팔 표시
    private ItemData displayedData; //제스처 종료 시 복원할 도구 위치
    private bool showingGesture; //빈손에서도 제스처를 표시하는 상태
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

    internal void stopGesture() //공격과 사망 시 제스처 전용 표시 중단
    {
        if (!showingGesture) return;
        showingGesture = false;
        refreshPresentation();
    }

    private void LateUpdate() //제스처 종료와 공격 중단에 맞춰 장착 표시 복원
    {
        if (!showingGesture) return;
        if (health != null && health.Object != null && health.Object.IsValid && !health.CanAct)
        {
            showingGesture = false;
            if (arms != null) arms.hide();
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

    private void refreshPresentation() //제스처와 도구에 맞는 팔 위치와 표시 선택
    {
        if (arms == null) return;
        bool twoHanded = displayedData != null && displayedData.usesTwoHandedHold; //양손 도구 전용 표시 여부
        arms.transform.localPosition = showingGesture ? gestureArmsPosition :
            displayedData != null && displayedData == knifeData ? knifeArmsPosition :
            twoHanded ? twoHandedArmsPosition : armsPosition;
        arms.transform.localRotation = showingGesture ? Quaternion.Euler(gestureArmsRotation) :
            twoHanded ? Quaternion.Euler(twoHandedArmsRotation) : Quaternion.identity;
        if (currentView != null) currentView.SetActive(!showingGesture);
        if (showingGesture || currentView != null) arms.show();
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
