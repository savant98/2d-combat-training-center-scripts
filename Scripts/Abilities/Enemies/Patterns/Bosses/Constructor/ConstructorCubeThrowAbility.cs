using System.Collections.Generic;
using UnityEngine;

namespace GameProject.AI.Abilities.Constructor
{
    [DefaultExecutionOrder(-50)]
    public sealed class ConstructorCubeThrowAbility : AiAbility
    {
        public enum CubeThrowPhase
        {
            Inactive, Approach, Deploy, CargoSpawn, Feed, Grip, Lift, Reposition, Swing, Settle, ReturnPickup, Reload, Recover,
            Mount, BombApproach, BombCharge, WaveReposition, WaveTelegraph, WaveSlam, BounceRelease, WaveHoist,
            Unpack, AssemblyLeft, AssemblyRight, BombChase, WaveStrike, BounceWindup, BounceThrow, WaveBrace, BounceReposition,
            AssemblyLower
        }

        [SerializeField] private Transform gripPoint;
        [SerializeField] private Transform leftGripPoint;
        [SerializeField] private Transform rightGripPoint;
        [SerializeField] private Transform mountPoint;
        [SerializeField] private Transform leftEquipmentPoint;
        [SerializeField] private Transform rightEquipmentPoint;
        [SerializeField] private Transform[] animatedParts;

        public CubeThrowPhase CurrentPhase { get; private set; }
        public int WallSide { get; private set; }
        public Vector2 WallTarget { get; private set; }
        public Vector2 PickupTarget { get; private set; }
        public Vector2 CarryTarget { get; private set; }
        public Vector2 ThrowTarget { get; private set; }
        public Vector2 LastLaunchVelocity { get; private set; }
        public Vector3 LastReleasePosition { get; private set; }
        public int FiredCount { get; private set; }
        public int PlannedThrowCount => Settings != null && Settings.cubeKind == ConstructorCubeKind.Shockwave ? 3 : 1;
        public Vector2 ActionTarget { get; private set; }
        public Vector2 BombWarningCenter { get; private set; }
        public Vector2 GroundImpactPoint { get; private set; }
        public int GroundImpactCount { get; private set; }
        public int WaveStrikeCount => GroundImpactCount;
        public int StrikeIndex => Mathf.Min(GroundImpactCount, 2);
        public int CurrentStrikeSide { get; private set; }
        public int AssemblyIndex { get; private set; }
        public int AssemblyImpactCount { get; private set; }
        public bool LeftEquipped => equipment[0] != null;
        public bool RightEquipped => equipment[1] != null;
        public bool IsCargoMounted => LeftEquipped && RightEquipped;
        public bool BombDetonated { get; private set; }
        public float MountProgress => AssemblyImpactCount * 0.5f;
        public float CurrentClipProgress { get; private set; }
        public float BombChaseElapsed { get; private set; }
        public float BombChaseSpeed { get; private set; }
        public Vector2 ChaseDirection { get; private set; }
        public GameObject LeftEquipment => equipment[0]?.root;
        public GameObject RightEquipment => equipment[1]?.root;
        public GameObject LeftAssemblyCargo => assembly[0]?.root;
        public GameObject RightAssemblyCargo => assembly[1]?.root;
        public Vector3 LeftAssemblyPosition { get; private set; }
        public Vector3 RightAssemblyPosition { get; private set; }
        public Vector3 MountPosition => leftEquipmentPoint != null && rightEquipmentPoint != null
            ? (leftEquipmentPoint.position + rightEquipmentPoint.position) * 0.5f
            : mountPoint != null ? mountPoint.position : transform.position;
        public ConstructorThrownCube ActiveBomb => activeBomb;
        public Vector3 GripCenter => leftGripPoint != null && rightGripPoint != null
            ? (leftGripPoint.position + rightGripPoint.position) * 0.5f : transform.position;
        public GameObject ActiveConveyor => null;
        public GameObject HeldVisual => cargo != null ? cargo : LeftAssemblyCargo != null ? LeftAssemblyCargo : RightAssemblyCargo;

        private ConstructorCubeThrowDataSO Settings => data as ConstructorCubeThrowDataSO;
        private AnimationClip PickupClip => Settings != null ? Settings.PickupClip : null;
        private AnimationClip ChaseClip => Settings != null ? Settings.ChaseClip : null;
        private Transform playerTarget;
        private GameObject cargo;
        private Vector3 cargoFullScale;
        private SpawnRenderer[] cargoSpawnRenderers;
        private readonly AssemblyVisual[] assembly = new AssemblyVisual[2];
        private readonly EquipmentVisual[] equipment = new EquipmentVisual[2];
        private Vector3[] restPositions;
        private Quaternion[] restRotations, phaseStartRotations;
        private bool restPoseCached, ownsMovement, previousGravity, previousIgnoreForces, previousLockFacing;
        private int previousFacing, activeBombLease, lastChaseFrame = -1;
        private float leftWallX, rightWallX, arenaCenterX, floorY, phaseStartedAt, approachStartedAt;
        private float recoverArrivedAt, lastChaseAt, chaseStartedAt;
        private Vector2 bombChaseVelocity;
        private readonly Vector2[] assemblyBodyTargets = new Vector2[2];
        private Vector2 fixedMoveTarget;
        private float fixedMoveSpeed;
        private bool hasFixedMoveTarget;
        private Vector2 phaseMoveStart, recoveryTarget;
        private Vector3 bounceSpawnPosition;
        private ConstructorThrownCube activeBomb;
        private AnimationClip presentationClip;
        private AnimationClip impactClip;
        private int impactFrame = -1;
        private float impactProgress;
        private bool phaseImpact;
        private string lastConfigurationFailure;

        private sealed class SpawnRenderer
        {
            public Renderer renderer;
            public MaterialPropertyBlock block;
            public Color color, baseColor, emission, rendererColor;
            public float intensity;
            public bool hasColor, hasBaseColor, hasEmission, hasRendererColor, hasIntensity;
        }
        private sealed class AssemblyVisual
        {
            public GameObject root;
            public Vector3 scale, origin;
            public Transform left, right;
            public Vector3 leftRest, rightRest;
            public Quaternion leftRotation, rightRotation;
            public SpawnRenderer[] renderers;
        }
        private sealed class EquipmentVisual
        {
            public GameObject root;
            public Transform mechanism;
            public Transform sleeve;
            public Vector3 mechanismRest;
            public Vector3 sleeveRestScale;
            public Vector3 sleeveRestPosition;
            public float sleeveRestLength = 1.55f;
            public SpawnRenderer[] renderers;
        }

