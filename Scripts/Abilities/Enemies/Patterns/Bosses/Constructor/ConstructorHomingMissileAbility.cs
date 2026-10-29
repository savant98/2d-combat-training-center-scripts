using UnityEngine;

namespace GameProject.AI.Abilities.Constructor
{
    public sealed class ConstructorHomingMissileAbility : AiAbility
    {
        private const int MissileCount = 4;
        private const int MissilesPerLauncher = 2;
        private const float RecoilTailTime = 0.44f;
        private static readonly float[] ShotAnimationTimes = { 0f, 0.35f, 1.35f, 1.7f };

        [Header("Launcher Points")]
        [SerializeField] private Transform launchPointLeft;
        [SerializeField] private Transform launchPointRight;

        [Header("Animated Pose")]
        [SerializeField] private Transform[] animatedParts;

        public int FiredCount { get; private set; }
        public int FiredVolleyCount => FiredCount / MissilesPerLauncher;
        public int FirstLauncherSide { get; private set; }
        public bool IsMovingToCenter { get; private set; }
        public Vector2 CenterTarget { get; private set; }

        private ConstructorHomingMissileDataSO MissileData => data as ConstructorHomingMissileDataSO;
        private Transform playerTarget;
        private bool previousGravity;
        private bool previousIgnoreForces;
        private bool previousLockFacing;
        private int previousFacing;
        private bool ownsMovement;
        private bool restPoseCached;
        private bool configurationWarningShown;
        private Vector3[] restPositions;
        private Quaternion[] restRotations;
        private float approachStartedAt;
        private float preparationStartedAt;
        private float lastShotAt;
        private AnimationClip presentationClip;

        private void Awake() => CacheRestPose();

        protected override void OnWindupStart()
        {
            CacheRestPose();
            FiredCount = 0;
            FirstLauncherSide = 0;
            var settings = MissileData;
            if (settings == null || settings.projectilePrefab == null || launchPointLeft == null
                || launchPointRight == null || settings.rightFirstExecuteClip == null || WorldEntityPool.Instance == null)
            {
                if (!configurationWarningShown)
                {
                    Debug.LogWarning("Constructor 유도 미사일 설정 또는 발사구·애니메이션 연결이 누락되어 패턴을 중단합니다.", this);
                    configurationWarningShown = true;
                }
                StopAbility();
                return;
            }

            previousGravity = controller.GravityOn;
            previousIgnoreForces = controller.IgnoreExternalForces;
            previousLockFacing = controller.LockFacing;
            previousFacing = controller.Facing;
            ownsMovement = true;
            controller.GravityOn = false;
            controller.IgnoreExternalForces = true;
            controller.LockFacing = true;
            playerTarget = GameManager.Instance != null && GameManager.Instance.Player != null
                ? GameManager.Instance.Player.transform : null;
            presentationClip = data.animationSet.windup;
            approachStartedAt = Time.time;
            preparationStartedAt = -1f;
            IsMovingToCenter = true;
            HoldStill();

            // 현재 방의 물리 경계를 기존 모터로 읽고, 별도 방 좌표나 물리 질의를 만들지 않는다.
            RaycastMotor2D.InvalidateAttackQuerySync();
            RaycastMotor2D.PrepareAttackQueries();
            Vector2 origin = (Vector2)controller.transform.position + Vector2.up * 0.1f;
            var left = controller.Motor.RaycastEnvironment(origin, Vector2.left, settings.arenaSearchDistance);
            var right = controller.Motor.RaycastEnvironment(origin, Vector2.right, settings.arenaSearchDistance);
            if (left.collider == null || right.collider == null || right.point.x <= left.point.x)
            {
                // 경계가 없는 곳에서 임의의 위치를 중앙으로 취급해 발사하지 않는다.
                StopAbility();
                return;
            }
            float centerX = (left.point.x + right.point.x) * 0.5f;
            var floor = controller.Motor.RaycastEnvironment(new Vector2(centerX, origin.y),
                Vector2.down, settings.arenaSearchDistance);
            if (floor.collider == null || floor.distance <= Mathf.Epsilon)
            {
                // 중앙의 바닥을 확인할 수 없으면 임의 높이에서 발사하지 않는다.
                StopAbility();
                return;
            }
            CenterTarget = new Vector2(centerX, floor.point.y + settings.centerHeightAboveFloor);
            MoveToCenter();
        }

