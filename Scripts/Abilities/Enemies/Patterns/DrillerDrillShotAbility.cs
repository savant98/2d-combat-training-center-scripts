using System.Collections.Generic;
using UnityEngine;

// Driller 드릴 발사.
// 준비: 플레이어 쪽을 보고 몸을 뒤로 90도 눕혀 아래를 향하던 드릴을 정면으로 겨눈다. 다 누우면 발사 경로에 예고선을 띄운다.
// 실행: 텔레스코프 피스톤 팔이 뻗으며 드릴을 쏘아 보낸다. 벽이 있으면 벽 앞에서 멈추고 불꽃을 낸다.
// 회수: 팔을 거두고 다시 일어선다.
// 판정은 드릴 머리가 이번 프레임에 지나간 구간 전체를 모터의 OverlapAttackBox로 검사하고, 한 번 쏠 때 Player마다 한 번만 맞힌다.
public sealed class DrillerDrillShotAbility : AiAbility
{
    [SerializeField] private DrillerRig rig;

    private DrillerDrillShotDataSO settings;
    private LaserPreviewLine preview;
    private readonly HashSet<PlayerHealth> struck = new();
    private readonly Collider2D[] hits = new Collider2D[8];
    private LayerMask playerLayer;
    private float extension;
    private float sweptFrom;
    private float retractFrom;
    private bool tiltLanded, standStarted, wallFeedbackPlayed;

    public float Extension => extension;
    public float Reach { get; private set; }
    public bool HitsWall { get; private set; }
    public Vector2 WallPoint { get; private set; }
    public int StruckCount => struck.Count;
    public bool PreviewVisible => preview != null;
    public override bool IsBodyAttackActive => false;

    private void Awake()
    {
        settings = (DrillerDrillShotDataSO)data;
        if (rig == null) rig = GetComponent<DrillerRig>();
        playerLayer = LayerMask.GetMask("Player");
    }

    private void OnDisable() => Cleanup();

    protected override void OnWindupStart()
    {
        if (rig == null)
        {
            StopAbility();
            return;
        }
        var target = GameManager.Instance != null ? GameManager.Instance.Player : null;
        if (target != null)
            controller.FacingUpdate(target.transform.position.x >= transform.position.x ? 1 : -1);
        controller.IgnoreExternalForces = true;
        rig.ResetMechanism();
        rig.Pivot = DrillerRig.PosePivot.Body;
        rig.GroundSnap = true;
        rig.IdleWobble = false;
        rig.SpinTarget = settings.aimSpin;
        // 누운 몸(뒤 0.36칸~앞 1.62칸)이 앞뒤 벽을 넘지 않게 드릴 높이와 몸통 높이에서 벽 여유를 잰다.
        rig.MeasureSideRoom(4f, -.36f, .3f);
        struck.Clear();
        extension = sweptFrom = 0f;
        Reach = 0f;
        HitsWall = false;
        tiltLanded = standStarted = wallFeedbackPlayed = false;
    }

    protected override void OnWindup(float t)
    {
        controller.Velocity.x = Mathf.MoveTowards(controller.Velocity.x, 0f, 40f * Time.deltaTime);
        rig.PoseAngle = TiltAngle(t);
        if (!tiltLanded && t >= settings.tiltTime)
        {
            tiltLanded = true;
            rig.ApplyPose();
            Play(settings.tiltFeedback, rig.ArmOriginWorld);
            // 수명은 실행 시작에서 직접 반환할 때까지 남도록 조금 길게 준다(먼저 풀로 돌아가 다른 패턴이 쓰는 것을 막는다).
            ShowPreview(Mathf.Max(.05f, data.windupTime - t) + .25f);
        }
        if (preview != null)
        {
            rig.ApplyPose();
            preview.SetOrigin(rig.ArmOriginWorld);
            preview.SetDirection(rig.ArmDirectionWorld);
            preview.UpdatePreviewVisual(Mathf.Max(0f, data.windupTime - t), Mathf.Max(.05f, data.windupTime - settings.tiltTime));
        }
    }

    // 무게중심을 넘긴 뒤 빨라지며 눕고(가속), 바닥에 닿아 살짝 튀었다 가라앉는다.
    private float TiltAngle(float t)
    {
        if (t < settings.tiltTime)
        {
            float k = t / settings.tiltTime;
            return 90f * k * k;
        }
        float b = (t - settings.tiltTime) / settings.tiltBounceTime;
        return b >= 1f ? 90f : 90f - settings.tiltBounceDegrees * Mathf.Sin(b * Mathf.PI);
    }