        private void Awake() => CacheRestPose();

        private void FixedUpdate()
        {
            if (!IsRunning || !ownsMovement || controller == null || !UsesFixedMoveTarget
                || !hasFixedMoveTarget) return;
            // Controller가 이동하기 직전에 남은 거리를 다시 계산한다. 한 렌더 프레임에 물리 tick이
            // 여러 번 실행되어도 이전 tick의 도착 속도를 재사용해 타격점이나 회수점을 지나치지 않는다.
            if (CurrentPhase == CubeThrowPhase.WaveStrike)
                MoveTo(GetWaveMoveTarget(Mathf.Max(0f, Time.fixedTime - phaseStartedAt)), Settings.waveSlamSpeed);
            else MoveTo(fixedMoveTarget, fixedMoveSpeed);
        }

        protected override void OnWindupStart()
        {
            CacheRestPose();
            FiredCount = GroundImpactCount = AssemblyImpactCount = 0;
            AssemblyIndex = CurrentStrikeSide = 0;
            BombDetonated = false;
            impactFrame = -1;
            BombChaseElapsed = BombChaseSpeed = 0f;
            bombChaseVelocity = Vector2.zero;
            ActionTarget = BombWarningCenter = GroundImpactPoint = ThrowTarget = LastLaunchVelocity = Vector2.zero;
            LastReleasePosition = Vector3.zero;
            playerTarget = GameManager.Instance?.Player != null ? GameManager.Instance.Player.transform : null;
            var s = Settings;
            if (GetMissingConfigurationFields().Length > 0)
            { ConfigurationFailure(); return; }
            lastConfigurationFailure = null;
            previousGravity = controller.GravityOn;
            previousIgnoreForces = controller.IgnoreExternalForces;
            previousLockFacing = controller.LockFacing;
            previousFacing = controller.Facing;
            ownsMovement = true;
            controller.GravityOn = false;
            controller.IgnoreExternalForces = true;
            controller.LockFacing = true;
            HoldStill();
            RaycastMotor2D.InvalidateAttackQuerySync();
            RaycastMotor2D.PrepareAttackQueries();
            Vector2 origin = (Vector2)controller.transform.position + Vector2.up * .1f;
            var left = controller.Motor.RaycastEnvironment(origin, Vector2.left, s.arenaSearchDistance);
            var right = controller.Motor.RaycastEnvironment(origin, Vector2.right, s.arenaSearchDistance);
            if (left.collider == null || right.collider == null || right.point.x <= left.point.x) { StopAbility(); return; }
            leftWallX = left.point.x; rightWallX = right.point.x; arenaCenterX = (leftWallX + rightWallX) * .5f;
            var floor = controller.Motor.RaycastEnvironment(new Vector2(arenaCenterX, origin.y), Vector2.down, s.arenaSearchDistance);
            if (floor.collider == null || floor.distance <= Mathf.Epsilon || rightWallX - leftWallX <= s.wallClearance * 2f)
            { StopAbility(); return; }
            floorY = floor.point.y;
            WallSide = playerTarget.position.x < arenaCenterX ? 1 : -1;
            WallTarget = new Vector2(WallSide < 0 ? leftWallX + s.wallClearance : rightWallX - s.wallClearance, floorY + s.heightAboveFloor);
            PickupTarget = new Vector2(WallTarget.x, floorY + s.pickupHeightAboveFloor);
            CarryTarget = PickupTarget + Vector2.up * s.liftHeight;
            controller.FacingUpdate(-WallSide);
            approachStartedAt = Time.time;
            EnterPhase(CubeThrowPhase.Approach);
            MoveTo(WallTarget, s.repositionSpeed);
        }

        protected override void OnWindup(float elapsed)
        {
            if (CurrentPhase != CubeThrowPhase.Approach) { HoldStill(); return; }
            if (Time.time - approachStartedAt >= Settings.travelTimeout) { StopAbility(); return; }
            if (MoveTo(WallTarget, Settings.repositionSpeed)) EnterPhase(CubeThrowPhase.Deploy);
        }
        protected override bool CanFinishWindup(float elapsed) => IsRunning && CurrentPhase == CubeThrowPhase.Deploy
            && Time.time - phaseStartedAt >= data.windupTime;

        protected override void OnExecuteStart()
        {
            HoldStill();
            if (Settings.cubeKind == ConstructorCubeKind.Bounce)
            {
                // 전용 픽업 자세의 실제 턱 끝에서 생성 위치를 구한다. 컨베이어는 만들지 않는다.
                PickupClip.SampleAnimation(animator.gameObject, Settings.gripPoseFraction * PickupClip.length);
                UpdateGripAnchor();
                bounceSpawnPosition = GripCenter + Vector3.up * (Settings.cargoGripOffset.y + PickupTarget.y - controller.transform.position.y);
                cargo = Instantiate(Settings.heldCubePrefab, bounceSpawnPosition, Quaternion.identity);
                cargoFullScale = cargo.transform.localScale; cargoSpawnRenderers = CacheRenderers(cargo);
            }
            else
            {
                // 큐브는 지면에 두고, 압입 자세의 가동 턱 끝이 닿는 위치와 몸체 높이를 구한다.
                assembly[0] = SpawnGroundAssembly(0);
                assembly[1] = SpawnGroundAssembly(1);
                LeftAssemblyPosition = assembly[0].origin;
                RightAssemblyPosition = assembly[1].origin;
                data.animationSet.windup.SampleAnimation(animator.gameObject, data.animationSet.windup.length);
            }
            EnterPhase(CubeThrowPhase.CargoSpawn);
            PlayFeedback(Settings.cargoSpawnFeedback, HeldVisual != null ? HeldVisual.transform.position : transform.position);
        }

