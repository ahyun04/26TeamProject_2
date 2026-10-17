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

    private GameObject currentView;
    private PlayerFirstPersonArmsComponent arms; //로컬 플레이어에게만 생성하는 팔 표시


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
        if (arms != null) arms.showItem(currentView);
    }


    public void Clear()
    {
        if (currentView != null)
        {
            currentView.SetActive(false);
            Destroy(currentView);
        }
        currentView = null;
        if (arms != null) arms.hide();
    }

    private bool prepareArms() //최초 장착 시 카메라 아래에 팔을 한 번 생성
    {
        if (arms != null) return true;
        if (playerAnimator == null) playerAnimator = GetComponentInChildren<Animator>(true);
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
