using UnityEngine;
using System.Collections.Generic;

namespace GameProject.AI.Abilities.Constructor
{
    [CreateAssetMenu(menuName = "Enemy/Ability Data/Constructor/Cube Throw")]
    public sealed class ConstructorCubeThrowDataSO : EnemyAbilityDataSO
    {
        [Header("Cube")]
        public ConstructorCubeKind cubeKind;
        public ConstructorThrownCube projectilePrefab;
        public ConstructorGroundWave groundWavePrefab;
        public GameObject heldCubePrefab;
        public GameObject conveyorPrefab;
        public LayerMask collideLayer = 1 << 6;
        public WeaponBaseSO.DamageInfo damageInfo = new WeaponBaseSO.DamageInfo { damage = 18f };
        public WeaponBaseSO.DamageInfo explosionDamageInfo = new WeaponBaseSO.DamageInfo { damage = 18f };
        [Min(0f)] public float explosionRadius = 5.5f;
        [Min(0.01f)] public float projectileGravity = 26f;
        [Min(0.1f)] public float projectileLifeTime = 4f;
        [Min(0.1f)] public float bombFlightTime = 0.85f;
        [Min(0.1f)] public float groundFlightTime = 0.5f;
        public Vector2 bombAimOffset = new Vector2(0f, 0.55f);

        [Header("Wall And Fixed Height")]
        [Min(0.1f)] public float repositionSpeed = 24f;
        [Min(0.1f)] public float heightAboveFloor = 8f;
        [Min(0.1f)] public float pickupHeightAboveFloor = 6.649545f;
        [Min(0.1f)] public float pickupMoveSpeed = 12f;
        [Min(0.1f)] public float liftHeight = 1f;
        [Min(0.1f)] public float liftMoveSpeed = 6f;
        [Min(0.1f)] public float wallClearance = 4.5f;
        [Min(1f)] public float arenaSearchDistance = 80f;
        [Min(0.01f)] public float arrivalTolerance = 0.08f;
        [Min(0.1f)] public float travelTimeout = 6f;
        [Min(0f)] public float bombThrowStandOff;
        [Min(0.1f)] public float bombMoveSpeed = 30f;
        [Min(0.1f)] public float bombMoveTimeout = 4f;
        [Min(0.1f)] public float oppositeLandingInset = 2.8f;

        [Header("Mounted Cube Actions")]
        public GameObject leftEquipmentPrefab;
        public GameObject rightEquipmentPrefab;
        public Vector3 cargoGripOffset = new Vector3(0f, -1.3f, 0f);
        [Min(0.01f)] public float mountTime = 0.45f;
        [Range(0.05f, 0.95f)] public float mountPoseFraction = 0.65f;
        [Min(2.3f)] public float bombApproachHeightAboveFloor = 3f;
        [Min(0.1f)] public float waveHeightAboveFloor = 8f;
        [Min(0.1f)] public float waveMoveSpeed = 24f;
        [Min(0.01f)] public float waveTelegraphTime = 0.35f;
        [Min(0.1f)] public float waveSlamSpeed = 220f;
        [Min(0.1f)] public float wavePreRiseHeight = 0.8f;
        [Min(0.01f)] public float wavePreRiseTime = 0.3f;
        [Min(0.1f)] public float waveSlamInitialSpeed = 4f;
        [Min(0.1f)] public float waveSlamAcceleration = 120f;
        [Min(0.1f)] public float waveSlamTimeout = 3f;
        [Min(0.01f)] public float bounceReleaseDelay = 0.2f;
        [Min(0.1f)] public float bounceThrowSpeed = 12f;
        public float bounceThrowRiseSpeed = -8f;
        public FeedbackDataSO bombChargeFeedback;
        public FeedbackDataSO groundImpactFeedback;

        [Header("Sequential Assembly And Actions")]
        public AnimationClip assemblyLeftClip;
        public AnimationClip assemblyRightClip;
        public AnimationClip assemblyRiseClip;
        public AnimationClip bombChaseClip;
        public AnimationClip chaseClip;
        public AnimationClip waveBraceClip;
        public AnimationClip waveLeftSlamClip;
        public AnimationClip waveRightSlamClip;
        public AnimationClip bouncePickupClip;
        public AnimationClip bounceThrowClip;
        public Vector3 assemblyCubeOffset = new Vector3(0f, -3.4f, -0.2f);
        [Min(0.01f)] public float unpackTime = 0.45f;
        [Min(0.01f)] public float assemblyStrikeTime = 0.65f;
        [Min(0.01f)] public float assemblyLowerTime = 0.35f;
        [Min(0.01f)] public float assemblyRiseTime = 0.75f;
        [Range(0.05f, 0.95f)] public float assemblyImpactFraction = 0.62f;
        [Min(0f)] public float poseBlendTime = 0.1f;
        [Min(0.1f)] public float bombChaseDuration = 2.5f;
        [Min(0.1f)] public float bombChaseMaxSpeed = 16f;
        [Min(0.1f)] public float bombChaseAcceleration = 44f;
        [Min(0.1f)] public float bombChaseTurnRate = 270f;
        [Min(0.1f)] public float bombDetonationDistance = 2.5f;
        [Min(0f)] public float bombMinimumChaseTime = 0.7f;
        [Min(0f)] public float bombDetonationCommitTime = 0.35f;
        [Min(0.1f)] public float waveBraceHeight = 4.5f;
        [Min(0f)] public float waveStrikeLiftHeight = 1.7f;
        [Min(0.01f)] public float waveBraceTime = 0.16f;
        [Min(0f)] public float waveMechanismExtension = 0.862581f;
        [Min(0.01f)] public float waveStrikeTime = 1.65f;
        [Min(0.01f)] public float waveDownstrokeTime = 0.2f;
        [Min(0.01f)] public float waveReturnTime = 1f;
        [Range(0.05f, 0.95f)] public float waveStrikeImpactFraction = 0.403f / 1.65f;
        [Min(0f)] public float waveStrikeInterval = 0.2f;
        [Min(0.01f)] public float bounceOpenTime = 0.3f;
        [Min(0.01f)] public float bounceThrowTime = 0.25f;
        [Range(0.05f, 0.95f)] public float bounceReleaseFraction = 0.2f;
        public FeedbackDataSO assemblyImpactFeedback;