        protected override void OnExecute(float elapsed)
        {
            var s = Settings; float t = Time.time - phaseStartedAt;
            switch (CurrentPhase)
            {
                case CubeThrowPhase.CargoSpawn:
                    HoldStill();
                    if (t >= s.cargoSpawnTime) EnterPhase(s.cubeKind == ConstructorCubeKind.Bounce ? CubeThrowPhase.BounceWindup : CubeThrowPhase.Unpack);
                    break;
                case CubeThrowPhase.Unpack:
                    HoldStill();
                    if (t >= s.unpackTime) { AssemblyIndex = 0; phaseMoveStart = controller.transform.position; EnterPhase(CubeThrowPhase.AssemblyLower); }
                    break;
                case CubeThrowPhase.AssemblyLower:
                    if (t >= s.travelTimeout) { StopAbility(); return; }
                    if (MoveAlongPhase(assemblyBodyTargets[0], s.assemblyLowerTime, s.pickupMoveSpeed, t))
                        EnterPhase(CubeThrowPhase.AssemblyLeft);
                    break;
                case CubeThrowPhase.AssemblyLeft:
                case CubeThrowPhase.AssemblyRight:
                    if (t >= s.travelTimeout) { StopAbility(); return; }
                    MoveTo(assemblyBodyTargets[0], s.pickupMoveSpeed);
                    CurrentStrikeSide = AssemblyIndex == 0 ? -1 : 1;
                    if (!phaseImpact && t >= s.assemblyStrikeTime * s.assemblyImpactFraction
                        && Mathf.Abs(controller.transform.position.y - assemblyBodyTargets[AssemblyIndex].y) < .08f)
                    { StampEquipment(AssemblyIndex); return; }
                    if (t >= s.assemblyStrikeTime && phaseImpact)
                    {
                        DestroyAssembly(AssemblyIndex);
                        if (AssemblyIndex == 0) { AssemblyIndex = 1; EnterPhase(CubeThrowPhase.AssemblyRight); }
                        else { phaseMoveStart = controller.transform.position; EnterPhase(CubeThrowPhase.Mount); }
                    }
                    break;
                case CubeThrowPhase.Mount:
                    if (t >= s.travelTimeout) { StopAbility(); return; }
                    if (MoveAlongPhase(WallTarget, s.assemblyRiseTime, s.liftMoveSpeed, t)) BeginEquippedAction();
                    break;
                case CubeThrowPhase.BombChase: UpdateBombChase(); break;
                case CubeThrowPhase.BombCharge:
                    HoldStill();
                    if (activeBomb != null && activeBomb.LeaseId == activeBombLease && activeBomb.HasResolved)
                    { BombDetonated = true; DestroyEquipment(); EnterPhase(CubeThrowPhase.Settle); }
                    else if (!OwnsActiveBomb() || t >= s.travelTimeout) StopAbility();
                    break;
                case CubeThrowPhase.WaveReposition:
                    if (t >= s.travelTimeout) { StopAbility(); return; }
                    if (MoveTo(ActionTarget, s.waveMoveSpeed))
                    { phaseMoveStart = controller.transform.position; ActionTarget = new Vector2(arenaCenterX, floorY + s.waveBraceHeight + s.waveStrikeLiftHeight); EnterPhase(CubeThrowPhase.WaveBrace); }
                    break;
                case CubeThrowPhase.WaveBrace:
                    if (t >= s.travelTimeout) { StopAbility(); return; }
                    if (MoveAlongPhase(ActionTarget, s.waveBraceTime, s.repositionSpeed, t)) EnterPhase(CubeThrowPhase.WaveTelegraph);
                    break;
                case CubeThrowPhase.WaveTelegraph:
                    HoldStill();
                    if (t >= s.waveTelegraphTime) { CurrentStrikeSide = -1; EnterPhase(CubeThrowPhase.WaveStrike); }
                    break;
                case CubeThrowPhase.WaveStrike:
                    if (t >= s.travelTimeout) { StopAbility(); return; }
                    UpdateWaveStrikeMovement(t);
                    if (!phaseImpact && t >= s.waveStrikeTime * s.waveStrikeImpactFraction
                        && Mathf.Abs(controller.transform.position.y - floorY - s.waveBraceHeight) < .08f)
                    { ResolveWaveStrike(); return; }
                    if (t >= s.waveStrikeTime + s.waveStrikeInterval && phaseImpact)
                    {
                        if (GroundImpactCount >= 3) EnterPhase(CubeThrowPhase.Settle);
                        else { CurrentStrikeSide = GroundImpactCount == 1 ? 1 : -1; EnterPhase(CubeThrowPhase.WaveStrike); }
                    }
                    break;
                case CubeThrowPhase.BounceWindup:
                    HoldStill();
                    if (t >= s.bounceOpenTime) { phaseMoveStart = controller.transform.position; EnterPhase(CubeThrowPhase.Grip); }
                    break;
                case CubeThrowPhase.Grip:
                    if (t >= s.travelTimeout) { StopAbility(); return; }
                    if (MoveAlongPhase(PickupTarget, s.gripTime, s.pickupMoveSpeed, t))
                    {
                        SampleClip(PickupClip, s.gripPoseFraction, false); UpdateGripAnchor();
                        cargo.transform.SetParent(gripPoint, false); cargo.transform.localPosition = s.cargoGripOffset;
                        PlayFeedback(s.pickupFeedback, cargo.transform.position);
                        phaseMoveStart = controller.transform.position; EnterPhase(CubeThrowPhase.Lift);
                    }
                    break;
                case CubeThrowPhase.Lift:
                    if (t >= s.travelTimeout) { StopAbility(); return; }
                    if (MoveAlongPhase(CarryTarget, s.liftTime, s.liftMoveSpeed, t))
                        EnterPhase(CubeThrowPhase.BounceThrow);
                    break;
                case CubeThrowPhase.BounceThrow:
                    HoldStill();
                    if (!phaseImpact && t >= s.bounceThrowTime * s.bounceReleaseFraction)
                    {
                        HoldImpactPose(s.bounceThrowClip, s.bounceReleaseFraction, s.bounceThrowTime); UpdateGripAnchor();
                        ReleaseBounce(); return;
                    }
                    if (t >= s.bounceThrowTime && phaseImpact) EnterPhase(CubeThrowPhase.Settle);
                    break;
                case CubeThrowPhase.Settle: HoldStill(); break;
            }
        }
        protected override bool CanFinishExecute(float elapsed) => IsRunning && FiredCount == PlannedThrowCount
            && CurrentPhase == CubeThrowPhase.Settle && Time.time - phaseStartedAt >= Settings.postThrowTime;

