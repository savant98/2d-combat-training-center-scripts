using UnityEngine;

// 레일건 충전 추가타 한 발(2026-11-21 무장 특성 4차 — 사용자 화면 검수 전 시안). 규칙·요청 원문 요지는 RailEcho(Assets/Scripts/Weapons/State/RailEcho.cs).
// RailEcho.Begin이 풀에서 꺼내 Begin을 부른다. 흐름(본 공격 뒤 시간):
//   대기(보이지 않음) → 쏘기 summonTime 전: Player 둘레 자리에 소환 문양(Gate)이 나타나 목표를 겨눈다(작은 빛 알갱이가 모이고 고리가 좁혀짐)
//   → (k + 1) × 간격에 발사: 종류별 미니 공격 + 발사 큐(소리·총구 섬광) → 빛줄기·문양이 사라지면 풀로.
// - 자리(slots): 보는 방향 기준 Player 몸 가운데에서의 칸(위쪽·아래쪽 바깥). 자리까지 벽·바닥이 있으면 모터의 RaycastEnvironment로
//   그 앞(wallMargin)까지 줄인다. 너무 많이 줄면(서 있을 때 아래 자리) 몸 뒤 낮은 자리(바닥 근처 — 사용자 그림의 '아래쪽 바깥')를,
//   mirrorBlockedSlots면(파괴 광선) 위아래를 뒤집은 자리를 쓴다(굵은 본 레이저 안에 묻히지 않게).
//   소환 중에는 Player를 따라다니고(보간 위치), 쏜 뒤에는 그 자리에 남는다.
// - 종류(kind):
//   Rail  = 미니 레일 사격: 본 레일건 빛줄기(RailgunTracer)와 같은 재질의 가는 빛줄기가 목표 쪽으로 빠르게 뻗고(extendSpeed) 가늘어지며 사라진다.
//           판정은 RailEcho.Cast(관통, 쏘는 순간).
//   Laser = 미니 레이저: 소환 중 목표까지 조준선(본 레이저 예고선과 같은 재질)이 뻗고, 발사하면 본 레이저와 같은 재질의 작은 레이저가
//           잠깐 굵어졌다(burst) 유지(hold)된 뒤 사라진다. 판정은 쏘는 순간 한 번(RailEcho.Cast).
//   Spear = 미니 별창: 소환 문양 자리에 미니 별창(RailMiniSpear, StarSpear Variant)을 꺼내 목표 쪽으로 들고 있다가 쏜다(유도·폭발은 StarSpear).
// - 문양·빛줄기는 흰 사각형 + 절차형 셰이더(HammerSkillBlast·Player/Laser·Player/LaserPreview) Sprite 조각을 이 스크립트가 크기·알파로 움직인다.
//   값은 Prefab 직렬화 값(생성 도구 tools/Unity/RailgunTraitSetup.cs). 시간은 Time.deltaTime이라 히트스톱 때 함께 멈춘다.
[DefaultExecutionOrder(115)]   // FixedMotionPresentation(99)이 이 프레임의 보간 자세를 계산한 뒤에 따라간다
public sealed class RailEchoShot : WorldEntityBase
{
    private static readonly int QuadSizeId = Shader.PropertyToID("_QuadSize");
    private static readonly int MarginId = Shader.PropertyToID("_Margin");
    private static readonly int LengthId = Shader.PropertyToID("_Length");
    private static readonly int ReachId = Shader.PropertyToID("_Reach");
    private static readonly int HalfWidthId = Shader.PropertyToID("_HalfWidth");
    private static readonly int FlowId = Shader.PropertyToID("_Flow");
    private static readonly int ChargeId = Shader.PropertyToID("_Charge");
    private static readonly int FlashId = Shader.PropertyToID("_Flash");
    private static readonly int AlphaId = Shader.PropertyToID("_Alpha");

    private enum Phase { Idle, Pending, Summon, Fired, Dropped }

    [Header("Kind")]
    [SerializeField] private RailEchoKind kind = RailEchoKind.Rail;

    [Header("Place (보는 방향 기준, Player 몸 가운데에서 칸)")]
    [SerializeField] private Vector2[] slots = { new(-0.35f, 1.25f), new(-0.35f, -0.95f), new(-0.95f, 1.0f), new(-0.95f, -0.75f) };
    [SerializeField, Min(0f)] private float wallMargin = 0.25f;
    [SerializeField] private bool mirrorBlockedSlots;
    [SerializeField, Range(0f, 1f)] private float blockedShare = 0.6f;

