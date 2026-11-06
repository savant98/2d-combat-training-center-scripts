using UnityEngine;

/// <summary>기존 모터의 속도 경로로 상승·가속 추적·수직 내려찍기를 반복한다.</summary>
[DefaultExecutionOrder(-50)]
public sealed class TrackingSlamAbility : AiAbility
{
    public enum FlightPhase { None, Rise, Tracking, Slam }

    [SerializeField] private Collider2D bodyCollider;
    [SerializeField] private Transform bodyVisual;
    [SerializeField] private Vector3 restingBodyScale = Vector3.one;

    public LaserPreviewLine ActivePreview => activePreview;

    public FlightPhase CurrentPhase { get; private set; }
    public int CompletedSlams { get; private set; }
    public int SpawnedWaves { get; private set; }
    public float TargetHeight { get; private set; }
    public float PhaseElapsed => Time.time - phaseStartedAt;
    public Vector2 LastImpactPoint { get; private set; }

    private TrackingSlamDataSO Settings => (TrackingSlamDataSO)data;
    private Transform player;
    private float phaseStartedAt;
    private float horizontalSpeed;
    private float riseStartY;
    private bool savedGravityOn;
    private bool savedIgnoreExternalForces;
    private bool ownsMovement;
    private LaserPreviewLine activePreview;

    protected override void OnWindupStart()
    {
        // Pool 재사용 때도 횟수·속도·이벤트 구독을 새로 시작한다.
        CompletedSlams = SpawnedWaves = 0;
        CurrentPhase = FlightPhase.None;
        horizontalSpeed = 0f;
        ReturnPreview();
        LastImpactPoint = Vector2.zero;
        player = GameManager.Instance != null && GameManager.Instance.Player != null
            ? GameManager.Instance.Player.transform : null;
        savedGravityOn = controller.GravityOn;
        savedIgnoreExternalForces = controller.IgnoreExternalForces;
        ownsMovement = true;
        controller.IgnoreExternalForces = true;
        controller.externalForce = Vector2.zero;
        controller.Velocity = Vector2.zero;
        controller.OnLanded -= HandleLanding;
        controller.OnLanded += HandleLanding;
        FacePlayer();
    }

    protected override void OnWindup(float elapsed)
    {
        controller.Velocity.x = 0f;
    }

    protected override void OnExecuteStart()
    {
        controller.GravityOn = false;
        BeginRise();
    }

    private void BeginRise()
    {
        RaycastMotor2D.InvalidateAttackQuerySync();
        RaycastMotor2D.PrepareAttackQueries();
        var floor = controller.Motor.RaycastEnvironment(bodyCollider.bounds.center,
            Vector2.down, Settings.floorSearchDistance);
        if (floor.collider == null)
        {
            StopAbility();
            return;
        }
        // 높이는 Admin처럼 바닥에서 루트까지의 거리다. 매 착지 지면을 기준으로 다시 잡는다.
        TargetHeight = Mathf.Max(transform.position.y, floor.point.y + Settings.heightAboveFloor);
        riseStartY = transform.position.y;
        horizontalSpeed = 0f;
        EnterPhase(FlightPhase.Rise);
        controller.Velocity = new Vector2(0f, Settings.riseSpeed);
        feedbackPlayer.Play(Settings.riseFeedback, new Vector2(bodyCollider.bounds.center.x, bodyCollider.bounds.min.y));
    }

    // CharacterController2D.FixedUpdate보다 먼저 속도를 준비해 렌더 프레임률에 의존하지 않는다.
    private void FixedUpdate()
    {
        if (State != AbilityState.Execute || brain.IsPaused) return;
        float dt = Time.fixedDeltaTime;
        switch (CurrentPhase)
        {
            case FlightPhase.Rise:
                float remaining = TargetHeight - transform.position.y;
                var ceiling = controller.Motor.RaycastEnvironment(bodyCollider.bounds.center,
                    Vector2.up, bodyCollider.bounds.extents.y + 0.03f);
                if (remaining <= 0.01f || ceiling.collider != null || PhaseElapsed >= Settings.travelTimeout)
                {
                    // 낮은 천장에서는 실제 도달 높이에서 추적한다. 천장을 뚫거나 상승에 갇히지 않는다.
                    TargetHeight = transform.position.y;
                    EnterPhase(FlightPhase.Tracking);
                    controller.Velocity = Vector2.zero;
                }
                else controller.Velocity = new Vector2(0f, Mathf.Min(Settings.riseSpeed, remaining / dt));
                break;
            case FlightPhase.Tracking:
                if (PhaseElapsed >= Settings.trackingDuration)
                {
                    horizontalSpeed = 0f;
                    EnterPhase(FlightPhase.Slam);
                    controller.Velocity = new Vector2(0f, -Settings.slamSpeed);
                    break;
                }
                float offset = player != null ? player.position.x - transform.position.x : 0f;
                // 남은 거리 안에서 제동하는 가속 추적. 플레이어가 방향을 바꿔도 속도가 순간 반전하지 않는다.
                float desired = Mathf.Sign(offset) * Mathf.Min(Settings.trackingMaxSpeed,
                    Mathf.Sqrt(2f * Settings.trackingAcceleration * Mathf.Abs(offset)));
                horizontalSpeed = Mathf.MoveTowards(horizontalSpeed, desired, Settings.trackingAcceleration * dt);
                controller.Velocity = new Vector2(horizontalSpeed, (TargetHeight - transform.position.y) / dt);
                break;
            case FlightPhase.Slam:
                controller.Velocity = new Vector2(0f, -Settings.slamSpeed);
                // 천장에 완전히 막혀 접지 상태를 벗어나지 못하면 새 OnLanded가 발생하지 않는다.
                // 실제 모터 지면 접촉도 확인해 같은 착지 경로로 한 번만 처리한다.
                if (controller.IsGrounded && controller.Motor.RaycastEnvironment(bodyCollider.bounds.center,
                    Vector2.down, bodyCollider.bounds.extents.y + 0.03f).collider != null)
                {
                    HandleLanding();
                    break;
                }
                // 바닥 없는 맵에서도 무한 Execute를 남기지 않고 원래 중력을 복원한다.
                if (PhaseElapsed >= Settings.travelTimeout) StopAbility();
                break;
        }
    }