    protected override void OnExecuteStart()
    {
        ReturnPreview();
        rig.PoseAngle = 90f;
        rig.Extension = 0f;
        rig.ApplyPose();
        Vector2 origin = rig.ArmOriginWorld, direction = rig.ArmDirectionWorld;
        // 팔 길이의 기준은 패턴 SO 하나다(Enemy Tool에서 바꾼 값이 그대로 사거리·예고선·마디에 쓰인다).
        float limit = settings.maxExtension;
        Reach = limit;
        HitsWall = false;
        if (controller.Motor != null)
        {
            var wall = controller.Motor.RaycastEnvironment(origin, direction, limit + rig.DrillLength + settings.wallMargin);
            if (wall.collider != null)
            {
                Reach = Mathf.Clamp(wall.distance - rig.DrillLength - settings.wallMargin, 0f, limit);
                HitsWall = true;
                WallPoint = wall.point;
            }
        }
        rig.SpinTarget = settings.fireSpin;
        extension = sweptFrom = 0f;
        Play(settings.fireFeedback, origin);
    }

    protected override void OnExecute(float t)
    {
        controller.Velocity.x = 0f;
        float k = Mathf.Clamp01(t / settings.extendTime);
        extension = Reach * (1f - (1f - k) * (1f - k) * (1f - k));
        rig.Extension = extension;
        rig.ApplyPose();
        SweepDrill();
        if (k >= 1f && HitsWall && !wallFeedbackPlayed)
        {
            wallFeedbackPlayed = true;
            Play(settings.wallHitFeedback, WallPoint);
        }
    }

    // 지난 판정 이후 드릴 머리가 지나간 구간(밑동~끝)을 한 상자로 덮어, 빠르게 뻗어도 사이를 건너뛰지 않는다.
    private void SweepDrill()
    {
        if (controller.Motor == null || !controller.CanDealContactDamage) return;
        float damage = settings.drillDamageInfo.damage;
        if (damage <= 0f) return;
        Vector2 origin = rig.ArmOriginWorld, direction = rig.ArmDirectionWorld;
        float from = Mathf.Min(sweptFrom, extension), to = extension + rig.DrillLength;
        float length = Mathf.Max(.05f, to - from);
        Vector2 center = origin + direction * ((from + to) * .5f);
        float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
        RaycastMotor2D.InvalidateAttackQuerySync();
        int count = controller.Motor.OverlapAttackBox(center, new Vector2(length, settings.drillHitThickness), angle, hits, playerLayer);
        for (int i = 0; i < count; i++)
        {
            var health = hits[i] != null ? hits[i].GetComponentInParent<PlayerHealth>() : null;
            if (health != null && struck.Add(health)) controller.RequestTouchDamage(health, damage, false);
        }
        sweptFrom = extension;
    }

    protected override void OnRecoverStart()
    {
        retractFrom = extension;
        rig.SpinTarget = settings.retractSpin;
        Play(settings.retractFeedback, rig.ArmOriginWorld);
    }

    protected override void OnRecover(float t)
    {
        controller.Velocity.x = 0f;
        if (t < settings.retractTime)
        {
            float k = t / settings.retractTime;
            k = k * k * (3f - 2f * k);
            extension = retractFrom * (1f - k);
            rig.Extension = extension;
            rig.PoseAngle = 90f;
            return;
        }
        extension = 0f;
        rig.Extension = 0f;
        if (!standStarted)
        {
            standStarted = true;
            Play(settings.standFeedback, rig.ArmOriginWorld);
        }
        float s = Mathf.Clamp01((t - settings.retractTime) / settings.standUpTime);
        s = s * s * (3f - 2f * s);
        rig.PoseAngle = 90f * (1f - s);
    }

    protected override bool CanFinishRecover(float elapsed)
        => elapsed >= Mathf.Max(data.recoverTime, settings.retractTime + settings.standUpTime);

    protected override void OnStop()
    {
        bool paused = brain != null && brain.IsPaused;
        Cleanup();
        if (paused) controller.Velocity = Vector2.zero;
    }

    private void Cleanup()
    {
        ReturnPreview();
        extension = 0f;
        if (rig != null) rig.ResetMechanism();
        if (controller != null) controller.IgnoreExternalForces = false;
    }

    private void ShowPreview(float duration)
    {
        if (settings.previewLinePrefab == null || WorldEntityPool.Instance == null || preview != null) return;
        Vector2 origin = rig.ArmOriginWorld, direction = rig.ArmDirectionWorld;
        preview = WorldEntityPool.Instance.Spawn(settings.previewLinePrefab, origin, Quaternion.identity);
        if (preview == null) return;
        float length = settings.maxExtension + rig.DrillLength;
        preview.Show(origin, direction, length, settings.previewWidth, settings.previewBlockMask, settings.previewColor, duration);
    }

    private void ReturnPreview()
    {
        if (preview == null) return;
        // 수명이 끝나 이미 풀로 돌아간 예고선은 다시 반환하지 않는다.
        if (preview.isActiveAndEnabled) preview.ForceReturnToPool();
        preview = null;
    }

    private void Play(FeedbackDataSO feedback, Vector2 position)
    {
        if (feedback == null || feedbackPlayer == null) return;
        // 방향이 있는 효과는 오른쪽 기준으로 만들고, 왼쪽을 볼 때 뒤집어 재생한다.
        var rotation = controller.Facing >= 0 ? Quaternion.identity : Quaternion.Euler(0f, 180f, 0f);
        feedbackPlayer.PlayExact(feedback, position, rotation, controller);
    }
}