    [Header("Gate (소환 문양: gate 아래 halo·star·core는 함께 커지고, ring·motes는 루트 자식이라 따로 움직인다)")]
    [SerializeField] private Transform gate;
    [SerializeField] private SpriteRenderer halo, star, core;
    [SerializeField] private SpriteRenderer ring;
    [SerializeField] private ParticleSystem motes;
    [SerializeField, Min(0.02f)] private float summonTime = 0.15f;
    [SerializeField, Min(0f)] private float haloSize = 1f, ringSize = 0.8f, starSize = 0.7f, coreSize = 0.32f;
    [SerializeField, Range(0.1f, 1f)] private float ringAspect = 0.45f;     // 고리를 쏘는 방향에 수직인 타원으로(가로 폭 비율)
    [SerializeField, Min(1f)] private float ringFrom = 2f;                  // 소환 시작 때 고리 크기 배율(좁혀지며 1이 된다)
    [SerializeField] private float starSpin = 540f;                        // 별빛 회전(도/초, 소환이 진행될수록 빨라진다)
    [SerializeField, Min(0.01f)] private float gateFade = 0.16f;            // 쏜 뒤 문양이 사라지는 시간
    [SerializeField, Min(1f)] private float firePop = 1.35f;                // 쏘는 순간 문양이 커지는 배율

    [Header("Preview (파괴 광선: 소환 중 목표까지 조준선)")]
    [SerializeField] private SpriteRenderer preview;
    [SerializeField, Min(0.001f)] private float previewHalfWidth = 0.045f;
    [SerializeField, Min(0.01f)] private float previewQuadHeight = 0.7f;

    [Header("Beam (기본 사격·파괴 광선)")]
    [SerializeField] private SpriteRenderer beam;
    [SerializeField] private SpriteRenderer afterglow, flare, head, impact;
    [SerializeField] private ParticleSystem sparks;
    [SerializeField, Min(0f)] private float extendSpeed = 220f;             // 빛줄기 끝이 뻗어 나가는 빠르기(칸/초, 0 = 한 번에)
    [SerializeField, Min(0.001f)] private float peakHalfWidth = 0.13f;      // 쏘는 순간(또는 터진) 반폭
    [SerializeField, Min(0.001f)] private float thinHalfWidth = 0.04f;      // 가늘어진(유지) 반폭
    [SerializeField, Min(0f)] private float burstTime = 0f;                 // 0.35 × 최대 반폭에서 최대 반폭까지(파괴 광선의 터짐)
    [SerializeField, Min(0f)] private float settleTime = 0.06f;             // 최대 반폭에서 유지 반폭까지
    [SerializeField, Min(0f)] private float holdTime = 0f;                  // 다 뻗은 뒤 사라지기 시작할 때까지
    [SerializeField, Min(0.01f)] private float fadeTime = 0.18f;
    [SerializeField, Min(0f)] private float flashTime = 0.05f;
    [SerializeField, Min(0f)] private float flowSpeed = 1f;
    [SerializeField, Min(0.001f)] private float afterglowHalfWidth = 0.09f;
    [SerializeField, Min(0f)] private float afterglowSpread = 0.6f;
    [SerializeField, Min(0.01f)] private float afterglowTime = 0.3f;
    [SerializeField, Range(0f, 1f)] private float afterglowAlpha = 0.55f;
    [SerializeField, Min(0f)] private float flareSize = 1.3f, headSize = 0.9f, impactSize = 1.1f;
    [SerializeField, Min(0.01f)] private float flareTime = 0.12f;
    [SerializeField, Min(0f)] private float blockedOverlap = 0.1f;
    [SerializeField, Min(0.01f)] private float beamMargin = 0.4f, beamEndMargin = 0.8f;
    [SerializeField, Min(0f)] private float sparkDensity = 0.6f;            // 빛줄기 1칸당 옆으로 튀는 불꽃 수
    [SerializeField, Min(0)] private int maxSparks = 18;
    [SerializeField] private Vector2 sparkSpeed = new(3f, 8f);
    [SerializeField] private Vector2 sparkLife = new(0.1f, 0.24f);
    [SerializeField, Min(0f)] private float moteDensity = 0f;               // 유지 중 빛줄기 1칸당 초당 앞으로 날아가는 빛 알갱이(파괴 광선)
    [SerializeField] private Vector2 moteSpeed = new(60f, 110f);

    private const float DropTime = 0.08f;

    private RailEchoRequest request;
    private PlayerController player;
    private PlayerWeaponTraits traits;
    private uint generation;
    private Phase phase = Phase.Idle;
    private bool counted;
    private float age, summonAt, fireAt, endAge, dropAge;
    private float facing = 1f, starAngle, moteDebt;
    private Vector2 slot, offset;
    private Collider2D aimTarget;
    private Vector2 aimPoint;
    private StarSpear heldSpear;
    private Vector2 origin, direction = Vector2.right;
    private float length;
    private bool blocked;
    private MaterialPropertyBlock block;
    private float quadSize = 6.2f, starBase = 6.2f;

