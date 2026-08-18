using Fusion;
using UnityEngine;

public class PlayerItemHoldPoint : NetworkBehaviour
{
    [Header("아이템 손 위치")]
    [SerializeField] private Transform handPoint;

    public Transform HandPoint => handPoint;
}