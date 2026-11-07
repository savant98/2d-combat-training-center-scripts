using UnityEngine;

[CreateAssetMenu(menuName = "Enemy/Ability Data/Sniper Patrol")]
public class SniperPatrolDataSO : FlyingAbilityDataSO
{
    [Header("Patrol")]
    public Vector2 playerOffset = new Vector2(10f, 7f);
    [Min(0f)] public float patrolDuration = 2f;
    [Min(0.1f)] public float sideSwitchInterval = 6.5f;
    [Min(0f)] public float wallMargin = 1.6f;
    [Min(0f)] public float minSideSpace = 2f;

    [Header("Flock")]
    public bool moveAsFlock = true;
    [Min(0.1f)] public float formationSpacing = 4.5f;
    [Range(0.3f, 1f)] public float formationHeightRatio = 0.85f;

    [Header("Teleport")]
    public FeedbackDataSO teleportFeedback;
    [Min(0f)] public float disappearDelay = 0.05f;
    [Min(0f)] public float appearDelay = 0.05f;
}
