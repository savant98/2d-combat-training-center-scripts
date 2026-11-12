using System;
using System.Collections.Generic;
using UnityEngine;

// 도약 내려찍기 패턴(Stomper 2페이즈).
// 본체는 단단한 기계라 모양이 변하지 않는다. 움직임은 본체에 달린 로켓 추진기(LeapSlamThrusters)가 만든다.
// 준비: 추진기가 아래로 뻗어 나와 본체를 들어 올리고 예열한다.
// 실행: 1) 도약 — 추진기가 분사해 표시(displayRoot)만 위로 솟구쳐 화면 밖으로 사라진다. 실제 몸(모터 Collider)은 제자리에 둔다.
//       2) 체공 — 표시를 숨긴 채 실제 몸을 기존 Velocity·RaycastMotor2D 경로로 룸 중앙 착지점까지 옮긴다
//          (떠오름 → 수평 이동 → 바닥에 내려앉음. 위치를 직접 바꾸지 않는다). 그동안 룸 중앙의 사각형 피격 범위를 예고한다.
//       3) 내려찍기 — 예고가 끝나는 순간 착지하도록 표시가 화면 위에서 나타나 내려온다. 추진기는 위쪽에 거꾸로 달려 분사한다.
//       4) 착지 — 범위 전체에 한 번 피해를 주고 폭발한다. 추진기는 떨어져 나간다.
// 회수: 후딜레이 동안 자세를 고쳐 잡는다(기울기와 자세 제어 분사는 회수 Clip이 맡는다).
// 표시 높이는 실제 좌표와 무관하다. 화면에서는 빠르게 솟구쳤다가 그대로 내려찍는 한 동작으로 이어지고, 체공하는 모습은 보이지 않는다.
// 도약부터 착지까지 보스는 화면에 없으므로 피해를 받지 않고 HitBox·접촉 피해도 꺼 둔다.
[DefaultExecutionOrder(-50)]
public sealed class LeapSlamAbility : AiAbility
{
    public enum LeapPhase { None, Launch, Airborne, Dive, Landed }

    [SerializeField] private Collider2D bodyCollider;
    [Tooltip("표시만 올리고 내릴 대상(FacingObject). Animator가 기록하지 않는 Transform이어야 한다")]
    [SerializeField] private Transform displayRoot;
    [Tooltip("추진기를 붙일 본체 프레임(ModelBorderMask)")]
    [SerializeField] private Transform thrusterMount;

    public LeapPhase CurrentPhase { get; private set; }
    public float PhaseElapsed => Time.time - phaseStartedAt;
    // 룸 범위와 피격 범위(월드 좌표). 도약하는 순간 한 번 정한다.
    public Rect RoomRect { get; private set; }
    public Rect AreaRect { get; private set; }
    // 좌우 벽과 범위 사이에 남긴 폭(플레이어 폭 + 여백)
    public float SideSafeWidth { get; private set; }
    public bool UsedCameraBounds { get; private set; }
    public Vector2 LandingRoot { get; private set; }
    public Vector2 ImpactPoint { get; private set; }
    public float DisplayHeight { get; private set; }
    public bool DisplayHidden { get; private set; }
    public bool Arrived { get; private set; }
    public bool DroppedInPlace { get; private set; }
    public bool Protected => protection != null;
    public int ImpactCount { get; private set; }
    public int HitCount { get; private set; }
    public bool DamagedPlayer { get; private set; }
    public float LaunchStartedAt { get; private set; } = -1f;
    public float WarningStartedAt { get; private set; } = -1f;
    public float DiveStartedAt { get; private set; } = -1f;
    public float ImpactAt { get; private set; } = -1f;
    // 회수 동안 시작한 자세 제어 분사의 수와 마지막으로 분사한 쪽(프레임 로컬 x의 부호)
    public int JetCount { get; private set; }
    public int LastJetSide { get; private set; }
    public LeapSlamAreaVisual Visual => visual;
    public LeapSlamThrusters Thrusters => thrusters;
    public bool WarningSoundIsPlaying => warningLoop != null && warningLoop.IsValid;

    private const float ArriveTolerance = 0.04f;
    private static readonly Collider2D[] hitBuffer = new Collider2D[16];
    private readonly HashSet<PlayerHealth> damaged = new HashSet<PlayerHealth>();

