using UnityEngine;

[CreateAssetMenu(fileName = "NewItemData", menuName = "Game/Item Data")]
public class ItemData : ScriptableObject
{
    [SerializeField] private int id;
    [SerializeField] private string itemName;
    [SerializeField] private Sprite icon;

    [Header("1인칭")]
    [SerializeField] private GameObject firstPersonPrefab;

    [Header("무기")]
    [SerializeField] private bool isWeapon; //일반 공격 가능 여부
    [SerializeField, Min(0f)] private float attackDamage; //일반 공격 피해량
    [SerializeField, Min(0f)] private float attackInterval; //일반 공격 사이의 간격

    internal bool IsWeapon => isWeapon && attackDamage > 0f && attackInterval > 0f; //유효한 무기 설정
    internal float AttackDamage => attackDamage; //호스트가 적용할 피해량
    internal float AttackInterval => attackInterval; //호스트가 적용할 공격 간격

    public int Id => id;
    public string ItemName => itemName;
    public Sprite Icon => icon;
    public GameObject FirstPersonPrefab => firstPersonPrefab;
}