    // ===== 검증·디버그용 읽기 전용 값 =====
    public RailEchoKind Kind => kind;
    public int Index { get; private set; }
    public bool IsPending => phase == Phase.Pending || phase == Phase.Summon;
    public bool IsSummoning => phase == Phase.Summon;
    public bool HasFired => phase == Phase.Fired;
    public float FireDelay => fireAt;                         // 본 공격 뒤 쏘는 시각(초)
    public float SummonTime => summonTime;
    public Vector2 SlotOffset => offset;                      // 지금 Player 기준 자리(벽에 맞춰 줄인 값)
    public Vector2 Emitter => transform.position;
    public Vector2 Origin => origin;                          // 쏜 자리
    public Vector2 Direction => direction;                    // 쏜 방향
    public Vector2 TargetPoint => aimPoint;                   // 겨눈 곳
    public float Length => length;
    public int HitCount { get; private set; }
    public int SlotCount => slots != null ? slots.Length : 0;
    public Vector2 SlotAt(int index) => slots != null && slots.Length > 0 ? slots[((index % slots.Length) + slots.Length) % slots.Length] : Vector2.zero;

    protected override void Awake()
    {
        base.Awake();
        block = new MaterialPropertyBlock();   // MonoBehaviour 필드 초기화 중에는 만들 수 없다
        var quadSource = beam != null ? beam : halo;
        if (quadSource != null && quadSource.sprite != null) quadSize = quadSource.sprite.bounds.size.x;
        if (star != null && star.sprite != null) starBase = Mathf.Max(0.01f, star.sprite.bounds.size.x);
    }

    public override void OnSpawnedFromPool()
    {
        base.OnSpawnedFromPool();
        ResetState();
    }

    public override void ForceReturnToPool()
    {
        Release();
        ResetState();
        base.ForceReturnToPool();
    }

    private void OnDisable() => Release();
    private void OnDestroy() => Release();

    // 쏘기 전에 사라지면(풀 일괄 회수·Scene 전환) 대기 수를 줄이고 들고 있던 미니 별창을 거둔다.
    private void Release()
    {
        bool wasPending = counted;
        if (counted)
        {
            counted = false;
            RailEcho.ActiveCount = Mathf.Max(0, RailEcho.ActiveCount - 1);
        }
        ReleaseSpear();
        if (wasPending)
        {
            var r = request;
            request = null;
            r?.onDropped?.Invoke(this);
        }
    }

    private void ReleaseSpear()
    {
        if (heldSpear != null && heldSpear.IsHeld) heldSpear.ForceReturnToPool();
        heldSpear = null;
    }

    private void ResetState()
    {
        phase = Phase.Idle;
        age = summonAt = fireAt = endAge = dropAge = 0f;
        starAngle = moteDebt = 0f;
        HitCount = 0;
        length = 0f;
        blocked = false;
        aimTarget = null;
        heldSpear = null;
        request = null;
        player = null;
        traits = null;
        ShowGate(false);
        SetBeamRenderers(false);
        if (preview != null) preview.enabled = false;
        Clear(motes);
        Clear(sparks);
    }

    /// <summary>RailEcho.Begin이 부른다. index = 묶음 안 순서, fireDelay = 본 공격 뒤 쏘는 시각(초).</summary>
    internal void Begin(RailEchoRequest request, int index, float fireDelay, PlayerWeaponTraits traits)
    {
        ResetState();
        this.request = request;
        player = request.owner;
        owner = player;
        ownerFaction = Faction.Player;
        damageInfo = request.damage;
        this.traits = traits;
        generation = traits.Generation;
        Index = index;
        facing = request.facing < 0f ? -1f : 1f;
        slot = SlotAt(request.slotBase + index);
        fireAt = Mathf.Max(0.01f, fireDelay);
        summonAt = Mathf.Max(0f, fireAt - summonTime);
        aimPoint = request.fallbackPoint;
        phase = Phase.Pending;
        counted = true;
        RailEcho.ActiveCount++;
        transform.SetPositionAndRotation(player.transform.position, Quaternion.identity);
    }

    private bool Alive()
    {
        if (request == null || player == null || !player.isActiveAndEnabled) return false;
        if (player.Health != null && player.Health.IsDead) return false;
        if (traits == null || traits.Generation != generation) return false;
        if (kind == RailEchoKind.Rail && traits.OverheatRemaining > 0f) return false;   // 과열 잠금 = 충전이 끊김
        return request.guard == null || request.guard();
    }