    private LeapSlamDataSO Settings => (LeapSlamDataSO)data;
    private LeapSlamAreaVisual visual;
    private LeapSlamThrusters thrusters;
    private Transform player;
    private Camera viewCamera;
    private Collider2D hitBox;
    private Canvas hud;
    private Renderer[] displayRenderers;
    private SpriteRenderer frameRenderer;
    private bool[] savedRenderingOff;
    private IDisposable protection;
    private SoundHandle warningLoop, fireLoop;
    private float phaseStartedAt, landTime, transitY, targetX, nextWashTime;
    private float restDisplayY, restHudY, launchBaseHeight, bodyBottomOffset, bodyCenterOffsetX, bodyHeight;
    // 회수의 자세 제어 분사 두 번: 분사하는 쪽(프레임 로컬 x의 부호, 0은 아직 시작하지 않음)과 아직 내지 않은 불꽃의 수
    private const float JetEmitInterval = 0.009f;
    private readonly int[] jetSide = new int[2];
    private readonly float[] jetDebt = new float[2];
    private Vector2 lastTransitPosition, plannedFloorPoint;
    private int blockedTicks;
    private bool savedGravityOn, savedIgnoreExternalForces, savedHitBoxEnabled, savedHudEnabled;
    private bool ownsFlight, ownsDisplay, riseBlocked, transitMoved;

    private void Awake()
    {
        // Pool이 미리 만들 때 표시 개체와 추진기도 함께 만들어 첫 사용 순간의 끊김을 없앤다.
        EnsureVisual();
        EnsureThrusters();
    }

    private void EnsureVisual()
    {
        if (visual != null || data == null || Settings.areaVisualPrefab == null) return;
        visual = Instantiate(Settings.areaVisualPrefab);
        visual.name = name + "_LeapSlamArea";
    }

    private void EnsureThrusters()
    {
        if ((thrusters != null && thrusters.IsValid) || data == null || thrusterMount == null || Settings.thrusterPrefab == null) return;
        // 떨어져 나간 채 씬과 함께 사라졌다면 다시 만든다.
        thrusters?.Destroy();
        thrusters = new LeapSlamThrusters(thrusterMount, Settings.thrusterPrefab, Settings.thrusterStroke);
    }

    protected override void OnWindupStart()
    {
        EnsureVisual();
        EnsureThrusters();
        ReleaseFlight();
        ReleaseDisplay(false);
        StopWarningLoop();
        StopFireLoop();
        if (visual != null) visual.HideAll();
        CurrentPhase = LeapPhase.None;
        Arrived = DroppedInPlace = DamagedPlayer = false;
        ImpactCount = HitCount = 0;
        LaunchStartedAt = WarningStartedAt = DiveStartedAt = ImpactAt = -1f;
        player = GameManager.Instance != null && GameManager.Instance.Player != null
            ? GameManager.Instance.Player.transform : null;
        controller.Velocity.x = 0f;
        FacePlayer();
        // 준비부터 표시 높이를 쓴다(추진기가 본체를 들어 올린다). 실제 몸과 모터 Collider는 바닥에 그대로 있다.
        ownsDisplay = true;
        restDisplayY = displayRoot.localPosition.y;
        if (hud == null) hud = controller.GetComponentInChildren<Canvas>(true);
        restHudY = hud != null ? hud.transform.localPosition.y : 0f;
        nextWashTime = 0f;
        if (thrusters != null) thrusters.Mount(false);
    }

    protected override void OnWindup(float elapsed)
    {
        // 준비 동안은 제자리에 있고, 공중에서 시작했다면 중력으로 내려앉는다.
        controller.Velocity.x = 0f;
        if (thrusters == null) return;
        // 추진기가 뻗어 나온 만큼 본체가 올라가, 분사구는 바닥을 짚고 있다.
        float deployTime = Mathf.Min(Settings.thrusterDeployTime, data.windupTime);
        thrusters.SetDeployment(Mathf.SmoothStep(0f, 1f, elapsed / deployTime));
        SetDisplayHeight(thrusters.LiftHeight);
        if (elapsed < deployTime || visual == null || Time.time < nextWashTime) return;
        // 다 뻗으면 예열한다. 분사구가 바닥에 닿아 있어 불길은 발치에서 좌우로 새어 나오고, 발사가 가까울수록 커진다.
        nextWashTime = Time.time + Settings.preheatWashInterval;
        float preheat = data.windupTime > deployTime ? Mathf.Clamp01((elapsed - deployTime) / (data.windupTime - deployTime)) : 1f;
        for (int i = 0; i < 2; i++) visual.EmitNozzleWash(thrusters.NozzleTip(i), preheat);
    }

    protected override void OnExecuteStart()
    {
        if (!TryPlan())
        {
            // 룸 범위나 착지할 바닥을 찾지 못하면 도약하지 않는다.
            StopAbility();
            return;
        }
        BeginFlight();
    }

