using System.Collections.Generic;
using UnityEngine;

namespace GameProject.AI.Abilities.Constructor
{
    public class Constructor_SpinChaseAbility : AiAbility
    {
        public enum ApproachPhase { MoveToWall, MatchHeight, OpenClaws }

        [SerializeField] private SpriteRenderer[] clawSprites;
        [SerializeField] private SpriteRenderer[] trailSprites;
        [SerializeField] private TrailRenderer[] clawEdgeTrails;
        public ApproachPhase CurrentApproachPhase { get; private set; }
        public Vector3 StartPosition => startPosition;
        public float MaxChaseSpeed { get; private set; }
        public bool DamageActive => IsRunning && (State == AbilityState.Execute
            || (State == AbilityState.Windup && CurrentApproachPhase == ApproachPhase.OpenClaws
                && Time.time - openStartedAt >= spinData.clawOpenTime));

        private Constructor_SpinChaseData spinData;
        private PlayerController player;
        private Vector3 startPosition;
        private Vector3 wallPosition;
        private Vector3 heightPosition;
        private float openStartedAt;
        private float phaseStartedAt;
        private float nextDamageAt;
        private Vector2 chaseVelocity;
        private bool previousGravity;
        private bool previousIgnoreForces;
        private bool ownsMovement;
        private AnimationClip poseClip;
        private float poseTime;
        private readonly Collider2D[] overlapResults = new Collider2D[16];
        private readonly Dictionary<Transform, Quaternion> restRotations = new();

        private void Awake()
        {
            spinData = (Constructor_SpinChaseData)data;
            foreach (Transform joint in GetComponent<EnemyController>().animator.GetComponentsInChildren<Transform>(true))
                if (joint.name.EndsWith("Bone")) restRotations[joint] = joint.localRotation;
        }

        protected override void OnWindupStart()
        {
            player = GameManager.Instance.Player;
            startPosition = transform.position;
            previousGravity = controller.GravityOn;
            previousIgnoreForces = controller.IgnoreExternalForces;
            ownsMovement = true;
            controller.GravityOn = false;
            controller.IgnoreExternalForces = true;
            controller.Velocity = Vector2.zero;
            controller.CanTouchDamageActive(false);
            nextDamageAt = Time.time;
            UpdateSpinTrails(false);

            var rayOrigin = (Vector2)player.transform.position + Vector2.up * 0.1f;
            var left = controller.Motor.RaycastEnvironment(rayOrigin, Vector2.left, spinData.wallSearchDistance);
            var right = controller.Motor.RaycastEnvironment(rayOrigin, Vector2.right, spinData.wallSearchDistance);
            float leftX = left.collider != null ? left.point.x : startPosition.x - spinData.wallSearchDistance * 0.5f;
            float rightX = right.collider != null ? right.point.x : startPosition.x + spinData.wallSearchDistance * 0.5f;
            float inset = Mathf.Min(spinData.wallClearance, (rightX - leftX) * 0.4f);
            float wallX = player.transform.position.x >= (leftX + rightX) * 0.5f
                ? leftX + inset : rightX - inset;
            wallPosition = new Vector3(wallX, startPosition.y, startPosition.z);
            heightPosition = new Vector3(wallX, SafePlayerHeight(), startPosition.z);
            CurrentApproachPhase = ApproachPhase.MoveToWall;
            phaseStartedAt = Time.time;
            poseClip = data.animationSet.windup;
            poseTime = 0f;
        }

        private float SafePlayerHeight()
        {
            return controller.Motor.ClampHeightAboveFloor(player.transform.position, spinData.wallSearchDistance);
        }

        protected override void OnWindup(float t)
        {
            if (Time.time - phaseStartedAt > spinData.travelTimeout && CurrentApproachPhase != ApproachPhase.OpenClaws)
            {
                StopAbility();
                return;
            }
            switch (CurrentApproachPhase)
            {
                case ApproachPhase.MoveToWall:
                    if (MoveTo(wallPosition)) CurrentApproachPhase = ApproachPhase.MatchHeight;
                    break;
                case ApproachPhase.MatchHeight:
                    if (MoveTo(heightPosition))
                    {
                        CurrentApproachPhase = ApproachPhase.OpenClaws;
                        openStartedAt = Time.time;
                    }
                    break;
                case ApproachPhase.OpenClaws:
                    controller.Velocity = Vector2.zero;
                    poseTime = Mathf.Clamp01((Time.time - openStartedAt) / Mathf.Max(0.01f, data.windupTime)) * poseClip.length;
                    break;
            }
        }

        protected override bool CanFinishWindup(float elapsed) => IsRunning
            && CurrentApproachPhase == ApproachPhase.OpenClaws && Time.time - openStartedAt >= data.windupTime;

