using UnityEngine;

[CreateAssetMenu(menuName = "Player/Ability Data/Move Data")]
public class PlayerMoveData : PlayerAbilityData
{
    [Header("Move Option")]
    public float moveSpeed = 10f;
}