    // 도약하는 순간의 룸 범위·피격 범위·착지점을 정한다. 지형 질의는 모터의 RaycastEnvironment만 쓴다.
    private bool TryPlan()
    {
        var motor = controller.Motor;
        RaycastMotor2D.InvalidateAttackQuerySync();
        RaycastMotor2D.PrepareAttackQueries();
        Bounds body = bodyCollider.bounds;
        bodyHeight = body.size.y;
        bodyBottomOffset = body.min.y - transform.position.y;
        bodyCenterOffsetX = body.center.x - transform.position.x;

        Bounds cameraBounds = default;
        bool hasBounds = Settings.clampToCameraBounds && EnemyCameraBounds.TryGet(out cameraBounds);
        UsedCameraBounds = hasBounds;

        // 좌우 벽: 보스와 플레이어 자리에서 각각 찾아 더 넓은 쪽을 쓴다(가운데 지형에 가려 좁게 잡히지 않게).
        float search = Settings.wallSearchDistance;
        float left = float.PositiveInfinity, right = float.NegativeInfinity;
        ProbeWalls(motor, body.center, search, ref left, ref right);
        if (player != null) ProbeWalls(motor, (Vector2)player.position + Vector2.up * 0.1f, search, ref left, ref right);
        if (hasBounds)
        {
            left = float.IsInfinity(left) ? cameraBounds.min.x : Mathf.Max(left, cameraBounds.min.x);
            right = float.IsInfinity(right) ? cameraBounds.max.x : Mathf.Min(right, cameraBounds.max.x);
        }
        if (float.IsInfinity(left) || float.IsInfinity(right) || right - left < body.size.x + 1f) return false;
        float width = right - left;
        float centerX = (left + right) * 0.5f;

        // 착지 바닥: 룸 가운데 x에서 아래로 처음 닿는 바닥
        float vertical = Settings.verticalSearchDistance;
        float probeY = Mathf.Max(body.center.y, player != null ? player.position.y + 0.1f : float.NegativeInfinity);
        if (!TryFindFloor(motor, centerX, hasBounds ? cameraBounds.center.y : float.NaN, probeY, body.center.y, vertical, out var landingFloor))
            return false;

        // 룸의 바닥·천장: 세 군데에서 찾아 가장 낮은 바닥과 가장 높은 천장을 쓴다(발판에 가려 낮게 잡히지 않게).
        float roomFloor = landingFloor.point.y, roomCeiling = float.NegativeInfinity;
        for (int i = 0; i < 3; i++)
        {
            float x = left + width * (0.2f + 0.3f * i);
            float floorHere = landingFloor.point.y;
            if (TryFindFloor(motor, x, hasBounds ? cameraBounds.center.y : float.NaN, probeY, body.center.y, vertical, out var floor))
            {
                floorHere = floor.point.y;
                roomFloor = Mathf.Min(roomFloor, floorHere);
            }
            var ceiling = motor.RaycastEnvironment(new Vector2(x, floorHere + 0.3f), Vector2.up, vertical);
            if (ceiling.collider != null && ceiling.distance > 0.02f) roomCeiling = Mathf.Max(roomCeiling, ceiling.point.y);
        }
        if (float.IsInfinity(roomCeiling))
            roomCeiling = hasBounds ? cameraBounds.max.y : roomFloor + Settings.fallbackRoomHeight;
        else if (hasBounds) roomCeiling = Mathf.Min(roomCeiling, cameraBounds.max.y);
        float height = roomCeiling - roomFloor;
        if (height < body.size.y + 1f) return false;
        RoomRect = new Rect(left, roomFloor, width, height);

        // 좌우 벽 쪽에는 플레이어 하나가 설 자리와 약간의 여백만 남기고 나머지를 모두 덮는다. 그래도 범위가 몸 폭보다 좁아지지는 않는다.
        float playerWidth = Settings.fallbackPlayerWidth;
        if (player != null && player.TryGetComponent(out Collider2D playerBody)) playerWidth = playerBody.bounds.size.x;
        SideSafeWidth = playerWidth + Settings.sideSafePadding;
        float areaWidth = Mathf.Max(body.size.x, Mathf.Min(width * Settings.areaWidthRatio, width - 2f * SideSafeWidth));
        float areaHeight = height * Settings.areaHeightRatio;
        AreaRect = new Rect(centerX - areaWidth * 0.5f, roomFloor + (height - areaHeight) * Settings.areaVerticalAnchor,
            areaWidth, areaHeight);

        targetX = centerX - bodyCenterOffsetX;
        float ceilingLimit = roomCeiling - (bodyHeight + bodyBottomOffset) - 0.3f;
        SetLanding(centerX, landingFloor.point.y, ceilingLimit);
        // 실제 몸은 체공 높이에서 내려앉는다. 통과 발판(OneWay)은 몸 바닥이 그 위에 있을 때만 바닥이 되므로,
        // 지금 자리에서 찾은 바닥과 다를 수 있다. 체공 높이의 발에서 다시 찾아 예고하는 착지점을 실제 착지와 맞춘다.
        for (int i = 0; i < 2; i++)
        {
            var below = motor.RaycastFloorBelow(new Vector2(centerX, transitY + bodyBottomOffset), 0.05f, vertical);
            if (below.collider == null || below.normal.y < 0.5f || Mathf.Abs(below.point.y - plannedFloorPoint.y) < 0.001f) break;
            SetLanding(centerX, below.point.y, ceilingLimit);
        }
        return true;
    }