        private AssemblyVisual SpawnGroundAssembly(int index)
        {
            var clip = index == 0 ? Settings.assemblyLeftClip : Settings.assemblyRightClip;
            clip.SampleAnimation(animator.gameObject, Settings.assemblyImpactFraction * clip.length);
            var tip = index == 0 ? leftGripPoint : rightGripPoint;
            var visual = SpawnAssembly(new Vector3(tip.position.x, floorY, tip.position.z));
            var renderers = visual.root.GetComponentsInChildren<Renderer>(true);
            var bounds = new Bounds(visual.root.transform.position, Vector3.zero);
            bool hasBounds = false;
            foreach (var renderer in renderers)
            {
                if (!hasBounds) { bounds = renderer.bounds; hasBounds = true; }
                else bounds.Encapsulate(renderer.bounds);
            }
            // 외형의 실제 바닥이 지면에 닿도록 한다. 임의의 큐브 크기에 기대지 않는다.
            float raise = floorY - bounds.min.y;
            visual.root.transform.position += Vector3.up * raise;
            visual.origin = visual.root.transform.position;
            assemblyBodyTargets[index] = new Vector2(WallTarget.x,
                controller.transform.position.y + bounds.max.y + raise - tip.position.y);
            return visual;
        }
        private void UpdateWaveStrikeMovement(float elapsed)
            => MoveTo(GetWaveMoveTarget(elapsed), Settings.waveSlamSpeed);

        private Vector2 GetWaveMoveTarget(float elapsed)
        {
            var s = Settings;
            float p = Mathf.Clamp01(elapsed / Mathf.Max(.01f, s.waveStrikeTime));
            // 물리 시각이 타격이 확정된 렌더 시각보다 조금 빨라도 타격 이전으로 되돌아가지 않는다.
            p = phaseImpact ? Mathf.Max(p, s.waveStrikeImpactFraction) : Mathf.Min(p, s.waveStrikeImpactFraction);
            float raised = floorY + s.waveBraceHeight + s.waveStrikeLiftHeight;
            // 팔·피스톤과 같은 시간 곡선으로 하강하고, 더 짧은 반동 구간에 원래 높이로 돌아간다.
            float height = raised - s.waveStrikeLiftHeight * WaveStroke(p);
            return new Vector2(arenaCenterX, height);
        }
        private float WaveStroke(float progress)
        {
            float impact = Settings.waveStrikeImpactFraction;
            float duration = Mathf.Max(.01f, Settings.waveStrikeTime);
            float attackStart = Mathf.Max(0f, impact - Settings.waveDownstrokeTime / duration);
            float reboundEnd = Mathf.Min(1f, impact + Settings.waveReturnTime / duration);
            if (progress < attackStart || progress >= reboundEnd) return 0f;
            return progress <= impact ? Ease(Mathf.InverseLerp(attackStart, impact, progress))
                : 1f - Ease(Mathf.InverseLerp(impact, reboundEnd, progress));
        }
        private static float Ease(float value) => Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(value));