    private void Update()
    {
        if (phase == Phase.Idle || isReturning) return;
        float dt = Time.deltaTime;
        age += dt;
        switch (phase)
        {
            case Phase.Pending:
            case Phase.Summon:
                if (!Alive())
                {
                    Drop();
                    return;
                }
                if (phase == Phase.Pending && age >= summonAt) StartSummon();
                if (phase == Phase.Summon && age >= fireAt) Fire();
                break;
            case Phase.Fired:
                if (age - fireAt >= endAge) ReturnToPool();
                break;
            case Phase.Dropped:
                dropAge += dt;
                if (dropAge >= DropTime) ReturnToPool();
                break;
        }
    }

    // ===== 자리·목표 =====
    // 보는 방향 기준 자리를 지금 Player 둘레로: 벽·바닥이 가로막으면 그 앞까지 줄인다(모터 질의).
    // 막혀 blockedShare도 못 나가면(서 있을 때 아래 자리 — 그대로 줄이면 지름 1.5칸 몸 안에 들어간다) 다른 자리를 써 본다:
    //   mirrorBlockedSlots(파괴 광선) = 위아래를 뒤집고 조금 더 뒤로(굵은 본 레이저 밖에 남게),
    //   아니면 같은 쪽의 몸 뒤 낮은 자리(바닥 근처 — 사용자 그림의 '아래쪽 바깥'). 둘 중 더 멀리 나가는 쪽.
    private Vector2 PlaceSlot()
    {
        Vector2 local = new(slot.x * facing, slot.y);
        if (player == null || player.Motor == null) return local;
        Vector2 reach = Clamp(local, out float share);
        if (share >= blockedShare) return reach;
        float back = Mathf.Abs(slot.x);   // 대체 자리는 앞쪽 자리였더라도 늘 몸 뒤로(앞으로 가면 쏘는 선이 몸을 가로지른다)
        Vector2 alt = mirrorBlockedSlots
            ? new(-(back + 0.6f) * facing, -slot.y * 0.85f)
            : new(-(back + 0.9f) * facing, Mathf.Sign(slot.y) * Mathf.Min(Mathf.Abs(slot.y), 0.45f));
        Vector2 alternative = Clamp(alt, out float altShare);
        return altShare * alt.magnitude > share * local.magnitude ? alternative : reach;
    }

    private Vector2 Clamp(Vector2 local, out float share)
    {
        share = 1f;
        float distance = local.magnitude;
        if (distance < 1e-4f) return local;
        Vector2 dir = local / distance;
        RaycastHit2D hit = player.Motor.RaycastEnvironment(player.transform.position, dir, distance + wallMargin, request.platformLayer);
        if (hit.collider == null) return local;
        float reach = Mathf.Max(0f, hit.distance - wallMargin);
        share = reach / distance;
        return dir * reach;
    }

    // 쏠 곳: 지금 묻는 목표(파괴 광선) → 본 공격이 맞힌 적 → (search) 조준 방향의 가장 가까운 적 → 본 공격 끝점
    private void ResolveAim(bool search)
    {
        Collider2D target = request.liveTarget?.Invoke();
        if (!RailEcho.IsTarget(target)) target = RailEcho.IsTarget(request.target) ? request.target : null;
        if (target == null && search)
            target = RailEcho.FindNearest(player, player.transform.position, request.searchDirection, request.searchRange, request.searchHalfWidth, request.damageMask);
        aimTarget = target;
        aimPoint = target != null ? (Vector2)target.bounds.center : request.fallbackPoint;
    }

    private Vector2 CurrentAimPoint() => RailEcho.IsTarget(aimTarget) ? (Vector2)aimTarget.bounds.center : aimPoint;

    private Vector2 PresentedPlayer()
    {
        FixedMotionBody.GetPresentationPose(player.transform, out var presented, out _);
        return presented;
    }

    // ===== 소환 =====
    private void StartSummon()
    {
        phase = Phase.Summon;
        offset = PlaceSlot();
        ResolveAim(true);
        Vector2 at = PresentedPlayer() + offset;
        transform.SetPositionAndRotation(at, Quaternion.Euler(0f, 0f, AngleTo(at, CurrentAimPoint())));
        ShowGate(true);
        if (motes != null)
        {
            motes.Clear();
            motes.Play();
        }
        if (kind == RailEchoKind.Spear) SpawnSpear(at);
    }