    // 착지 바닥 높이로 착지점과 체공 높이를 정한다. 중간 지형을 넘도록 띄우되 천장 아래에 머문다.
    private void SetLanding(float centerX, float floorY, float ceilingLimit)
    {
        plannedFloorPoint = new Vector2(centerX, floorY);
        LandingRoot = new Vector2(targetX, floorY - bodyBottomOffset);
        float clearance = Mathf.Max(transform.position.y, LandingRoot.y) + Settings.transitClearance;
        transitY = Mathf.Max(transform.position.y, Mathf.Min(clearance, ceilingLimit));
    }

    private static void ProbeWalls(RaycastMotor2D motor, Vector2 origin, float distance, ref float left, ref float right)
    {
        var leftHit = motor.RaycastEnvironment(origin, Vector2.left, distance);
        if (leftHit.collider != null && leftHit.distance > 0.001f) left = Mathf.Min(left, leftHit.point.x);
        var rightHit = motor.RaycastEnvironment(origin, Vector2.right, distance);
        if (rightHit.collider != null && rightHit.distance > 0.001f) right = Mathf.Max(right, rightHit.point.x);
    }

    // 지형 속에서 시작한 질의(거리 0)는 버리고 다음 높이에서 다시 찾는다.
    private static bool TryFindFloor(RaycastMotor2D motor, float x, float firstY, float secondY, float thirdY, float distance, out RaycastHit2D floor)
    {
        floor = default;
        for (int i = 0; i < 3; i++)
        {
            float y = i == 0 ? firstY : i == 1 ? secondY : thirdY;
            if (float.IsNaN(y) || float.IsInfinity(y)) continue;
            var hit = motor.RaycastEnvironment(new Vector2(x, y), Vector2.down, distance);
            if (hit.collider == null || hit.distance <= 0.02f || hit.normal.y < 0.5f) continue;
            floor = hit;
            return true;
        }
        return false;
    }

    private void BeginFlight()
    {
        savedGravityOn = controller.GravityOn;
        savedIgnoreExternalForces = controller.IgnoreExternalForces;
        ownsFlight = true;
        controller.GravityOn = false;
        controller.IgnoreExternalForces = true;
        controller.externalForce = Vector2.zero;
        controller.Velocity = Vector2.zero;

        // 도약부터 착지까지 화면에 없으므로 피해·피격 판정·접촉 피해를 모두 막는다. 유도 무기의 표적에서도 빠진다.
        protection = controller.Health.AcquireInvulnerability(this);
        controller.CanTouchDamageActive(false);
        if (hitBox == null) hitBox = controller.Health.GetComponent<Collider2D>();
        if (hitBox != null)
        {
            savedHitBoxEnabled = hitBox.enabled;
            hitBox.enabled = false;
        }

        Arrived = DroppedInPlace = riseBlocked = transitMoved = false;
        blockedTicks = 0;
        LaunchStartedAt = Time.time;
        // 추진기가 들어 올린 높이에서 이어서 솟구친다.
        if (thrusters != null)
        {
            thrusters.SetDeployment(1f);
            SetDisplayHeight(thrusters.LiftHeight);
            thrusters.SetFlame(Settings.launchFlameScale);
        }
        launchBaseHeight = DisplayHeight;
        Vector2 origin = FloorPoint();
        feedbackPlayer.Play(Settings.launchFeedback, origin);
        PlaySound(Settings.launchFlameSound, origin);
        if (visual != null)
        {
            visual.PlayStreak(origin, OffscreenHeight() + bodyHeight, false);
            // 분사 불꽃이 바닥을 때리고 좌우로 퍼진다.
            visual.EmitLaunchBlast(origin, thrusters != null ? thrusters.SideOffset : bodyCollider.bounds.extents.x * 0.55f);
        }
        EnterPhase(LeapPhase.Launch);
    }

