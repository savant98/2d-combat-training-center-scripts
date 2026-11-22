using UnityEngine;

[CreateAssetMenu(menuName = "Enemy/Ability Data/Sniper Patrol")]
public class SniperPatrolDataSO : FlyingAbilityDataSO
{
    [Header("Patrol")]
    public Vector2 playerOffset = new Vector2(8f, 5f);
    [Min(0f)] public float patrolDuration = 2f;
    [Min(0f), Tooltip("각 순찰 시작 시 기준 대기 시간에 더하는 무작위 지연의 최대 초입니다.")]
    public float attackStartJitter = 1f;
    [Min(0.1f)] public float sideSwitchInterval = 6.5f;
    [Min(0f)] public float wallMargin = 1.6f;
    [Min(0f)] public float minSideSpace = 2f;

    [Header("Flock")]
    public bool moveAsFlock = true;
    [Min(0.1f)] public float formationSpacing = 4.5f;
    [Range(0.3f, 1f)] public float formationHeightRatio = 0.85f;

    [Header("Teleport")]
    [Min(0f), Tooltip("현재 수평 거리보다 더 물러날 때 확보하는 추가 거리입니다.")]
    public float retreatExtraDistance = 3f;
    public FeedbackDataSO teleportFeedback;
    [Min(0f)] public float disappearDelay = 0.05f;
    [Min(0f)] public float appearDelay = 0.05f;
}