    private void SpawnSpear(Vector2 at)
    {
        var weapon = request.spearWeapon;
        var spear = WorldEntityPool.Instance != null ? WorldEntityPool.Instance.Spawn(request.spearPrefab, at, Quaternion.identity) : null;
        if (spear == null) return;
        spear.Initialize(player, request.damage, weapon.spearSpeed, request.spearIndex, weapon.platformLayer);
        spear.RailCharged = true;
        spear.RailEcho = true;   // Initialize가 채운 넉백 방향을 지우고 적중마다 RailEchoHit 큐
        spear.ConfigureFlight(weapon.turnRateDegrees, weapon.maxFlightTime, weapon.explosionRadius * request.spearRadiusScale, weapon.targetSearchRadius);
        spear.Hold(at, AngleTo(at, CurrentAimPoint()));
        heldSpear = spear;
    }

    private void LateUpdate()
    {
        if (phase == Phase.Idle || isReturning) return;
        float dt = Time.deltaTime;
        switch (phase)
        {
            case Phase.Summon:
                if (player == null) return;
                offset = PlaceSlot();
                Vector2 at = PresentedPlayer() + offset;
                float angle = AngleTo(at, CurrentAimPoint());
                transform.SetPositionAndRotation(at, Quaternion.Euler(0f, 0f, angle));
                float s = Mathf.Clamp01((age - summonAt) / Mathf.Max(0.01f, fireAt - summonAt));
                DrawGate(s, dt);
                DrawPreview(s, at);
                if (heldSpear != null)
                {
                    if (heldSpear.IsHeld) heldSpear.Hold(at, angle);
                    else heldSpear = null;   // 풀 일괄 회수 등으로 이미 사라졌다
                }
                break;
            case Phase.Fired:
                float post = age - fireAt;
                DrawGateAfterFire(post, dt);
                DrawBeamParts(post, dt);
                break;
            case Phase.Dropped:
                DrawGateDrop(dt);
                break;
        }
    }

    // ===== 발사 =====
    private void Fire()
    {
        phase = Phase.Fired;
        offset = PlaceSlot();
        Vector2 emitter = (Vector2)player.transform.position + offset;
        ResolveAim(true);
        Vector2 delta = aimPoint - emitter;
        direction = delta.sqrMagnitude > 1e-4f ? delta.normalized
            : (request.searchDirection.sqrMagnitude > 1e-6f ? request.searchDirection.normalized : new Vector2(facing, 0f));
        origin = emitter;
        float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
        transform.SetPositionAndRotation(emitter, Quaternion.Euler(0f, 0f, angle));
        if (preview != null) preview.enabled = false;
        if (counted)
        {
            counted = false;
            RailEcho.ActiveCount = Mathf.Max(0, RailEcho.ActiveCount - 1);
        }
        RailEcho.TotalFired++;

        HitCount = 0;
        if (kind == RailEchoKind.Spear)
        {
            if (heldSpear != null && heldSpear.IsHeld)
            {
                heldSpear.Hold(emitter, angle);
                heldSpear.Launch(direction, aimTarget);
            }
            heldSpear = null;
            length = delta.magnitude;
        }
        else
        {
            float range = Mathf.Max(request.range, delta.magnitude + 1f);   // 목표가 본 사거리 끝에 있어도 닿게
            HitCount = RailEcho.Cast(player, emitter, direction, 0f, range, request.halfWidth, request.platformLayer, request.damageMask,
                request.damage, out length, out blocked);
            EmitSparks();
        }
        WeaponTraitFeedbackSO.Play(kind == RailEchoKind.Laser ? WeaponTraitCue.RailCross : WeaponTraitCue.RailEcho, emitter, direction);
        if (kind == RailEchoKind.Rail && request.rail != null) request.rail.RecordEcho(emitter, direction, HitCount);
        var aim = RailgunWeaponSO.FindAim(player);
        if (aim != null && aim.isActiveAndEnabled) aim.Barrel.Echo();   // 총 코일도 분홍으로 번쩍(충전에서 나온 추가타임을 잇는다)

        endAge = Mathf.Max(gateFade, motes != null ? motes.main.startLifetime.constantMax : 0f);
        if (kind != RailEchoKind.Spear)
        {
            float extend = extendSpeed > 0f ? length / extendSpeed : 0f;
            endAge = Mathf.Max(endAge, extend + holdTime + fadeTime, flareTime + extend, sparkLife.y);
            if (afterglow != null) endAge = Mathf.Max(endAge, afterglowTime);
        }
        endAge += 0.03f;
        var r = request;
        r.onFired?.Invoke(this, HitCount);
    }

    private void Drop()
    {
        phase = Phase.Dropped;
        dropAge = 0f;
        Release();
        if (preview != null) preview.enabled = false;
        if (motes != null) motes.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
    }

    // ===== 그림: 문양 =====
    private bool gateShown;