    protected override void OnExecute(float elapsed)
    {
        switch (CurrentPhase)
        {
            case LeapPhase.Launch: UpdateLaunch(); break;
            case LeapPhase.Airborne: UpdateAirborne(); break;
            case LeapPhase.Dive: UpdateDive(); break;
        }
    }

    private void UpdateLaunch()
    {
        float t = PhaseElapsed;
        float height = launchBaseHeight + Settings.launchSpeed * t + 0.5f * Settings.launchAcceleration * t * t;
        if (height < OffscreenHeight() && t < Settings.launchTimeout)
        {
            SetDisplayHeight(height);
            return;
        }
        // 화면 밖으로 나가면 숨기고 표시를 제자리로 되돌린다. 숨긴 동안에는 실제 좌표가 어디든 보이지 않는다.
        SetDisplayHidden(true);
        SetDisplayHeight(0f);
        // 숨은 동안 추진기를 위쪽으로 옮겨 단다. 내려찍을 때는 위로 분사하며 본체를 내리꽂는다.
        if (thrusters != null)
        {
            thrusters.SetFlame(0f);
            thrusters.Mount(true);
            thrusters.SetDeployment(1f);
        }
        EnterPhase(LeapPhase.Airborne);
        WarningStartedAt = Time.time;
        landTime = Time.time + Settings.warningDuration;
        if (visual != null) visual.BeginWarning(AreaRect, plannedFloorPoint);
        StartWarningLoop();
    }

    private void UpdateAirborne()
    {
        float diveDuration = DiveDuration();
        float remaining = landTime - Time.time;
        if (!Arrived && remaining <= diveDuration)
        {
            // 착지점에 닿기 전에는 내려찍지 않는다. 기다려도 닿지 못하면 지금 자리 바로 아래에 내려앉아 찍는다.
            if (!DroppedInPlace && Time.time - WarningStartedAt >= Settings.warningDuration + Settings.transitGraceTime)
            {
                DroppedInPlace = true;
                riseBlocked = true;
                targetX = transform.position.x;
            }
            landTime = Time.time + diveDuration + Time.deltaTime;
            remaining = landTime - Time.time;
        }
        UpdateWarning(remaining);
        if (Arrived && remaining <= diveDuration) BeginDive();
    }

    private void BeginDive()
    {
        EnterPhase(LeapPhase.Dive);
        DiveStartedAt = Time.time;
        FacePlayer();
        Vector2 floor = FloorPoint();
        PlaySound(Settings.diveSound, floor);
        PlaySound(Settings.diveFlameSound, floor);
        SetDisplayHeight(Settings.diveSpeed * Mathf.Max(0f, landTime - Time.time));
        SetDisplayHidden(false);
        if (thrusters != null) thrusters.SetFlame(Settings.diveFlameScale);
        if (visual != null)
        {
            visual.SetLandingPoint(floor);
            visual.PlayStreak(floor, OffscreenHeight() + bodyHeight, true);
            // 불덩이가 되어 떨어진다. 화염은 표시 몸을 따라간다.
            visual.BeginDiveFlame(BodyCenter());
        }
    }

    private void UpdateDive()
    {
        float remaining = landTime - Time.time;
        UpdateWarning(remaining);
        if (remaining > 0f)
        {
            SetDisplayHeight(Settings.diveSpeed * remaining);
            if (visual != null) visual.UpdateDiveFlame(BodyCenter());
            return;
        }
        Land();
    }

    private void Land()
    {
        SetDisplayHeight(0f);
        // 화염 꼬리를 착지점까지 잇는다.
        if (visual != null) visual.UpdateDiveFlame(BodyCenter());
        StopWarningLoop();
        ImpactPoint = FloorPoint();
        ImpactAt = Time.time;
        ImpactCount++;
        ApplyAreaDamage();
        if (visual != null) visual.Explode(AreaRect, ImpactPoint);
        feedbackPlayer.Play(Settings.impactFeedback, ImpactPoint);
        PlaySound(Settings.impactSubSound, ImpactPoint);
        StartFireLoop();
        // 다 쓴 추진기는 착지 충격으로 떨어져 나간다.
        if (thrusters != null) thrusters.Jettison(ImpactPoint.y);
        // 착지와 함께 다시 맞을 수 있다(후딜레이).
        ReleaseFlight();
        CurrentPhase = LeapPhase.Landed;
    }

