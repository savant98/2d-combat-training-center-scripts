using UnityEngine;
using UnityEngine.Serialization;

namespace GameProject.AI.Abilities.Constructor
{
    [CreateAssetMenu(menuName = "Enemy/Ability Data/Constructor/Homing Missile Volley")]
    public sealed class ConstructorHomingMissileDataSO : EnemyAbilityDataSO
    {
        [Header("Center Approach")]
        [Min(0.01f)] public float centerMoveSpeed = 24f;
        [Min(0.1f)] public float centerHeightAboveFloor = 5f;
        [Min(0.01f)] public float centerArrivalTolerance = 0.08f;
        [Min(0.1f)] public float centerTravelTimeout = 6f;
        [Min(1f)] public float arenaSearchDistance = 80f;
        public FeedbackDataSO chargeFeedback;

        [Header("Missile Volley")]
        public ConstructorHomingMissile projectilePrefab;
        public LayerMask collideLayer = 1 << 6;
        public WeaponBaseSO.DamageInfo damageInfo = new WeaponBaseSO.DamageInfo { damage = 12f };
        [Min(0.01f)] public float projectileSpeed = 14f;
        [Min(0.01f)] public float shotInterval = 0.35f;
        [Min(0.01f)] public float launcherSwitchDelay = 1f;
        public FeedbackDataSO launchFeedback;
        public AnimationClip rightFirstExecuteClip;

        [Header("Explosion")]
        [Min(0f)] public float explosionRadius = 1.5f;
        public WeaponBaseSO.DamageInfo explosionDamageInfo = new WeaponBaseSO.DamageInfo { damage = 12f };

        [Header("Guidance")]
        [FormerlySerializedAs("launchStraightTime")]
        [Min(0f)] public float launchDuration = 0.7f;
        [Min(0f)] public float launchTurnRateDegrees = 20f;
        [Min(0f)] public float slowDuration = 0.7f;
        [Range(0f, 1f)] public float slowSpeedRatio = 3f / 14f;
        [Min(0f)] public float slowTurnRateDegrees = 180f;
        [Min(0f)] public float trackingDuration = 3.75f;
        [Min(0f)] public float turnRateDegrees = 180f;
        [Min(0f)] public float accelerationDuration = 0.9f;
        [Range(0f, 1f)] public float initialSpeedRatio = 6f / 14f;
        [Min(0.01f)] public float lifeTime = 5f;

        [Header("Ejection And Inertia")]
        [Range(1f, 89f)] public float ejectionAngleDegrees = 45f;
        [Min(0f)] public float coastTurnDecayDuration = 1.8f;
        [Min(0f)] public float steeringAcceleration = 540f;
    }
}