        protected override void OnExecuteStart()
        {
            var move = player.GetComponent<MoveAbility>();
            MaxChaseSpeed = (move != null ? move.MoveSpeed : 0f) * spinData.playerSpeedRatio;
            chaseVelocity = Vector2.zero;
            nextDamageAt = Time.time;
            poseClip = data.animationSet.execute;
            poseTime = 0f;
        }

        protected override void OnExecute(float t)
        {
            Vector2 target = new Vector2(player.transform.position.x, SafePlayerHeight());
            Vector2 delta = target - (Vector2)transform.position;
            Vector2 desired = delta.sqrMagnitude > 0.01f ? delta.normalized * MaxChaseSpeed : Vector2.zero;
            float acceleration = MaxChaseSpeed / Mathf.Max(0.01f, spinData.accelerationTime);
            chaseVelocity = Vector2.MoveTowards(chaseVelocity, desired, acceleration * Time.deltaTime);
            controller.Velocity = chaseVelocity;
            poseTime = Mathf.Clamp01(t / Mathf.Max(0.01f, data.executeTime)) * poseClip.length;
        }

        protected override void OnRecoverStart()
        {
            controller.Velocity = Vector2.zero;
            UpdateSpinTrails(false);
            phaseStartedAt = Time.time;
            poseClip = data.animationSet.recover;
            poseTime = 0f;
        }

        protected override void OnRecover(float t)
        {
            poseTime = Mathf.Clamp01(t / Mathf.Max(0.01f, data.recoverTime)) * poseClip.length;
            MoveTo(startPosition);
        }

        protected override bool CanFinishRecover(float elapsed) => elapsed >= data.recoverTime
            && (Vector3.Distance(transform.position, startPosition) <= spinData.arrivalTolerance
                || elapsed >= spinData.travelTimeout);

        private bool MoveTo(Vector3 target)
        {
            Vector2 delta = target - transform.position;
            bool arrived = delta.magnitude <= spinData.arrivalTolerance;
            controller.Velocity = arrived ? Vector2.zero
                : Vector2.ClampMagnitude(delta / Time.fixedDeltaTime, spinData.repositionSpeed);
            return arrived;
        }

        private void LateUpdate()
        {
            if (!IsRunning || animator == null || poseClip == null) return;
            // Travel must not consume the opening animation; sample after Animator evaluation.
            poseClip.SampleAnimation(animator.gameObject, poseTime);
            UpdateSpinTrails(DamageActive);
            if (DamageActive && Time.time >= nextDamageAt) CheckClawDamage();
        }

        private void UpdateSpinTrails(bool active)
        {
            if (clawEdgeTrails == null) return;
            for (int i = 0; i < clawEdgeTrails.Length; i++)
            {
                var trail = clawEdgeTrails[i];
                if (trail == null) continue;
                var sprite = trailSprites != null && i < trailSprites.Length ? trailSprites[i] : null;
                bool visible = active && sprite != null && sprite.enabled;
                if (visible)
                {
                    // Let TrailRenderer retain actual movement history during rotation.
                    if (!trail.emitting) trail.Clear();
                    trail.enabled = true;
                    trail.emitting = true;
                }
                else
                {
                    trail.emitting = false;
                    trail.Clear();
                    trail.enabled = false;
                }
            }
        }

        private void CheckClawDamage()
        {
            foreach (var sprite in clawSprites)
            {
                if (sprite == null || !sprite.enabled) continue;
                var bounds = sprite.localBounds;
                Vector2 center = sprite.transform.TransformPoint(bounds.center);
                Vector3 scale = sprite.transform.lossyScale;
                Vector2 size = new Vector2(bounds.size.x * Mathf.Abs(scale.x), bounds.size.y * Mathf.Abs(scale.y));
                int count = controller.Motor.OverlapAttackBox(center, size, sprite.transform.eulerAngles.z,
                    overlapResults, spinData.playerMask);
                for (int i = 0; i < count; i++)
                {
                    var health = overlapResults[i].GetComponentInParent<PlayerHealth>();
                    if (health == null) continue;
                    health.TakeDamage(spinData.damageInfo, center);
                    nextDamageAt = Time.time + spinData.damageInterval;
                    return;
                }
            }
        }

        protected override void OnStop() => Cleanup();
        private void OnDisable() => Cleanup();

        private void Cleanup()
        {
            UpdateSpinTrails(false);
            if (!ownsMovement || controller == null) return;
            ownsMovement = false;
            controller.Velocity = Vector2.zero;
            controller.GravityOn = previousGravity;
            controller.IgnoreExternalForces = previousIgnoreForces;
            controller.CanTouchDamageActive(!brain.IsPaused);
            poseClip = null;
            foreach (var joint in restRotations)
                if (joint.Key != null) joint.Key.localRotation = joint.Value;
        }
    }
}