    // 떨어져 나간 추진기는 패턴 단계와 관계없이 끝까지 움직인다.
    private void LateUpdate()
    {
        if (thrusters != null) thrusters.Tick(Time.deltaTime);
    }

    // 폭발 뒤 화염이 타는 소리. 회수 동안 줄어들며 꺼진다.
    private void StartFireLoop()
    {
        StopFireLoop();
        if (SoundManager.Instance == null || Settings.fireLoopDuration <= 0f
            || Settings.fireLoopSound == null || Settings.fireLoopSound.clip == null) return;
        fireLoop = SoundManager.Instance.PlayLoop(Settings.fireLoopSound, ImpactPoint, null);
    }

    private void UpdateFireLoop()
    {
        if (fireLoop == null) return;
        float t = Settings.fireLoopDuration > 0f ? (Time.time - ImpactAt) / Settings.fireLoopDuration : 1f;
        if (t >= 1f || !fireLoop.IsValid)
        {
            StopFireLoop();
            return;
        }
        fireLoop.GetSource().volume = Settings.fireLoopSound.volume * (1f - t) * (1f - t);
    }

    private void StopFireLoop()
    {
        fireLoop?.Stop();
        fireLoop = null;
    }

    // 범위 안의 플레이어에게 한 번 피해를 준다. 질의는 모터의 공격 질의를 쓴다.
    private void ApplyAreaDamage()
    {
        RaycastMotor2D.InvalidateAttackQuerySync();
        float inset = Mathf.Min(Settings.areaHitInset, Mathf.Min(AreaRect.width, AreaRect.height) * 0.25f);
        var size = new Vector2(AreaRect.width - inset * 2f, AreaRect.height - inset * 2f);
        int count = controller.Motor.OverlapAttackBox(AreaRect.center, size, 0f, hitBuffer, 1 << LayerMask.NameToLayer("Player"));
        damaged.Clear();
        for (int i = 0; i < count; i++)
        {
            var hit = hitBuffer[i];
            var health = hit != null ? hit.GetComponentInParent<PlayerHealth>() : null;
            if (health == null || !damaged.Add(health)) continue;
            HitCount++;
            float before = health.currentHP;
            health.TakeDamage(Settings.damage, hit.ClosestPoint(ImpactPoint));
            if (health.currentHP < before) DamagedPlayer = true;
        }
        damaged.Clear();
    }

    protected override bool CanFinishExecute(float elapsed) => CurrentPhase == LeapPhase.Landed;

    protected override void OnRecoverStart()
    {
        controller.Velocity = Vector2.zero;
        jetSide[0] = jetSide[1] = 0;
        JetCount = 0;
    }

    protected override void OnRecover(float elapsed)
    {
        controller.Velocity.x = 0f;
        UpdateFireLoop();
        UpdateRecoverJet(0, elapsed, Settings.firstJetTime);
        UpdateRecoverJet(1, elapsed, Settings.secondJetTime);
    }

    // 회수 Clip이 기운 본체를 세우는 순간에 맞춰, 기운(낮은) 쪽 옆면에서 바깥으로 분사한다. 본체는 그 반작용으로 넘어가 바로 선다.
    // 분사 자리와 방향은 지금 화면의 프레임 자세에서 읽으므로 기울기와 좌우 반전을 그대로 따른다.
    private void UpdateRecoverJet(int index, float elapsed, float startTime)
    {
        if (visual == null || thrusterMount == null || elapsed < startTime || elapsed >= startTime + Settings.jetDuration) return;
        Bounds frame = FrameBounds();
        if (jetSide[index] == 0)
        {
            // 분사를 시작하는 순간 낮은 쪽을 정하고 끝까지 그쪽에서 뿜는다. 기울지 않았다면 분사하지 않는다.
            float left = thrusterMount.TransformPoint(new Vector3(frame.min.x, frame.max.y, 0f)).y;
            float right = thrusterMount.TransformPoint(new Vector3(frame.max.x, frame.max.y, 0f)).y;
            if (Mathf.Abs(left - right) < 0.02f) return;
            jetSide[index] = left < right ? -1 : 1;
            jetDebt[index] = 1f;
            JetCount++;
            LastJetSide = jetSide[index];
            PlaySound(Settings.jetSound, thrusterMount.position);
        }
        else jetDebt[index] += Time.deltaTime / JetEmitInterval;
        // 프레임 속도와 관계없이 같은 굵기로 보이게, 흐른 시간에 비례한 수만큼 불꽃을 낸다.
        int count = Mathf.Min(4, Mathf.FloorToInt(jetDebt[index]));
        if (count <= 0) return;
        jetDebt[index] -= count;
        float x = jetSide[index] < 0 ? frame.min.x : frame.max.x;
        float y = Mathf.Lerp(frame.min.y, frame.max.y, 0.8f);
        Vector2 origin = thrusterMount.TransformPoint(new Vector3(x, y, 0f));
        Vector2 inner = thrusterMount.TransformPoint(new Vector3(frame.center.x, y, 0f));
        for (int i = 0; i < count; i++) visual.EmitSideJet(origin, origin - inner);
    }

