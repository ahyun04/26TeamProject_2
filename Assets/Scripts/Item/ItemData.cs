using UnityEngine;

[CreateAssetMenu(fileName = "NewItemData", menuName = "Game/Item Data")]
public class ItemData : ScriptableObject
{
    [SerializeField] private int id;
    [SerializeField] private string itemName;
    [SerializeField] private Sprite icon;

    [Header("1인칭")]
    [SerializeField] private GameObject firstPersonPrefab;

    public int Id => id;
    public string ItemName => itemName;
    public Sprite Icon => icon;
    public GameObject FirstPersonPrefab => firstPersonPrefab;
}