    private void ShowGate(bool show)
    {
        gateShown = show;
        if (gate != null) gate.gameObject.SetActive(show);
        if (halo != null) halo.enabled = show;
        if (star != null) star.enabled = show;
        if (core != null) core.enabled = show;
        if (ring != null) ring.enabled = show;
    }

    // 소환: 문양이 조금 넘쳤다 제자리, 고리가 좁혀지며 밝아지고, 별빛이 점점 빨리 돈다
    private void DrawGate(float s, float dt)
    {
        float appear = s < 0.7f ? Mathf.Lerp(0f, 1.18f, 1f - Mathf.Pow(1f - s / 0.7f, 3f)) : Mathf.Lerp(1.18f, 1f, (s - 0.7f) / 0.3f);
        if (gate != null) gate.localScale = Vector3.one * appear;
        SetQuad(halo, haloSize, 0.35f + 0.65f * s);
        float ringScale = Mathf.Lerp(ringFrom, 1f, EaseOut(s));
        SetRing(ringSize * ringScale, Mathf.Sqrt(s));
        SetQuad(core, coreSize * (0.6f + 0.4f * s), s);
        starAngle += starSpin * (0.3f + s) * dt;
        SetStar(starSize, starAngle);
    }

    // 쏜 뒤: 순간 커졌다가(firePop) 줄어들며 사라진다. 고리는 퍼지며 옅어진다.
    private void DrawGateAfterFire(float post, float dt)
    {
        if (!gateShown) return;
        float t = Mathf.Clamp01(post / gateFade);
        if (t >= 1f)
        {
            ShowGate(false);
            return;
        }
        float pop = post < 0.05f ? Mathf.Lerp(1f, firePop, post / 0.05f) : Mathf.Lerp(firePop, 0f, EaseIn((post - 0.05f) / Mathf.Max(0.01f, gateFade - 0.05f)));
        if (gate != null) gate.localScale = Vector3.one * Mathf.Max(0f, pop);
        float a = 1f - t;
        SetQuad(halo, haloSize, a);
        SetRing(ringSize * (1f + 0.8f * EaseOut(t)), a * a);
        SetQuad(core, coreSize * (1f + 0.5f * (1f - t)), a);
        starAngle += starSpin * 1.3f * dt;
        SetStar(starSize, starAngle);
    }

    private void DrawGateDrop(float dt)
    {
        if (!gateShown) return;
        float k = 1f - Mathf.Clamp01(dropAge / DropTime);
        if (k <= 0f)
        {
            ShowGate(false);
            return;
        }
        if (gate != null) gate.localScale *= Mathf.Max(0f, 1f - Mathf.Min(1f, dt / DropTime));
        if (ring != null)
        {
            Color c = ring.color;
            c.a *= k;
            ring.color = c;
        }
    }

    private void SetQuad(SpriteRenderer r, float size, float alpha)
    {
        if (r == null) return;
        bool show = alpha > 0.002f && size > 0f;
        if (r.enabled != show) r.enabled = show;
        if (!show) return;
        float scale = size / quadSize;
        r.transform.localScale = new Vector3(scale, scale, 1f);
        Color c = r.color;
        c.a = Mathf.Clamp01(alpha);
        r.color = c;
    }

    private void SetRing(float size, float alpha)
    {
        if (ring == null) return;
        SetQuad(ring, size, alpha);
        float scale = size / quadSize;
        ring.transform.localScale = new Vector3(scale * ringAspect, scale, 1f);
    }

    // 별빛 Sprite(별창·레이저 계열은 별 탄 Sprite — 정점 알파를 쓰지 않는 재질이라 크기로만 줄인다)
    private void SetStar(float size, float angle)
    {
        if (star == null) return;
        float scale = size / starBase;
        star.transform.localScale = new Vector3(scale, scale, 1f);
        star.transform.localRotation = Quaternion.Euler(0f, 0f, angle);
        if (!star.enabled) star.enabled = true;
    }

    // ===== 그림: 조준선(파괴 광선) =====
    private void DrawPreview(float s, Vector2 at)
    {
        if (preview == null) return;
        float reach = Vector2.Distance(at, CurrentAimPoint()) * EaseOut(Mathf.Clamp01(s / 0.45f));
        bool flash = s > 0.7f && Mathf.Repeat(age * 30f, 1f) < 0.5f;
        const float margin = 0.5f, endMargin = 0.6f;
        float quadLength = margin + Mathf.Max(reach, 0f) + endMargin;
        if (!preview.enabled) preview.enabled = true;
        Transform t = preview.transform;
        t.localRotation = Quaternion.identity;
        t.localPosition = new Vector3(quadLength * 0.5f - margin, 0f, 0f);
        t.localScale = new Vector3(quadLength / quadSize, previewQuadHeight / quadSize, 1f);
        preview.GetPropertyBlock(block);
        block.SetVector(QuadSizeId, new Vector4(quadLength, previewQuadHeight, 0f, 0f));
        block.SetFloat(MarginId, margin);
        block.SetFloat(ReachId, Mathf.Max(reach, 0f));
        block.SetFloat(HalfWidthId, Mathf.Lerp(previewHalfWidth * 1.6f, previewHalfWidth, s));
        block.SetFloat(FlowId, age * 24f);
        block.SetFloat(ChargeId, s);
        block.SetFloat(FlashId, flash ? 1f : 0f);
        block.SetFloat(AlphaId, Mathf.Clamp01(s * 3f));
        preview.SetPropertyBlock(block);
    }