    private Bounds FrameBounds()
    {
        if (frameRenderer == null) frameRenderer = thrusterMount.GetComponent<SpriteRenderer>();
        return frameRenderer != null && frameRenderer.sprite != null ? frameRenderer.sprite.bounds : new Bounds(Vector3.zero, Vector3.one);
    }

    // CharacterController2D.FixedUpdate보다 먼저 속도를 정해, 숨은 동안 실제 몸을 모터로 착지점까지 옮긴다.
    private void FixedUpdate()
    {
        if (State != AbilityState.Execute || !ownsFlight || brain.IsPaused) return;
        if (CurrentPhase != LeapPhase.Airborne || Arrived)
        {
            controller.Velocity = Vector2.zero;
            return;
        }

        Vector2 current = transform.position;
        // Collider보다 작은 보폭으로 옮겨 모터가 지형을 빠짐없이 확인하게 한다.
        float step = Mathf.Max(0.02f, Mathf.Min(bodyCollider.bounds.extents.x, bodyCollider.bounds.extents.y) * 0.5f);
        bool aligned = Mathf.Abs(current.x - targetX) <= ArriveTolerance;
        bool rising = !aligned && !riseBlocked && current.y < transitY - ArriveTolerance;

        // 직전 이동이 지형에 막혔으면 길을 바꾼다. 떠오르다 막히면 그 높이에서 옆으로 가고, 옆이 막히면 그 자리에 내려앉는다.
        if (transitMoved && Vector2.Distance(current, lastTransitPosition) < step * 0.1f) blockedTicks++;
        else blockedTicks = 0;
        if (blockedTicks >= 3)
        {
            blockedTicks = 0;
            if (rising) { riseBlocked = true; rising = false; }
            else if (!aligned)
            {
                DroppedInPlace = true;
                targetX = current.x;
                aligned = true;
            }
        }
        lastTransitPosition = current;
        transitMoved = true;

        if (aligned && controller.IsGrounded)
        {
            Arrived = true;
            controller.Velocity = Vector2.zero;
            return;
        }
        Vector2 next = rising ? new Vector2(current.x, transitY)
            : aligned ? new Vector2(targetX, current.y - step)
            : new Vector2(targetX, current.y);
        controller.Velocity = Vector2.ClampMagnitude(next - current, step) / Time.fixedDeltaTime;
    }

    private void EnterPhase(LeapPhase phase)
    {
        CurrentPhase = phase;
        phaseStartedAt = Time.time;
    }

    private void UpdateWarning(float remaining)
    {
        if (visual != null) visual.UpdateWarning(remaining, Settings.warningDuration);
        if (!WarningSoundIsPlaying) return;
        // 착지가 가까울수록 경고음이 높아진다.
        float progress = 1f - Mathf.Clamp01(remaining / Settings.warningDuration);
        warningLoop.GetSource().pitch = Mathf.Lerp(Settings.warningLoopPitch.x, Settings.warningLoopPitch.y, progress * progress);
    }

    private void StartWarningLoop()
    {
        StopWarningLoop();
        if (SoundManager.Instance == null || Settings.warningLoopSound == null || Settings.warningLoopSound.clip == null) return;
        warningLoop = SoundManager.Instance.PlayLoop(Settings.warningLoopSound, AreaRect.center, null);
    }

    private void StopWarningLoop()
    {
        warningLoop?.Stop();
        warningLoop = null;
    }

    private static void PlaySound(SoundOptions sound, Vector2 position)
    {
        if (SoundManager.Instance != null && sound != null && sound.clip != null)
            SoundManager.Instance.PlaySound(sound, position);
    }

    // 몸 아랫면 가운데(바닥 접점). Transform 기준으로 계산해 물리 동기화를 기다리지 않는다.
    private Vector2 FloorPoint()
        => new Vector2(transform.position.x + bodyCenterOffsetX, transform.position.y + bodyBottomOffset);

    // 지금 화면에 보이는 몸의 가운데(표시 높이를 더한 자리)
    private Vector2 BodyCenter() => FloorPoint() + Vector2.up * (DisplayHeight + bodyHeight * 0.5f);