    private void HandleLanding()
    {
        if (State != AbilityState.Execute || CurrentPhase != FlightPhase.Slam) return;
        ReturnPreview();
        CompletedSlams++;
        controller.Velocity = Vector2.zero;
        RaycastMotor2D.InvalidateAttackQuerySync();
        RaycastMotor2D.PrepareAttackQueries();
        var floor = controller.Motor.RaycastEnvironment(bodyCollider.bounds.center,
            Vector2.down, bodyCollider.bounds.extents.y + 0.25f);
        LastImpactPoint = floor.collider != null ? floor.point
            : new Vector2(bodyCollider.bounds.center.x, bodyCollider.bounds.min.y);
        var wave = WorldEntityPool.Instance.Spawn(Settings.groundWavePrefab, LastImpactPoint, Quaternion.identity);
        wave.InitializeWave(controller, Settings.waveDamage, Settings.platformLayer);
        SpawnedWaves++;
        feedbackPlayer.Play(Settings.landingFeedback, LastImpactPoint);
        if (CompletedSlams < Settings.slamCount) BeginRise();
        else CurrentPhase = FlightPhase.None;
    }

    protected override bool CanFinishExecute(float elapsed)
    {
        return State == AbilityState.Execute && CompletedSlams >= Settings.slamCount;
    }

    protected override void OnRecoverStart()
    {
        ReturnPreview();
        RestorePresentation();
        CurrentPhase = FlightPhase.None;
        controller.Velocity = Vector2.zero;
        controller.GravityOn = savedGravityOn;
    }

    private void EnterPhase(FlightPhase phase)
    {
        CurrentPhase = phase;
        phaseStartedAt = Time.time;
        if (phase == FlightPhase.Tracking) SpawnPreview();
        else if (phase == FlightPhase.Rise) ReturnPreview();
    }

    private void FacePlayer()
    {
        if (player != null && Mathf.Abs(player.position.x - transform.position.x) > 0.05f)
            controller.FacingUpdate(player.position.x > transform.position.x ? 1 : -1);
    }

    private void LateUpdate()
    {
        if (State != AbilityState.Execute || brain.IsPaused) return;
        AnimationClip clip = CurrentPhase == FlightPhase.Rise ? Settings.riseClip
            : CurrentPhase == FlightPhase.Tracking ? Settings.trackingClip : Settings.slamClip;
        if (clip == null) return;
        // 추적 Clip에는 가속·등속 회전·감속 정렬이 모두 들어 있으므로 반복하지 않는다.
        float sample = Mathf.Min(PhaseElapsed, clip.length);
        if (CurrentPhase == FlightPhase.Rise)
        {
            float height = TargetHeight - riseStartY;
            sample = height > .001f ? Mathf.Clamp01((transform.position.y - riseStartY) / height) * clip.length : clip.length;
        }
        clip.SampleAnimation(controller.model, sample);
        if (activePreview != null)
        {
            activePreview.SetOrigin(new Vector2(bodyCollider.bounds.center.x, bodyCollider.bounds.min.y));
            activePreview.SetDirection(Vector2.down);
            activePreview.UpdatePreviewVisual(CurrentPhase == FlightPhase.Tracking
                ? Mathf.Max(0, Settings.trackingDuration - PhaseElapsed) : 0, Settings.trackingDuration);
        }
    }

    protected override void OnStop() => ReleaseMovement();
    private void OnDisable() => ReleaseMovement();

    private void ReleaseMovement()
    {
        if (!ownsMovement || controller == null) return;
        ReturnPreview();
        RestorePresentation();
        controller.OnLanded -= HandleLanding;
        controller.GravityOn = savedGravityOn;
        controller.IgnoreExternalForces = savedIgnoreExternalForces;
        controller.externalForce = Vector2.zero;
        controller.Velocity = Vector2.zero;
        CurrentPhase = FlightPhase.None;
        ownsMovement = false;
    }

    private void SpawnPreview()
    {
        ReturnPreview();
        if (Settings.previewLinePrefab == null) return;
        var origin = new Vector2(bodyCollider.bounds.center.x, bodyCollider.bounds.min.y);
        activePreview = WorldEntityPool.Instance.Spawn(Settings.previewLinePrefab, origin, Quaternion.identity);
        // 수명은 각 착지와 중단에서 직접 관리한다. 낙하 중에도 예고선이 바닥을 가리킨다.
        activePreview.Show(origin, Vector2.down, Settings.floorSearchDistance,
            Settings.previewLineWidth, Settings.platformLayer, Settings.previewLineColor, 0);
    }

    private void ReturnPreview()
    {
        if (activePreview == null) return;
        activePreview.ForceReturnToPool();
        activePreview = null;
    }

    private void RestorePresentation()
    {
        if (Settings.restPoseClip != null) Settings.restPoseClip.SampleAnimation(controller.model, 0);
        // 직접 샘플링한 Execute 전용 Scale은 Animator의 상태 종료만으로 복원되지 않는다.
        if (bodyVisual != null) bodyVisual.localScale = restingBodyScale;
    }
}