    // ===== 그림: 빛줄기 =====
    private void SetBeamRenderers(bool show)
    {
        if (beam != null) beam.enabled = show;
        if (afterglow != null) afterglow.enabled = show;
        if (flare != null) flare.enabled = show;
        if (head != null) head.enabled = show;
        if (impact != null) impact.enabled = false;
    }

    private void DrawBeamParts(float post, float dt)
    {
        if (beam == null) return;
        float drawLength = blocked ? length + blockedOverlap : length;
        float reachT = extendSpeed > 0f && drawLength > 0.01f ? Mathf.Clamp01(post * extendSpeed / drawLength) : 1f;
        float shown = drawLength * reachT;
        float extendEnd = extendSpeed > 0f ? drawLength / extendSpeed : 0f;
        float life = post - extendEnd;   // 다 뻗은 뒤 지난 시간

        // 본체: (파괴 광선) 터졌다가 → 가늘어지고 → 유지 → 사라짐
        float fade = Mathf.Clamp01((post - holdTime) / fadeTime);
        float alpha = 1f - fade * fade;
        float width;
        if (burstTime > 0f && post < burstTime) width = Mathf.Lerp(peakHalfWidth * 0.35f, peakHalfWidth, post / burstTime);
        else width = Mathf.Lerp(peakHalfWidth, thinHalfWidth, EaseOut((post - burstTime) / Mathf.Max(0.001f, settleTime)));
        width *= 1f - 0.5f * fade;
        float flashValue = flashTime > 0f ? 1f - Mathf.Clamp01(post / flashTime) : 0f;
        bool showBeam = alpha > 0.001f && shown > 0.01f;
        if (beam.enabled != showBeam) beam.enabled = showBeam;
        if (showBeam) DrawBeam(beam, shown, width, flashValue, alpha, post * flowSpeed);

        if (afterglow != null)
        {
            float glowT = Mathf.Clamp01(post / afterglowTime);
            float glowAlpha = afterglowAlpha * (1f - glowT) * (1f - glowT);
            bool show = glowAlpha > 0.001f && shown > 0.01f;
            if (afterglow.enabled != show) afterglow.enabled = show;
            if (show) DrawBeam(afterglow, shown, afterglowHalfWidth * (1f + afterglowSpread * EaseOut(glowT)), 0f, glowAlpha, post * flowSpeed * 0.5f);
        }

        // 쏜 자리 섬광, 뻗어 나가는 끝의 빛(다 뻗으면 곧 꺼짐), 벽에 막혔으면 끝의 빛
        float flareT = Mathf.Clamp01(post / flareTime);
        SetFlare(flare, 0f, flareSize * (1f + 0.5f * EaseOut(flareT)), (1f - flareT) * (1f - flareT));
        float headAlpha = reachT < 1f ? 1f : 1f - Mathf.Clamp01(life / 0.05f);
        SetFlare(head, shown, headSize, headAlpha);
        if (blocked && impact != null)
        {
            float impactT = Mathf.Clamp01(life / flareTime);
            SetFlare(impact, length, impactSize * (1f + 0.5f * EaseOut(impactT)), reachT >= 1f ? (1f - impactT) * (1f - impactT) : 0f);
        }

        // 유지 중 빛줄기를 따라 앞으로 날아가는 빛 알갱이(파괴 광선의 속도감)
        if (moteDensity > 0f && sparks != null && reachT >= 1f && post < holdTime)
        {
            Vector2 side = new(-direction.y, direction.x);
            moteDebt += moteDensity * length * dt;
            for (; moteDebt >= 1f; moteDebt -= 1f)
            {
                float along = Random.Range(0f, length);
                float speed = Random.Range(moteSpeed.x, moteSpeed.y);
                float lifeTime = Mathf.Min(Random.Range(sparkLife.x, sparkLife.y), (length - along) / speed);
                if (lifeTime <= 0.02f) continue;
                var p = new ParticleSystem.EmitParams
                {
                    position = origin + direction * along + side * (Random.Range(-1.6f, 1.6f) * thinHalfWidth),
                    velocity = direction * speed,
                    startLifetime = lifeTime,
                    applyShapeToPosition = false,
                };
                sparks.Emit(p, 1);
            }
        }
    }

