using UnityEngine;

/// <summary>
/// 로컬 플레이어의 1인칭 아이템 표시 
/// </summary>
public class PlayerFirstPersonItemView : MonoBehaviour
{
    [SerializeField] private Transform holdPoint;

    private GameObject currentView;


    public void Show(ItemData data)
    {
        Clear();

        if (data == null)
            return;

        if (data.FirstPersonPrefab == null)
            return;

        if (holdPoint == null)
            return;

        currentView =
            Instantiate(data.FirstPersonPrefab, holdPoint);

        currentView.transform.localPosition = Vector3.zero;

        currentView.transform.localRotation = Quaternion.identity;

        currentView.transform.localScale = Vector3.one;
    }


    public void Clear()
    {
        if (currentView == null)
            return;

        Destroy(currentView);

        currentView = null;
    }


    public T GetCurrentView<T>() where T : Component
    {
        if (currentView == null)
            return null;

        return currentView.GetComponentInChildren<T>(true);
    }
}