        private AssemblyVisual SpawnAssembly(Vector3 point)
        {
            var root = Instantiate(Settings.heldCubePrefab, point, Quaternion.identity);
            var result = new AssemblyVisual { root = root, scale = root.transform.localScale, left = root.transform.Find("ShellLeft"),
                right = root.transform.Find("ShellRight"), renderers = CacheRenderers(root) };
            if (result.left != null) { result.leftRest = result.left.localPosition; result.leftRotation = result.left.localRotation; }
            if (result.right != null) { result.rightRest = result.right.localPosition; result.rightRotation = result.right.localRotation; }
            return result;
        }
        private void StampEquipment(int index)
        {
            HoldImpactPose(index == 0 ? Settings.assemblyLeftClip : Settings.assemblyRightClip,
                Settings.assemblyImpactFraction, Settings.assemblyStrikeTime);
            Transform point = index == 0 ? leftEquipmentPoint : rightEquipmentPoint;
            var root = Instantiate(index == 0 ? Settings.leftEquipmentPrefab : Settings.rightEquipmentPrefab, point, false);
            var visual = new EquipmentVisual { root = root, mechanism = root.transform.Find("Mechanism"), renderers = CacheRenderers(root) };
            if (visual.mechanism != null) visual.mechanismRest = visual.mechanism.localPosition;
            visual.sleeve = root.transform.Find("Deployment/ExtensionSleeve");
            if (visual.sleeve != null)
            {
                visual.sleeveRestScale = visual.sleeve.localScale;
                visual.sleeveRestPosition = visual.sleeve.localPosition;
                var stem = visual.sleeve.Find("TelescopicStem")?.GetComponent<SpriteRenderer>();
                if (stem != null && stem.sprite != null)
                    visual.sleeveRestLength = Mathf.Max(.001f, Mathf.Abs(stem.sprite.bounds.size.y * stem.transform.localScale.y));
            }
            equipment[index] = visual; phaseImpact = true; AssemblyImpactCount++;
            var impact = Settings.assemblyImpactFeedback;
            if (impact != null) PlayFeedback(impact, (index == 0 ? leftGripPoint : rightGripPoint).position);
        }
        private void BeginEquippedAction()
        {
            CurrentStrikeSide = 0;
            if (Settings.cubeKind == ConstructorCubeKind.Bomb)
            {
                chaseStartedAt = lastChaseAt = Time.time; lastChaseFrame = -1;
                bombChaseVelocity = Vector2.zero;
                ChaseDirection = playerTarget != null ? ((Vector2)playerTarget.position - (Vector2)controller.transform.position).normalized : Vector2.down;
                EnterPhase(CubeThrowPhase.BombChase);
            }
            else { ActionTarget = new Vector2(arenaCenterX, floorY + Settings.waveHeightAboveFloor); EnterPhase(CubeThrowPhase.WaveReposition); }
        }
        private void UpdateBombChase()
        {
            if (playerTarget == null || !playerTarget.gameObject.activeInHierarchy) { StopAbility(); return; }
            var s = Settings; BombChaseElapsed = Time.time - chaseStartedAt;
            ActionTarget = new Vector2(Mathf.Clamp(playerTarget.position.x, leftWallX + s.wallClearance, rightWallX - s.wallClearance),
                Mathf.Max(floorY + s.bombApproachHeightAboveFloor, playerTarget.position.y));
            Vector2 toPlayer = ActionTarget - (Vector2)controller.transform.position;
            // 근접하거나 지나쳐도 돌입을 중단하지 않고 정해진 추적 시간까지 계속 유도한다.
            if (BombChaseElapsed >= s.bombChaseDuration)
            { BeginBombCharge(); return; }
            // 같은 렌더 프레임의 재호출은 가속과 조향을 중복 적분하지 않는다.
            if (lastChaseFrame != Time.frameCount)
            {
                float step = Mathf.Max(0f, Time.time - lastChaseAt); lastChaseAt = Time.time; lastChaseFrame = Time.frameCount;
                if (toPlayer.sqrMagnitude > .0001f)
                {
                    // 정확히 반대 방향에서도 3D 회전축으로 빠지지 않도록 XY 평면의 각도만 돌린다.
                    float currentAngle = Mathf.Atan2(ChaseDirection.y, ChaseDirection.x) * Mathf.Rad2Deg;
                    float targetAngle = Mathf.Atan2(toPlayer.y, toPlayer.x) * Mathf.Rad2Deg;
                    float angle = Mathf.MoveTowardsAngle(currentAngle, targetAngle, s.bombChaseTurnRate * step) * Mathf.Deg2Rad;
                    ChaseDirection = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                }
                // 방향뿐 아니라 속도 벡터를 가속한다. 표적이 뒤로 바뀌어도 기존 운동량을 한 프레임에 뒤집지 않는다.
                bombChaseVelocity = Vector2.MoveTowards(bombChaseVelocity, ChaseDirection * s.bombChaseMaxSpeed,
                    s.bombChaseAcceleration * step);
                BombChaseSpeed = bombChaseVelocity.magnitude;
            }
            Vector2 velocity = bombChaseVelocity;
            if (velocity.y < 0f)
            {
                float clearance = Mathf.Max(0f, controller.transform.position.y - floorY - s.bombApproachHeightAboveFloor);
                velocity.y = Mathf.Max(velocity.y, -clearance / Time.fixedDeltaTime);
            }
            controller.Velocity = velocity;
        }
        private void BeginBombCharge()
        {
            HoldStill();
            if (FiredCount != 0 || !IsCargoMounted) { StopAbility(); return; }
            BombWarningCenter = ThrowTarget = MountPosition;
            activeBomb = WorldEntityPool.Instance.Spawn(Settings.projectilePrefab, BombWarningCenter, Quaternion.identity);
            activeBombLease = activeBomb.LeaseId;
            activeBomb.ConfigureThrow(ConstructorCubeKind.Bomb, Vector2.zero, Settings.projectileGravity, Settings.projectileLifeTime,
                Settings.explosionRadius, Settings.explosionDamageInfo, Settings.groundWavePrefab);
            activeBomb.Initialize(controller, Settings.damageInfo, 0f, 0, Settings.collideLayer);
            if (!activeBomb.BeginStationaryBombWarning(true)) { StopAbility(); return; }
            FiredCount++; EnterPhase(CubeThrowPhase.BombCharge); PlayFeedback(Settings.bombChargeFeedback, BombWarningCenter);
        }
        private void ResolveWaveStrike()
        {
            HoldImpactPose(CurrentStrikeSide < 0 ? Settings.waveLeftSlamClip : Settings.waveRightSlamClip,
                Settings.waveStrikeImpactFraction, Settings.waveStrikeTime);
            UpdateVisuals();
            var striker = CurrentStrikeSide < 0 ? equipment[0] : equipment[1];
            var impactPoint = striker?.root != null ? striker.root.transform.Find("Mechanism/ImpactPoint") : null;
            if (impactPoint == null)
            {
                ConfigurationFailure((CurrentStrikeSide < 0 ? "왼쪽" : "오른쪽")
                    + " 해머 타격점(장착 장비의 Mechanism/ImpactPoint)");
                return;
            }
            RaycastMotor2D.InvalidateAttackQuerySync(); RaycastMotor2D.PrepareAttackQueries();
            var hit = controller.Motor.RaycastEnvironment((Vector2)impactPoint.position + Vector2.up * .1f, Vector2.down, Settings.arenaSearchDistance);
            if (hit.collider == null) { StopAbility(); return; }
            GroundImpactPoint = ThrowTarget = hit.point; phaseImpact = true; FiredCount++; GroundImpactCount++;
            var wave = WorldEntityPool.Instance.Spawn(Settings.groundWavePrefab, hit.point, Quaternion.identity);
            wave.InitializeWave(controller, Settings.explosionDamageInfo, Settings.collideLayer);
            PlayFeedback(Settings.groundImpactFeedback, hit.point);
        }
        private void ReleaseBounce()
        {
            if (cargo == null || FiredCount != 0) { StopAbility(); return; }
            LastLaunchVelocity = Vector2.zero;
            ThrowTarget = new Vector2(WallSide < 0 ? leftWallX : rightWallX, floorY);
            LastReleasePosition = cargo.transform.position;
            var shot = WorldEntityPool.Instance.Spawn(Settings.projectilePrefab, LastReleasePosition, Quaternion.identity);
            shot.ConfigureThrow(ConstructorCubeKind.Bounce, LastLaunchVelocity, Settings.projectileGravity, Settings.projectileLifeTime,
                0f, Settings.explosionDamageInfo, Settings.groundWavePrefab);
            shot.ConfigureBounceDeployment(WallSide);
            shot.Initialize(controller, Settings.damageInfo, LastLaunchVelocity.magnitude, 0, Settings.collideLayer);
            phaseImpact = true; FiredCount++; DestroyCargo(); PlayFeedback(Settings.throwFeedback, gripPoint.position);
        }

        protected override void OnRecoverStart()
        {
            DestroyEquipment(); DestroyAssembly(0); DestroyAssembly(1);
            recoveryTarget = new Vector2(controller.transform.position.x, WallTarget.y); recoverArrivedAt = -1f;
            EnterPhase(CubeThrowPhase.Recover); MoveTo(recoveryTarget, Settings.repositionSpeed);
        }
        protected override void OnRecover(float elapsed)
        {
            if (Time.time - phaseStartedAt >= Settings.travelTimeout + data.recoverTime) { StopAbility(); return; }
            if (MoveTo(recoveryTarget, Settings.repositionSpeed) && recoverArrivedAt < 0f) recoverArrivedAt = Time.time;
        }
        protected override bool CanFinishRecover(float elapsed) => IsRunning && recoverArrivedAt >= 0f
            && Time.time - recoverArrivedAt >= data.recoverTime;