    private void SetFlare(SpriteRenderer r, float x, float size, float alpha)
    {
        if (r == null) return;
        bool show = alpha > 0.002f && size > 0f;
        if (r.enabled != show) r.enabled = show;
        if (!show) return;
        Transform t = r.transform;
        t.localPosition = new Vector3(x, 0f, 0f);
        t.localRotation = Quaternion.identity;
        float scale = size / quadSize;
        t.localScale = new Vector3(scale, scale, 1f);
        Color c = r.color;
        c.a = Mathf.Clamp01(alpha);
        r.color = c;
    }

    // PlayerLaserBeam.DrawBeam·RailgunTracer와 같은 사각형: 쏜 자리 뒤 beamMargin부터 끝 뒤 beamEndMargin까지, 높이는 번짐이 끝나는 곳 + 여유.
    private void DrawBeam(SpriteRenderer renderer, float drawLength, float halfWidth, float flashValue, float alpha, float flow)
    {
        float quadLength = beamMargin + Mathf.Max(drawLength, 0f) + beamEndMargin;
        float wide = Mathf.Max(halfWidth, 0.05f);
        float quadHeight = 2f * (wide * 1.3f + 5f * (0.14f + 0.38f * wide));
        Transform t = renderer.transform;
        t.localPosition = new Vector3(quadLength * 0.5f - beamMargin, 0f, 0f);
        t.localRotation = Quaternion.identity;
        t.localScale = new Vector3(quadLength / quadSize, quadHeight / quadSize, 1f);
        renderer.GetPropertyBlock(block);
        block.SetVector(QuadSizeId, new Vector4(quadLength, quadHeight, 0f, 0f));
        block.SetFloat(MarginId, beamMargin);
        block.SetFloat(LengthId, Mathf.Max(drawLength, 0.001f));
        block.SetFloat(HalfWidthId, Mathf.Max(halfWidth, 0.001f));
        block.SetFloat(FlowId, flow);
        block.SetFloat(FlashId, flashValue);
        block.SetFloat(AlphaId, alpha);
        renderer.SetPropertyBlock(block);
    }

    // 쏘는 순간 빛줄기를 따라 옆으로 튀는 작은 불꽃(월드 공간), 벽에 막혔으면 벽에서 뒤로 튄다
    private void EmitSparks()
    {
        if (sparks == null) return;
        sparks.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        sparks.Play();
        Vector2 side = new(-direction.y, direction.x);
        int count = Mathf.Min(maxSparks, Mathf.CeilToInt(length * sparkDensity));
        for (int i = 0; i < count; i++)
        {
            float along = Random.Range(0f, length);
            float speed = Random.Range(sparkSpeed.x, sparkSpeed.y);
            var p = new ParticleSystem.EmitParams
            {
                position = origin + direction * along + side * Random.Range(-0.04f, 0.04f),
                velocity = side * ((Random.value < 0.5f ? -1f : 1f) * speed) + direction * (Random.Range(-0.3f, 1f) * speed * 0.5f),
                startLifetime = Random.Range(sparkLife.x, sparkLife.y),
                applyShapeToPosition = false,
            };
            sparks.Emit(p, 1);
        }
        if (!blocked) return;
        Vector2 end = origin + direction * length;
        for (int i = 0; i < 5; i++)
        {
            float a = Random.Range(-70f, 70f) * Mathf.Deg2Rad;
            var p = new ParticleSystem.EmitParams
            {
                position = end,
                velocity = (-direction * Mathf.Cos(a) + side * Mathf.Sin(a)) * Random.Range(sparkSpeed.x * 1.5f, sparkSpeed.y * 1.5f),
                startLifetime = Random.Range(sparkLife.x, sparkLife.y),
                applyShapeToPosition = false,
            };
            sparks.Emit(p, 1);
        }
    }

    private static void Clear(ParticleSystem system)
    {
        if (system != null) system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
    }

    private static float AngleTo(Vector2 from, Vector2 to)
    {
        Vector2 d = to - from;
        return d.sqrMagnitude > 1e-6f ? Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg : 0f;
    }

    private static float EaseOut(float t)
    {
        t = Mathf.Clamp01(t);
        float u = 1f - t;
        return 1f - u * u * u;
    }

    private static float EaseIn(float t)
    {
        t = Mathf.Clamp01(t);
        return t * t;
    }
}