    // 몸체 아랫면이 화면 위쪽 끝과 여유를 넘는 표시 높이. 카메라가 플레이어를 따라 움직이므로 매번 다시 잰다.
    private float OffscreenHeight()
    {
        if (viewCamera == null) viewCamera = Camera.main;
        if (viewCamera == null || !viewCamera.orthographic) return Settings.fallbackOffscreenHeight;
        float top = viewCamera.transform.position.y + viewCamera.orthographicSize + Settings.offscreenMargin;
        return Mathf.Max(2f, top - FloorPoint().y);
    }

    private float DiveDuration() => Mathf.Max(0.05f, OffscreenHeight() / Settings.diveSpeed);

    private void SetDisplayHeight(float height)
    {
        DisplayHeight = height;
        Vector3 position = displayRoot.localPosition;
        position.y = restDisplayY + height;
        displayRoot.localPosition = position;
        // 머리 위 HUD도 본체를 따라 오르내린다(본체가 들려도 HUD와의 간격이 그대로다).
        if (hud == null) return;
        Vector3 hudPosition = hud.transform.localPosition;
        hudPosition.y = restHudY + height;
        hud.transform.localPosition = hudPosition;
    }

    // Renderer의 enabled는 Animator가 기록하므로 건드리지 않고 그리기만 끈다. 머리 위 HUD도 함께 숨긴다.
    private void SetDisplayHidden(bool hidden)
    {
        if (DisplayHidden == hidden) return;
        DisplayHidden = hidden;
        if (hidden)
        {
            displayRenderers = displayRoot.GetComponentsInChildren<Renderer>(true);
            savedRenderingOff = new bool[displayRenderers.Length];
            for (int i = 0; i < displayRenderers.Length; i++)
            {
                savedRenderingOff[i] = displayRenderers[i].forceRenderingOff;
                displayRenderers[i].forceRenderingOff = true;
            }
            if (hud != null)
            {
                savedHudEnabled = hud.enabled;
                hud.enabled = false;
            }
            return;
        }
        if (displayRenderers != null)
            for (int i = 0; i < displayRenderers.Length; i++)
                if (displayRenderers[i] != null) displayRenderers[i].forceRenderingOff = savedRenderingOff[i];
        if (hud != null) hud.enabled = savedHudEnabled;
    }

    private void FacePlayer()
    {
        if (player != null && Mathf.Abs(player.position.x - transform.position.x) > 0.05f)
            controller.FacingUpdate(player.position.x > transform.position.x ? 1 : -1);
    }

    // 패턴이 끝나거나 끊겨도 떨어져 나간 추진기는 제 수명대로 사라진다. 비활성화될 때는 바로 거둔다.
    protected override void OnStop() => Cleanup(true);
    private void OnDisable() => Cleanup(false);

    private void OnDestroy()
    {
        if (visual != null) Destroy(visual.gameObject);
        if (thrusters != null) thrusters.Destroy();
    }

    private void Cleanup(bool keepJettisoned)
    {
        StopWarningLoop();
        StopFireLoop();
        ReleaseFlight();
        ReleaseDisplay(keepJettisoned);
        if (visual != null) visual.HideAll();
        CurrentPhase = LeapPhase.None;
    }

    // 준비부터 올린 표시 높이와 추진기를 되돌린다. 준비 중에 끊겨도 본체가 떠 있거나 추진기가 남지 않는다.
    private void ReleaseDisplay(bool keepJettisoned)
    {
        if (thrusters != null && !(keepJettisoned && thrusters.IsJettisoned)) thrusters.Hide();
        if (!ownsDisplay) return;
        ownsDisplay = false;
        SetDisplayHidden(false);
        SetDisplayHeight(0f);
    }

    // 도약 동안 바꾼 표시·보호·판정·중력을 되돌린다. 착지, 중단, 비활성화 어느 경로에서 불러도 한 번만 되돌린다.
    private void ReleaseFlight()
    {
        if (!ownsFlight || controller == null) return;
        ownsFlight = false;
        SetDisplayHidden(false);
        SetDisplayHeight(0f);
        protection?.Dispose();
        protection = null;
        if (hitBox != null) hitBox.enabled = savedHitBoxEnabled;
        controller.CanTouchDamageActive(true);
        controller.GravityOn = savedGravityOn;
        controller.IgnoreExternalForces = savedIgnoreExternalForces;
        controller.externalForce = Vector2.zero;
        controller.Velocity = Vector2.zero;
    }
}