        protected override void OnWindup(float elapsed)
        {
            if (IsMovingToCenter)
            {
                if (Time.time - approachStartedAt >= MissileData.centerTravelTimeout)
                {
                    StopAbility();
                    return;
                }
                MoveToCenter();
            }
            else HoldStill();
        }

        protected override bool CanFinishWindup(float elapsed) => IsRunning
            && !IsMovingToCenter && preparationStartedAt >= 0f
            && Time.time - preparationStartedAt >= data.windupTime;

        private void MoveToCenter()
        {
            Vector2 delta = CenterTarget - (Vector2)controller.transform.position;
            if (delta.magnitude <= MissileData.centerArrivalTolerance)
            {
                IsMovingToCenter = false;
                preparationStartedAt = Time.time;
                HoldStill();
                // 월드 좌우 발사구와 반동을 맞추도록 중앙에서는 정면 기준 자세를 사용한다.
                controller.FacingUpdate(1);
                if (feedbackPlayer != null)
                    feedbackPlayer.Play(MissileData.chargeFeedback, windupPoint != null ? windupPoint : transform);
                return;
            }
            controller.Velocity = Vector2.ClampMagnitude(delta / Time.fixedDeltaTime, MissileData.centerMoveSpeed);
        }

        protected override void OnExecuteStart()
        {
            HoldStill();
            // 첫 발 직전의 플레이어 위치로 반대쪽을 정하고 이번 패턴 동안 유지한다.
            FirstLauncherSide = playerTarget != null && playerTarget.gameObject.activeInHierarchy
                && playerTarget.position.x < controller.transform.position.x ? 1 : -1;
            presentationClip = FirstLauncherSide > 0 ? MissileData.rightFirstExecuteClip : data.animationSet.execute;
            FireNextMissile();
        }

        protected override void OnExecute(float elapsed)
        {
            HoldStill();
            var settings = MissileData;
            if (settings == null) return;

            // 같은 발사구의 두 발 사이와 발사구 교대 사이를 실제 마지막 발사 시각에서 센다.
            // 갱신이 늦어져도 한 번에는 한 발만 생성한다.
            float interval = FiredCount == MissilesPerLauncher ? settings.launcherSwitchDelay : settings.shotInterval;
            if (FiredCount < MissileCount && Time.time - lastShotAt >= Mathf.Max(0.01f, interval))
                FireNextMissile();
        }

        protected override bool CanFinishExecute(float elapsed) => IsRunning
            && FiredCount == MissileCount && elapsed >= data.executeTime
            && Time.time - lastShotAt >= RecoilTailTime;

        protected override void OnRecoverStart()
        {
            presentationClip = null;
            HoldStill();
        }
        protected override void OnRecover(float elapsed) => HoldStill();

        private bool FireNextMissile()
        {
            var settings = MissileData;
            var pool = WorldEntityPool.Instance;
            if (FiredCount >= MissileCount || FirstLauncherSide == 0 || settings == null || settings.projectilePrefab == null
                || pool == null || launchPointLeft == null || launchPointRight == null)
            {
                StopAbility();
                return false;
            }

            int side = FiredCount < MissilesPerLauncher ? FirstLauncherSide : -FirstLauncherSide;
            FireMissile(settings, pool, side < 0 ? launchPointLeft : launchPointRight, side);
            FiredCount++;
            lastShotAt = Time.time;
            return true;
        }