        [Header("Conveyor Presentation")]
        // Y는 보스가 아닌 실제 바닥 기준이다.
        public Vector3 conveyorOffset = new Vector3(1.2f, 0f, 0.2f);
        [Min(0.01f)] public float conveyorDeployTime = 0.45f;
        [Min(0.01f)] public float cargoSpawnTime = 0.5f;
        [Min(0.01f)] public float feedTime = 1.1f;
        [Min(0.01f)] public float gripTime = 0.35f;
        [Min(0.01f)] public float liftTime = 0.55f;
        [Min(0.01f)] public float swingTime = 0.9f;
        [Range(0.01f, 0.8f)] public float gripPoseFraction = 0.25f;
        [Range(0.05f, 0.9f)] public float carryPoseFraction = 0.4f;
        [Range(0.05f, 0.95f)] public float releaseClipFraction = 0.65f;
        [Min(0.01f)] public float postThrowTime = 0.18f;
        [Min(1)] public int bombThrowCount = 1;
        [Min(0f)] public float bombReloadDelay = 0.35f;
        public float rollerDegreesPerSecond = 720f;
        [Min(0f)] public float beltMarkSpeed = 3.1f;
        [Min(0.1f)] public float beltMarkSpan = 5.2f;
        public FeedbackDataSO deployFeedback;
        public FeedbackDataSO cargoSpawnFeedback;
        public FeedbackDataSO pickupFeedback;
        public FeedbackDataSO throwFeedback;

        public AnimationClip PickupClip => bouncePickupClip != null ? bouncePickupClip
            : animationSet != null ? animationSet.execute : null;
        public AnimationClip ChaseClip => chaseClip != null ? chaseClip : bombChaseClip;

        // 작성 중인 다른 종류의 설정을 요구하지 않고, 현재 종류가 실제로 쓰는 참조만 확인한다.
        public string[] GetMissingReferences()
        {
            var missing = new List<string>();
            if (animationSet == null) missing.Add("공통 애니메이션 세트(animationSet)");
            else
            {
                if (animationSet.windup == null) missing.Add("준비 애니메이션(animationSet.windup)");
                if (animationSet.execute == null) missing.Add("공통 실행 애니메이션(animationSet.execute)");
                if (animationSet.recover == null) missing.Add("복귀 애니메이션(animationSet.recover)");
            }
            if (heldCubePrefab == null) missing.Add("현장 큐브 외형(heldCubePrefab)");
            switch (cubeKind)
            {
                case ConstructorCubeKind.Bomb:
                    if (projectilePrefab == null) missing.Add("논리 폭탄 발사체(projectilePrefab)");
                    AddAssemblyReferences(missing);
                    if (ChaseClip == null) missing.Add("추적 애니메이션(chaseClip 또는 bombChaseClip)");
                    break;
                case ConstructorCubeKind.Shockwave:
                    AddAssemblyReferences(missing);
                    if (groundWavePrefab == null) missing.Add("지면 파도(groundWavePrefab)");
                    if (waveBraceClip == null) missing.Add("내려찍기 준비 애니메이션(waveBraceClip)");
                    if (waveLeftSlamClip == null) missing.Add("왼쪽 내려찍기 애니메이션(waveLeftSlamClip)");
                    if (waveRightSlamClip == null) missing.Add("오른쪽 내려찍기 애니메이션(waveRightSlamClip)");
                    break;
                case ConstructorCubeKind.Bounce:
                    if (projectilePrefab == null) missing.Add("바운스 발사체(projectilePrefab)");
                    if (PickupClip == null) missing.Add("집기 애니메이션(bouncePickupClip 또는 animationSet.execute)");
                    if (bounceThrowClip == null) missing.Add("투척 애니메이션(bounceThrowClip)");
                    break;
                default:
                    missing.Add("지원하는 큐브 종류(cubeKind)");
                    break;
            }
            return missing.ToArray();
        }

        private void AddAssemblyReferences(List<string> missing)
        {
            if (leftEquipmentPrefab == null) missing.Add("왼쪽 집게 장비(leftEquipmentPrefab)");
            if (rightEquipmentPrefab == null) missing.Add("오른쪽 집게 장비(rightEquipmentPrefab)");
            if (assemblyLeftClip == null) missing.Add("왼쪽 압입 애니메이션(assemblyLeftClip)");
            if (assemblyRightClip == null) missing.Add("오른쪽 압입 애니메이션(assemblyRightClip)");
            if (assemblyRiseClip == null) missing.Add("장착 후 단일 상승 애니메이션(assemblyRiseClip)");
        }
    }
}
