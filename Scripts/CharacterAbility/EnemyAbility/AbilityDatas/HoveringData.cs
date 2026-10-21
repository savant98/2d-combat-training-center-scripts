using UnityEngine;

[CreateAssetMenu(menuName = "Enemy/Ability Data/Hovering Data")]
public class HoveringData : EnemyAbilityData
{
    [Header("Hover Settings")]
    public Vector2 desiredOffset = new(2.5f, 2.0f);// 플레이어 기준 (x, y)
    public float maxHoverTime = 10f;

    [Header("Desired Offset Switch")]
    public float sideSwitchCooldown = 1.5f;
    public float sideSwitchChance = 0.25f;

    [Header("Speed Settings")]
    public float hoverSpeed = 5f;
    public float acceleration = 6f;
    public float jitterAmount = 0.3f;
    public float jitterSpeed = 2f;

    [Header("Attack Settings")]
    public float minAttackTime = 1.5f;
    public float attackHeightToTolerance = 4f;
    public float minAttackXDistance = 5f;
    public float maxAttackXDistance = 10f;
}