        private void LateUpdate()
        {
            if (!IsRunning || animator == null) return;
            SelectPose();
            if (impactFrame == Time.frameCount) { presentationClip = impactClip; CurrentClipProgress = impactProgress; }
            SampleClip(presentationClip, CurrentClipProgress, impactFrame != Time.frameCount); UpdateGripAnchor();
            UpdateVisuals();
        }
        private void SelectPose()
        {
            var s = Settings; float t = Time.time - phaseStartedAt;
            presentationClip = data.animationSet.execute; CurrentClipProgress = 0f;
            if (State == AbilityState.Windup) { presentationClip = data.animationSet.windup; CurrentClipProgress = CurrentPhase == CubeThrowPhase.Deploy ? Mathf.Clamp01(t / Mathf.Max(.01f, data.windupTime)) : 0f; return; }
            if (State == AbilityState.Recover) { presentationClip = data.animationSet.recover; CurrentClipProgress = Mathf.Clamp01(t / Mathf.Max(.01f, data.recoverTime)); return; }
            switch (CurrentPhase)
            {
                case CubeThrowPhase.AssemblyLeft: presentationClip = s.assemblyLeftClip; CurrentClipProgress = Mathf.Clamp01(t / s.assemblyStrikeTime); if (!phaseImpact) CurrentClipProgress = Mathf.Min(CurrentClipProgress, s.assemblyImpactFraction); break;
                case CubeThrowPhase.AssemblyRight: presentationClip = s.assemblyRightClip; CurrentClipProgress = Mathf.Clamp01(t / s.assemblyStrikeTime); if (!phaseImpact) CurrentClipProgress = Mathf.Min(CurrentClipProgress, s.assemblyImpactFraction); break;
                case CubeThrowPhase.AssemblyLower: presentationClip = s.assemblyLeftClip; CurrentClipProgress = 0f; break;
                case CubeThrowPhase.Mount: presentationClip = s.assemblyRiseClip; CurrentClipProgress = Mathf.Clamp01(t / s.assemblyRiseTime); break;
                case CubeThrowPhase.BombChase:
                case CubeThrowPhase.BombCharge: presentationClip = ChaseClip; CurrentClipProgress = 0f; break;
                case CubeThrowPhase.WaveReposition: presentationClip = s.waveBraceClip; CurrentClipProgress = 0f; break;
                case CubeThrowPhase.WaveBrace: presentationClip = s.waveBraceClip; CurrentClipProgress = Mathf.Clamp01(t / s.waveBraceTime); break;
                case CubeThrowPhase.WaveTelegraph: presentationClip = s.waveBraceClip; CurrentClipProgress = 1f; break;
                case CubeThrowPhase.WaveStrike: presentationClip = CurrentStrikeSide < 0 ? s.waveLeftSlamClip : s.waveRightSlamClip; CurrentClipProgress = Mathf.Clamp01(t / s.waveStrikeTime); if (!phaseImpact) CurrentClipProgress = Mathf.Min(CurrentClipProgress, s.waveStrikeImpactFraction); break;
                case CubeThrowPhase.CargoSpawn:
                case CubeThrowPhase.Unpack:
                    presentationClip = data.animationSet.windup;
                    CurrentClipProgress = 1f;
                    break;
                case CubeThrowPhase.BounceWindup: presentationClip = PickupClip; CurrentClipProgress = s.gripPoseFraction * .4f * Mathf.Clamp01(t / s.bounceOpenTime); break;
                case CubeThrowPhase.Grip: presentationClip = PickupClip; CurrentClipProgress = Mathf.Lerp(s.gripPoseFraction * .4f, s.gripPoseFraction, Mathf.Clamp01(t / s.gripTime)); break;
                case CubeThrowPhase.Lift: presentationClip = PickupClip; CurrentClipProgress = Mathf.Lerp(s.gripPoseFraction, s.carryPoseFraction, Mathf.Clamp01(t / s.liftTime)); break;
                case CubeThrowPhase.BounceThrow: presentationClip = s.bounceThrowClip; CurrentClipProgress = Mathf.Clamp01(t / s.bounceThrowTime); break;
                case CubeThrowPhase.Settle:
                    if (s.cubeKind == ConstructorCubeKind.Shockwave) { presentationClip = s.waveBraceClip; CurrentClipProgress = 1f; }
                    else if (s.cubeKind == ConstructorCubeKind.Bounce) { presentationClip = s.bounceThrowClip; CurrentClipProgress = 1f; }
                    else { presentationClip = data.animationSet.windup; CurrentClipProgress = 1f; }
                    break;
            }
        }
        private void HoldImpactPose(AnimationClip clip, float progress, float duration)
        {
            impactClip = clip; impactProgress = progress; impactFrame = Time.frameCount;
            phaseStartedAt = Time.time - duration * progress;
            CurrentClipProgress = progress; SampleClip(clip, progress, false);
        }
        private void SampleClip(AnimationClip clip, float progress, bool blend)
        {
            if (clip == null || animator == null) return;
            clip.SampleAnimation(animator.gameObject, Mathf.Clamp01(progress) * clip.length);
            if (!blend || phaseStartRotations == null) return;
            float weight = Settings.poseBlendTime <= 0f ? 1f : Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((Time.time - phaseStartedAt) / Settings.poseBlendTime));
            for (int i = 0; i < animatedParts.Length; i++) if (animatedParts[i] != null)
                animatedParts[i].localRotation = Quaternion.Slerp(phaseStartRotations[i], animatedParts[i].localRotation, weight);
        }
        private void UpdateGripAnchor()
        {
            if (gripPoint == null || leftGripPoint == null || rightGripPoint == null) return;
            Vector3 axis = rightGripPoint.position - leftGripPoint.position; if (axis.x < 0f) axis = -axis;
            gripPoint.SetPositionAndRotation(GripCenter, Quaternion.Euler(0f, 0f, Mathf.Atan2(axis.y, axis.x) * Mathf.Rad2Deg));
        }
        private void UpdateVisuals()
        {
            float t = Time.time - phaseStartedAt;
            float spawn = CurrentPhase == CubeThrowPhase.CargoSpawn ? Mathf.Clamp01(t / Mathf.Max(.01f, Settings.cargoSpawnTime)) : 1f;
            if (cargo != null)
            {
                cargo.transform.localScale = cargoFullScale * SpawnScale(spawn); SetAppearance(cargoSpawnRenderers, 1f + (1f - spawn) * 2f, Mathf.Clamp01(spawn * 6f));
                if (CurrentPhase == CubeThrowPhase.CargoSpawn || CurrentPhase == CubeThrowPhase.BounceWindup || CurrentPhase == CubeThrowPhase.Grip) cargo.transform.position = bounceSpawnPosition;
            }
            for (int i = 0; i < assembly.Length; i++)
            {
                var a = assembly[i]; if (a?.root == null) continue;
                a.root.transform.localScale = a.scale * SpawnScale(spawn);
                float opening = CurrentPhase == CubeThrowPhase.CargoSpawn ? 0f : Mathf.Clamp01((CurrentPhase == CubeThrowPhase.Unpack ? t / Mathf.Max(.01f, Settings.unpackTime) : 1f));
                opening = Mathf.SmoothStep(0f, 1f, opening);
                if (a.left != null) { a.left.localPosition = a.leftRest + Vector3.left * .75f * opening; a.left.localRotation = a.leftRotation * Quaternion.Euler(0f, 0f, 35f * opening); }
                if (a.right != null) { a.right.localPosition = a.rightRest + Vector3.right * .75f * opening; a.right.localRotation = a.rightRotation * Quaternion.Euler(0f, 0f, -35f * opening); }
                float collapse = equipment[i] != null ? Mathf.Clamp01((t / Mathf.Max(.01f, Settings.assemblyStrikeTime) - Settings.assemblyImpactFraction) / Mathf.Max(.01f, 1f - Settings.assemblyImpactFraction)) : 0f;
                if (equipment[i]?.root != null) a.root.transform.position = Vector3.Lerp(a.origin, equipment[i].root.transform.position, Ease(collapse));
                a.root.transform.localScale *= Mathf.Lerp(1f, .15f, collapse);
                SetAppearance(a.renderers, 1f + opening, Mathf.Clamp01(spawn * 6f) * (1f - collapse));
            }
            float urgency = CurrentPhase == CubeThrowPhase.BombChase ? Mathf.Clamp01(BombChaseElapsed / Mathf.Max(.01f, Settings.bombChaseDuration)) : 1f;
            float pulse = 1.1f + Mathf.Pow(Mathf.Sin(Time.time * Mathf.Lerp(20f, 45f, urgency)), 2f) * Mathf.Lerp(.7f, 2f, urgency);
            for (int i = 0; i < equipment.Length; i++)
            {
                var e = equipment[i]; if (e?.root == null) continue;
                float stroke = 0f;
                if (CurrentPhase == CubeThrowPhase.WaveStrike && (CurrentStrikeSide < 0 ? i == 0 : i == 1))
                {
                    float p = Mathf.Clamp01(t / Mathf.Max(.01f, Settings.waveStrikeTime));
                    stroke = WaveStroke(p);
                }
                float extension = stroke * Settings.waveMechanismExtension;
                if (e.mechanism != null) e.mechanism.localPosition = e.mechanismRest + Vector3.down * extension;
                if (e.sleeve != null)
                {
                    // 상단 연결점을 고정하고 늘어난 길이의 절반만 중심을 내려 해머와 이어 준다.
                    e.sleeve.localPosition = e.sleeveRestPosition + Vector3.down * (extension * .5f);
                    e.sleeve.localScale = Vector3.Scale(e.sleeveRestScale, new Vector3(1f, 1f + extension / e.sleeveRestLength, 1f));
                }
                bool charge = CurrentPhase == CubeThrowPhase.BombChase || CurrentPhase == CubeThrowPhase.BombCharge || CurrentPhase == CubeThrowPhase.WaveTelegraph;
                SetAppearance(e.renderers, charge ? pulse : 1f, 1f);
            }
        }

        private bool MoveTo(Vector2 target, float speed)
        {
            if (UsesFixedMoveTarget) { fixedMoveTarget = target; fixedMoveSpeed = speed; hasFixedMoveTarget = true; }
            Vector2 delta = target - (Vector2)controller.transform.position;
            bool arrived = delta.magnitude <= .01f;
            controller.Velocity = arrived ? Vector2.zero : Vector2.ClampMagnitude(delta / Time.fixedDeltaTime, Mathf.Max(.1f, speed));
            return arrived;
        }
        private bool UsesFixedMoveTarget => CurrentPhase == CubeThrowPhase.WaveStrike
            || CurrentPhase == CubeThrowPhase.AssemblyLower || CurrentPhase == CubeThrowPhase.AssemblyLeft
            || CurrentPhase == CubeThrowPhase.AssemblyRight || CurrentPhase == CubeThrowPhase.Mount;
        private bool MoveAlongPhase(Vector2 target, float duration, float speed, float elapsed)
        {
            float p = Mathf.Clamp01(elapsed / Mathf.Max(.01f, duration));
            bool arrived = MoveTo(Vector2.Lerp(phaseMoveStart, target, Mathf.SmoothStep(0f, 1f, p)), speed);
            return p >= 1f && arrived;
        }
        private void HoldStill() { if (ownsMovement && controller != null) controller.Velocity = Vector2.zero; }
        private void EnterPhase(CubeThrowPhase phase)
        {
            hasFixedMoveTarget = false;
            if (animatedParts != null)
            {
                phaseStartRotations = new Quaternion[animatedParts.Length];
                for (int i = 0; i < animatedParts.Length; i++) if (animatedParts[i] != null) phaseStartRotations[i] = animatedParts[i].localRotation;
            }
            CurrentPhase = phase; phaseStartedAt = Time.time; phaseImpact = false;
        }
        private void PlayFeedback(FeedbackDataSO feedback, Vector3 position) { if (feedbackPlayer != null && feedback != null) feedbackPlayer.PlayExact(feedback, position, Quaternion.identity); }
        // Prefab 작성 도구는 false로 조회하고 실제 실행 전에는 런타임 의존성까지 확인한다.
        public string[] GetMissingConfigurationFields(bool includeRuntimeDependencies = true)
        {
            var missing = new List<string>();
            var settings = Settings;
            if (settings == null) missing.Add("큐브 패턴 데이터(data: ConstructorCubeThrowDataSO)");
            else
            {
                missing.AddRange(settings.GetMissingReferences());
                if (leftGripPoint == null) missing.Add("왼쪽 턱 끝 집기점(leftGripPoint)");
                if (rightGripPoint == null) missing.Add("오른쪽 턱 끝 집기점(rightGripPoint)");
                if (settings.cubeKind == ConstructorCubeKind.Bounce)
                {
                    if (gripPoint == null) missing.Add("큐브 운반 기준점(gripPoint)");
                }
                else if (settings.cubeKind == ConstructorCubeKind.Bomb || settings.cubeKind == ConstructorCubeKind.Shockwave)
                {
                    if (leftEquipmentPoint == null) missing.Add("왼쪽 장비 연결점(leftEquipmentPoint)");
                    if (rightEquipmentPoint == null) missing.Add("오른쪽 장비 연결점(rightEquipmentPoint)");
                }
            }
            if (animatedParts == null || animatedParts.Length == 0) missing.Add("모델 관절 목록(animatedParts)");
            else
                for (int i = 0; i < animatedParts.Length; i++)
                    if (animatedParts[i] == null) missing.Add("모델 관절(animatedParts[" + i + "])");
            if (includeRuntimeDependencies)
            {
                if (controller == null) missing.Add("초기화된 적 컨트롤러(controller)");
                else if (controller.Motor == null) missing.Add("기존 이동 모터(controller.Motor)");
                if (animator == null) missing.Add("초기화된 Animator(animator)");
                if (feedbackPlayer == null) missing.Add("초기화된 피드백 재생기(feedbackPlayer)");
                if (WorldEntityPool.Instance == null) missing.Add("월드 엔티티 풀(WorldEntityPool.Instance)");
                if (GameManager.Instance == null) missing.Add("게임 관리자(GameManager.Instance)");
                else if (GameManager.Instance.Player == null) missing.Add("플레이어(GameManager.Player)");
            }
            return missing.ToArray();
        }

        private void ConfigurationFailure(string additionalReason = null)
        {
            var missing = new List<string>(GetMissingConfigurationFields());
            if (!string.IsNullOrWhiteSpace(additionalReason)) missing.Add(additionalReason);
            string details = missing.Count > 0 ? string.Join(", ", missing) : "실행 중 필수 참조를 사용할 수 없습니다.";
            string key = data != null ? data.abilityKey : "없음";
            string dataName = data != null ? data.name : "없음";
            string kind = Settings != null ? Settings.cubeKind.ToString() : "알 수 없음";
            string message = "Constructor 큐브 패턴 중단 [종류=" + kind + ", 키=" + key + ", 데이터=" + dataName + "]: " + details;
            if (lastConfigurationFailure != message)
            {
                Debug.LogWarning(message, this);
                lastConfigurationFailure = message;
            }
            StopAbility();
        }
        private bool OwnsActiveBomb() => activeBomb != null && activeBomb.gameObject.activeSelf && activeBomb.LeaseId == activeBombLease && !activeBomb.HasResolved;
        private void DestroyCargo() { if (cargo != null) Destroy(cargo); cargo = null; cargoSpawnRenderers = null; }
        private void DestroyAssembly(int index) { if (assembly[index]?.root != null) Destroy(assembly[index].root); assembly[index] = null; }
        private void DestroyEquipment() { for (int i = 0; i < equipment.Length; i++) { if (equipment[i]?.root != null) Destroy(equipment[i].root); equipment[i] = null; } }
        protected override void OnStop() => Cleanup();
        private void OnDisable() { if (IsRunning && animator != null) StopAbility(); else Cleanup(); }
        private void Cleanup()
        {
            hasFixedMoveTarget = false;
            if (OwnsActiveBomb()) activeBomb.ForceReturnToPool(); activeBomb = null; activeBombLease = 0;
            DestroyCargo(); DestroyAssembly(0); DestroyAssembly(1); DestroyEquipment();
            CurrentPhase = CubeThrowPhase.Inactive; playerTarget = null;
            if (!ownsMovement) return; ownsMovement = false; RestoreRestPose();
            if (controller == null) return;
            controller.Velocity = Vector2.zero; controller.GravityOn = previousGravity; controller.IgnoreExternalForces = previousIgnoreForces;
            controller.LockFacing = previousLockFacing; controller.FacingUpdate(previousFacing);
        }
        private void CacheRestPose()
        {
            if (restPoseCached || animatedParts == null) return;
            restPositions = new Vector3[animatedParts.Length]; restRotations = new Quaternion[animatedParts.Length];
            for (int i = 0; i < animatedParts.Length; i++) if (animatedParts[i] != null) { restPositions[i] = animatedParts[i].localPosition; restRotations[i] = animatedParts[i].localRotation; }
            restPoseCached = true;
        }
        private void RestoreRestPose()
        {
            if (!restPoseCached || animatedParts == null) return;
            for (int i = 0; i < animatedParts.Length; i++) if (animatedParts[i] != null) { animatedParts[i].localPosition = restPositions[i]; animatedParts[i].localRotation = restRotations[i]; }
        }
        private static float SpawnScale(float p) => p < 1f / 3f ? Mathf.Lerp(.001f, 1.2f, Mathf.SmoothStep(0f, 1f, p * 3f)) : Mathf.Lerp(1.2f, 1f, Mathf.SmoothStep(0f, 1f, (p - 1f / 3f) * 1.5f));
        private static SpawnRenderer[] CacheRenderers(GameObject root)
        {
            var renderers = root.GetComponentsInChildren<Renderer>(true); var result = new SpawnRenderer[renderers.Length];
            for (int i = 0; i < renderers.Length; i++)
            {
                var m = renderers[i].sharedMaterial; var r = new SpawnRenderer { renderer = renderers[i], block = new MaterialPropertyBlock() };
                if (m != null)
                {
                    r.hasColor = m.HasProperty("_Color"); r.hasBaseColor = m.HasProperty("_BaseColor"); r.hasEmission = m.HasProperty("_EmissionColor");
                    r.hasRendererColor = m.HasProperty("_RendererColor"); r.hasIntensity = m.HasProperty("_Intensity");
                    if (r.hasColor) r.color = m.GetColor("_Color"); if (r.hasBaseColor) r.baseColor = m.GetColor("_BaseColor");
                    if (r.hasEmission) r.emission = m.GetColor("_EmissionColor"); if (r.hasRendererColor) r.rendererColor = m.GetColor("_RendererColor");
                    if (r.hasIntensity) r.intensity = m.GetFloat("_Intensity");
                }
                result[i] = r;
            }
            return result;
        }
        private static void SetAppearance(SpawnRenderer[] renderers, float brightness, float alpha)
        {
            if (renderers == null) return;
            foreach (var r in renderers)
            {
                if (r.renderer == null) continue; r.renderer.GetPropertyBlock(r.block);
                if (r.hasColor) r.block.SetColor("_Color", Tint(r.color, brightness, alpha));
                if (r.hasBaseColor) r.block.SetColor("_BaseColor", Tint(r.baseColor, brightness, alpha));
                if (r.hasEmission) r.block.SetColor("_EmissionColor", Tint(r.emission, brightness, alpha));
                if (r.hasRendererColor) r.block.SetColor("_RendererColor", Tint(r.rendererColor, 1f, alpha));
                if (r.hasIntensity) r.block.SetFloat("_Intensity", r.intensity * brightness); r.renderer.SetPropertyBlock(r.block);
            }
        }
        private static Color Tint(Color value, float brightness, float alpha) => new Color(value.r * brightness, value.g * brightness, value.b * brightness, value.a * alpha);
    }
}
