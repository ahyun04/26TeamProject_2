using UnityEngine;

[CreateAssetMenu(fileName = "NewItemData", menuName = "Game/Item Data")]
public class ItemData : ScriptableObject
{
    [SerializeField] private int id;
    [SerializeField] private string itemName;
    [SerializeField] private Sprite icon;

    [Header("1인칭")]
    [SerializeField] private GameObject firstPersonPrefab;

    [Header("들기 모션")]
    [SerializeField] private bool useTwoHandedHold; //양손 들기 모션 사용 여부

    [Header("무기")]
    [SerializeField] private bool isWeapon; //일반 공격 가능 여부
    [SerializeField, Min(0f)] private float attackDamage; //일반 공격 피해량
    [SerializeField, Min(0f)] private float attackInterval; //일반 공격 사이의 간격
    [SerializeField, Min(0.1f)] private float attackRange = 2f; //일반 공격의 최대 사거리
    [SerializeField, Min(0f)] private float attackStaminaCost = 25f; //한 번 휘두를 때 소모할 스태미나

    internal bool IsWeapon => isWeapon && attackDamage > 0f && attackInterval > 0f; //유효한 무기 설정
    internal float AttackDamage => attackDamage; //호스트가 적용할 피해량
    internal float AttackInterval => attackInterval; //호스트가 적용할 공격 간격
    internal float AttackRange => attackRange; //호스트가 판정할 일반 공격 사거리
    internal float AttackStaminaCost => attackStaminaCost; //호스트가 소모할 아이템별 공격 자원
    internal bool usesTwoHandedHold => useTwoHandedHold; //아이템별 들기 모션 선택

    public int Id => id;
    public string ItemName => itemName;
    public Sprite Icon => icon;
    public GameObject FirstPersonPrefab => firstPersonPrefab;
}
