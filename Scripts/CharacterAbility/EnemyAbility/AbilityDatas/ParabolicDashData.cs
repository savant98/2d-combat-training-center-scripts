using UnityEngine;

[CreateAssetMenu(menuName = "Enemy/Ability Data/Parabolic Dash")]
public class ParabolicDashData : EnemyAbilityData
{
    [Header("Parabolic Settings")]
    public float maxHeight = 4f;
    public float flyDuration = 1f;
    public float maxDistance = 10f;
}