        private void FireMissile(ConstructorHomingMissileDataSO settings, WorldEntityPool pool,
            Transform launchPoint, int side)
        {
            // 월드 기준 좌상단과 우상단으로 사출한다. 이동과 외형은 같은 방향을 사용한다.
            float radians = Mathf.Clamp(settings.ejectionAngleDegrees, 1f, 89f) * Mathf.Deg2Rad;
            Vector2 direction = new Vector2(side * Mathf.Cos(radians), Mathf.Sin(radians));
            var missile = pool.Spawn(settings.projectilePrefab, launchPoint.position, Quaternion.identity);
            missile.ConfigureGuidance(playerTarget, direction, settings.launchDuration,
                settings.launchTurnRateDegrees, settings.trackingDuration, settings.turnRateDegrees, settings.accelerationDuration,
                settings.initialSpeedRatio, settings.lifeTime, settings.slowDuration, settings.slowSpeedRatio,
                settings.slowTurnRateDegrees);
            missile.ConfigureEjection(side, settings.ejectionAngleDegrees,
                settings.coastTurnDecayDuration, settings.steeringAcceleration);
            missile.ConfigureExplosion(settings.explosionRadius, settings.explosionDamageInfo);
            missile.Initialize(controller, settings.damageInfo, settings.projectileSpeed, 1, settings.collideLayer);

            if (feedbackPlayer != null && settings.launchFeedback != null)
            {
                // 발사 불꽃도 좌우 대각선 사출 방향에 맞춘다.
                float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
                feedbackPlayer.PlayExact(settings.launchFeedback,
                    launchPoint.TransformPoint(settings.launchFeedback.offset),
                    Quaternion.Euler(0f, 0f, angle));
            }
        }

        private void HoldStill()
        {
            if (ownsMovement && controller != null)
                controller.Velocity = Vector2.zero;
        }

        private void LateUpdate()
        {
            if (!IsRunning || animator == null || presentationClip == null) return;
            if (State == AbilityState.Windup)
            {
                // 이동 시간은 준비 동작을 소비하지 않는다.
                float time = IsMovingToCenter ? 0f : Time.time - preparationStartedAt;
                presentationClip.SampleAnimation(animator.gameObject, Mathf.Clamp(time, 0f, presentationClip.length));
            }
            else if (State == AbilityState.Execute)
            {
                // 발사 횟수로 Clip의 해당 반동에 맞추고, 아직 쏘지 않은 다음 반동 앞에서 기다린다.
                int shotIndex = Mathf.Clamp(FiredCount - 1, 0, MissileCount - 1);
                float clipScale = Mathf.Max(0.001f, presentationClip.length - RecoilTailTime) / ShotAnimationTimes[MissileCount - 1];
                float sinceShot = Time.time - lastShotAt;
                if (FiredCount < MissileCount)
                    sinceShot = Mathf.Min(sinceShot,
                        (ShotAnimationTimes[shotIndex + 1] - ShotAnimationTimes[shotIndex]) * clipScale - 0.001f);
                float time = ShotAnimationTimes[shotIndex] * clipScale + sinceShot;
                presentationClip.SampleAnimation(animator.gameObject, Mathf.Clamp(time, 0f, presentationClip.length));
            }
        }

        protected override void OnStop() => Cleanup();

        private void OnDisable()
        {
            if (IsRunning && animator != null) StopAbility();
            else Cleanup();
        }

        private void Cleanup()
        {
            playerTarget = null;
            IsMovingToCenter = false;
            presentationClip = null;
            if (!ownsMovement) return;
            ownsMovement = false;
            RestoreRestPose();
            if (controller == null) return;
            controller.Velocity = Vector2.zero;
            controller.GravityOn = previousGravity;
            controller.IgnoreExternalForces = previousIgnoreForces;
            controller.LockFacing = previousLockFacing;
            controller.FacingUpdate(previousFacing);
        }

        private void CacheRestPose()
        {
            if (restPoseCached || animatedParts == null || animatedParts.Length == 0) return;
            restPositions = new Vector3[animatedParts.Length];
            restRotations = new Quaternion[animatedParts.Length];
            for (int i = 0; i < animatedParts.Length; i++)
            {
                if (animatedParts[i] == null) continue;
                restPositions[i] = animatedParts[i].localPosition;
                restRotations[i] = animatedParts[i].localRotation;
            }
            restPoseCached = true;
        }

        private void RestoreRestPose()
        {
            if (!restPoseCached || animatedParts == null) return;
            int count = Mathf.Min(animatedParts.Length, restPositions.Length);
            for (int i = 0; i < count; i++)
            {
                if (animatedParts[i] == null) continue;
                // 게임 이동 루트는 건드리지 않고 이번 패턴이 사용하는 시각 관절만 복원한다.
                animatedParts[i].localPosition = restPositions[i];
                animatedParts[i].localRotation = restRotations[i];
            }
        }
    }
}
