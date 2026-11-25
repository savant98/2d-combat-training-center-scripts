using System;
using System.Collections.Generic;
using UnityEngine;

// 일격필살의 실행 연출. 준비 단계의 암전·발도·세계 정지는 SwordFinisherCinematic이 맡는다.
// 화면 중앙의 15도 선 이후 0.8초에 균열이 시작되고 1초 동안 자란 뒤 파쇄·화면 분단·피해가 함께 일어난다.
// 화면 판정은 기존 모터의 OverlapAttackBox를 사용한다. 메뉴 중에는 연출 시계도 멈춘다.
// 풀 반환·비활성·중단 시 화면 분할과 자신이 소유한 정지를 해제한다.
public class SwordFinisherSlash : WorldEntityBase
{
    public const int HitCount = 8;
    private const float HitInterval = .055f;

    private static readonly int RectId = Shader.PropertyToID("_Rect");
    private static readonly int SpanId = Shader.PropertyToID("_Span");
    private static readonly int FullSpanId = Shader.PropertyToID("_FullSpan");
    private static readonly int HeadId = Shader.PropertyToID("_Head");
    private static readonly int AgeId = Shader.PropertyToID("_Age");
    private static readonly int ClockId = Shader.PropertyToID("_Clock");
    private static readonly int CutClockId = Shader.PropertyToID("_CutClock");

    [Header("Trail")]
    [SerializeField] private SpriteRenderer trail;
    [Tooltip("그리는 사각형이 시작점 뒤·끝점 앞으로 더 나가는 거리와 위아래 반높이(칸). 비스듬한 칼자국·굵어진 선의 번짐이 잘리지 않을 만큼 둔다.")]
    [SerializeField, Min(0f)] private float padBack = 2f;
    [SerializeField, Min(0f)] private float padFront = 2.5f;
    [SerializeField, Min(0.1f)] private float halfHeight = 2.4f;
    [Tooltip("발도: 선의 머리가 화면 끝에서 끝까지 뻗는 시간(초, 실제 시간).")]
    [SerializeField, Min(0.01f)] private float drawTime = 0.05f;
    [Tooltip("베는 순간 뒤 사라질 때까지(초, 실제 시간). 셰이더의 궤적이 사라지는 시각·마지막 칼자국이 사라지는 시각보다 길어야 한다.")]
    [SerializeField, Min(0.1f)] private float lifeAfterCut = 1.0f;

    [Header("Line")]
    [Tooltip("가르는 선의 기울기(도). 보는 방향으로 올라간다.")]
    [SerializeField, Range(0f, 60f)] private float lineAngle = 15f;
    [Tooltip("선이 화면 가장자리 밖으로 더 나가는 거리(칸). 선 양 끝이 가늘어지는 구간이 화면 밖에 있게 한다.")]
    [SerializeField, Min(0f)] private float lineOverscan = 1.5f;
    [Tooltip("메인 카메라가 없을 때 쓰는 대체 화면의 반폭·반높이(칸, 주인 중심).")]
    [SerializeField] private Vector2 fallbackHalfSize = new Vector2(14f, 8f);

    [Header("Split")]
    [Tooltip("화면 가르기 전체 화면 재질(Hidden/2DCTC/ScreenSplit). 여기서 참조해 빌드에 셰이더가 포함된다.")]
    [SerializeField] private Material splitMaterial;
    [Tooltip("파쇄 직후 화면 조각이 끝까지 벌어지는 시간(초, 실제 시간).")]
    [SerializeField, Min(0.01f)] private float splitOpenTime = 0.09f;
    [Tooltip("벌어진 화면을 유지하는 시간(초, 실제 시간).")]
    [SerializeField, Min(0f)] private float splitHoldTime = 0.14f;
    [Tooltip("두 화면 조각이 돌아오는 시간(초, 실제 시간).")]
    [SerializeField, Min(0.01f)] private float splitCloseTime = 0.23f;
    [Tooltip("다 벌어졌을 때 틈의 반폭·두 조각이 선 방향으로 어긋난 거리·자른 면이 들쭉날쭉한 정도(화면 높이 비율).")]
    [SerializeField, Min(0f)] private float splitGap = 0.032f;
    [SerializeField, Min(0f)] private float splitSlide = 0.13f;
    [SerializeField, Min(0f)] private float splitJag = 0.0025f;

    [Header("Cut")]
    [Tooltip("첫 베기부터 유리에 균열이 생길 때까지(초, 실제 시간).")]
    [SerializeField, Min(0f)] private float crackDelay = 0.8f;
    [Tooltip("첫 베기부터 균열이 깨지고 피해가 들어갈 때까지(초, 실제 시간).")]
    [SerializeField, Min(0f)] private float cutDelay = 1.8f;

    private float SplitDuration => splitOpenTime + splitHoldTime + splitCloseTime;

    [Header("Hit")]
    [Tooltip("판정: 베는 순간 메인 카메라 화면 사각형의 네 변 밖으로 더 닿는 거리(칸). 화면에 걸친 적도 맞는다.")]
    [SerializeField, Min(0f)] private float screenMargin = 0.5f;

    private struct Target
    {
        public EnemyHealth health;
        public Collider2D body;
        public uint lifeVersion;
        public bool struck;
    }

    private readonly List<Target> targets = new();
    private readonly HashSet<HealthBase> seen = new();
    private readonly Collider2D[] overlapResults = new Collider2D[256];
    private MaterialPropertyBlock block;
    private Color trailTint;
    private FeedbackDataSO hitFeedback, crackFeedback, impactFeedback;
    private Action<CharacterController2D, Vector2> onHitEnemy;
    private LayerMask damageMask;
    private Vector4 rect;
    private Vector2 lineDirection = Vector2.right;
    private Rect hitRect;
    private int cinematicGeneration;
    private int facing = 1, beginFrame, rewardedHits = int.MaxValue, rewarded, struck;
    private float lineStart, length, head, clock;
    private int strikes;
    private bool running, drawn, cracked, splitStarted, splitEnded, cut, cancelled;

    public bool IsRunning => running && !isReturning;
    public bool IsDrawn => drawn;
    public bool IsCracked => cracked;
    public bool IsCut => cut;
    public bool IsStrikeComplete => strikes == HitCount;
    public bool WasCancelled => cancelled;
    public bool IsSplitting => splitStarted && !splitEnded;

    public int Facing => facing;
    public float LineAngle => lineAngle;
    public Vector2 LineDirection => lineDirection;    // 선 방향(월드, 길이 1)
    public float LineStart => lineStart;              // 이 개체 자리에서 선 시작점까지(칸, 선 방향 기준, 보통 음수 = 뒤쪽 화면 끝)
    public float Length => length;                    // 선 길이(칸)
    public float Head => head;                        // 선이 그어진 데까지(칸, 시작점 기준)
    public float Clock => clock;                      // 꺼낸 뒤 흐른 실제 시간(초, 일시정지에서 멈춤). 풀로 돌아간 뒤에도 마지막 값을 남긴다
    public float Age => running ? clock : -1f;
    public float DrawTime => drawTime;
    public float SplitOpenEnd => cutDelay + splitOpenTime;
    public float SplitCloseStart => cutDelay + splitOpenTime + splitHoldTime;
    public float SplitGap => splitGap;
    public float SplitSlide => splitSlide;
    public Material SplitMaterial => splitMaterial;
    public float CrackDelay => crackDelay;
    public float CutDelay => cutDelay;
    public float LifeAfterCut => lifeAfterCut;
    public float ScreenMargin => screenMargin;
    public Rect HitRect => hitRect;                   // 베는 순간 물은 판정 사각형(월드)
    public int TargetCount => targets.Count;          // 판정에 걸린 적 수
    public int StruckCount => struck;                 // 실제로 벤 적 수(막힌 적·쓰러진 적 제외)
    public int RewardedCount => rewarded;             // 게이지 보상을 낸 적 수
    public int RewardedHitLimit => rewardedHits;

    protected override void Awake()
    {
        base.Awake();
        block = new MaterialPropertyBlock();   // MonoBehaviour 필드 초기화 중에는 만들 수 없다
        if (trail == null) trail = GetComponentInChildren<SpriteRenderer>(true);
        if (trail != null) trailTint = trail.color;
    }

    public override void OnSpawnedFromPool()
    {
        base.OnSpawnedFromPool();
        ResetState();
    }

    private void ResetState()
    {
        EndSplit();
        targets.Clear();
        seen.Clear();
        hitFeedback = crackFeedback = impactFeedback = null;
        onHitEnemy = null;
        running = drawn = cracked = splitStarted = splitEnded = cut = cancelled = false;
        lineStart = length = head = clock = 0f;
        rewardedHits = int.MaxValue;
        rewarded = struck = strikes = 0;
        hitRect = default;
        if (trail != null) trail.enabled = false;
    }

    // 균열과 파쇄 효과는 각 시점에 한 번씩 재생한다. 적별 적중 효과는 피해를 줄 때 재생한다.
    public void SetHit(FeedbackDataSO hitFeedback, FeedbackDataSO crackFeedback, FeedbackDataSO impactFeedback,
        int rewardedHits, Action<CharacterController2D, Vector2> onHitEnemy)
    {
        this.hitFeedback = hitFeedback;
        this.crackFeedback = crackFeedback;
        this.impactFeedback = impactFeedback;
        this.rewardedHits = Mathf.Max(0, rewardedHits);
        this.onHitEnemy = onHitEnemy;
    }

    // 발도하는 순간. 이 개체는 선이 지나는 자리(칼 높이)에 놓여 있다(Initialize 뒤에 부른다).
    // 보는 방향으로 올라가는 선이 메인 카메라 화면을 끝에서 끝까지 지나도록 시작점·길이를 정한다.
    public void Begin(int facing, LayerMask damageMask)
    {
        this.facing = facing >= 0 ? 1 : -1;
        this.damageMask = damageMask;
        cinematicGeneration = SwordFinisherCinematic.For(owner)?.Generation ?? -1;
        float radians = lineAngle * Mathf.Deg2Rad;
        lineDirection = new Vector2(this.facing * Mathf.Cos(radians), Mathf.Sin(radians));
        // 왼쪽을 보면 그림을 좌우로 뒤집고 반대로 돌린다: 로컬 +x가 lineDirection과 같아진다(왼쪽 위).
        transform.rotation = Quaternion.Euler(0f, 0f, this.facing * lineAngle);
        transform.localScale = new Vector3(this.facing, 1f, 1f);

        Rect centerView = ScreenRect(0f);
        transform.position = new Vector3(centerView.center.x, centerView.center.y, transform.position.z);
        SwordFinisherCinematic.For(owner)?.Promote(GetComponentsInChildren<Renderer>(true));
        Vector2 origin = transform.position;
        Rect view = ScreenRect(lineOverscan);
        if (ClipLine(origin, lineDirection, view, out float near, out float far))
        {
            lineStart = near;
            length = Mathf.Max(0.1f, far - near);
        }
        else
        {
            float half = fallbackHalfSize.x / Mathf.Max(0.1f, Mathf.Cos(radians)) + lineOverscan;
            lineStart = -half;
            length = half * 2f;
        }

        head = 0f;
        clock = 0f;
        beginFrame = Time.frameCount;
        running = true;
        drawn = cracked = splitStarted = splitEnded = cut = cancelled = false;

        // 셰이더의 x(0 = 시작점)는 로컬 x - lineStart다. 사각형의 왼쪽 끝을 시작점 뒤 padBack에 둔다.
        float width = length + padBack + padFront;
        rect = new Vector4(-padBack, -halfHeight, width, halfHeight * 2f);
        if (trail != null && trail.sprite != null)
        {
            Vector2 size = trail.sprite.bounds.size;
            trail.transform.localPosition = new Vector3(lineStart - padBack + width * 0.5f, 0f, 0f);
            trail.transform.localRotation = Quaternion.identity;
            trail.transform.localScale = new Vector3(width / size.x, halfHeight * 2f / size.y, 1f);
            trail.enabled = true;
        }
        Apply();
    }

    // Player의 공격이 아직 이어지는지(PlayerLaserBeam.IsAttackAlive와 같은 기준). Player가 아닌 주인은 끊김을 보지 않는다.
    private bool IsAttackAlive()
    {
        if (owner == null || !owner.gameObject.activeInHierarchy) return false;
        if (owner is not PlayerController player) return true;
        var abilities = player.AbilityManager;
        if (abilities == null || abilities.AttackAbility == null || abilities.AttackAbility.AttackPhase == AbilityState.None) return false;
        return abilities.StateMachine.CurrentState == PlayerState.Attack;
    }

    private void LateUpdate()
    {
        if (!IsRunning) return;
        var cinematic = SwordFinisherCinematic.For(owner);
        bool ownsCinematic = cinematic != null && cinematic.IsRunning && cinematic.Generation == cinematicGeneration;
        if (!IsStrikeComplete && (!IsAttackAlive() || !ownsCinematic))
        {
            cancelled = true;
            ForceReturnToPool();
            return;
        }
        if (IsStrikeComplete && cinematic != null && cinematic.IsRunning && !ownsCinematic)
        {
            ForceReturnToPool();
            return;
        }
        if (Time.frameCount != beginFrame && !SwordFinisherCinematic.MenuBlocked && (ownsCinematic || Time.timeScale > 0f))
            clock += Time.unscaledDeltaTime;

        if (!drawn)
        {
            float u = Mathf.Clamp01(clock / drawTime);
            head = length * (1f - (1f - u) * (1f - u) * (1f - u));
            drawn = u >= 1f;
        }

        if (!cracked && clock >= crackDelay)
        {
            cracked = true;
            cinematic.Crack((Vector2)transform.position + lineDirection * lineStart, lineDirection, length);
            PlayFeedback(crackFeedback, OwnerPosition());
        }
        if (!cut)
        {
            if (cracked) cinematic.SetCrackProgress(Mathf.InverseLerp(crackDelay, cutDelay, clock));
            if (clock >= cutDelay) BreakGlass();
        }

        if (cut && !IsStrikeComplete) ApplyStrikes();

        if (IsSplitting)
        {
            if (clock >= cutDelay + SplitDuration)
                EndSplit();
            else
                ScreenSplitRenderer.UpdateState(this, SplitState());
        }
        if (IsStrikeComplete && clock >= cutDelay + lifeAfterCut)
        {
            ForceReturnToPool();
            return;
        }
        Apply();
    }

    private void BreakGlass()
    {
        cut = true;
        head = length;
        if (splitMaterial != null)
        {
            splitStarted = true;
            ScreenSplitRenderer.Begin(this, splitMaterial, SplitState());
        }
        PlayFeedback(impactFeedback, OwnerPosition());
        Collect();
    }

    private void ApplyStrikes()
    {
        while (!IsStrikeComplete && clock >= cutDelay + strikes * HitInterval)
        {
            var hit = damageInfo;
            // 누적 피해를 정수 경계에서 나눠 표시된 피해 합도 기존 총피해와 맞춘다.
            float perHit = damageInfo.damage / HitCount;
            float before = perHit * strikes;
            float after = strikes + 1 == HitCount ? damageInfo.damage : perHit * (strikes + 1);
            if (perHit >= 1f)
            {
                before = Mathf.Round(before);
                if (strikes + 1 < HitCount) after = Mathf.Round(after);
            }
            hit.damage = after - before;

            for (int i = 0; i < targets.Count; i++)
            {
                Strike(i, hit);
                if (!IsRunning) return;
            }

            strikes++;
        }

        if (IsStrikeComplete) SwordFinisherCinematic.End(owner, cinematicGeneration);
    }

    private float SplitAmount()
    {
        float sinceBreak = Mathf.Max(0f, clock - cutDelay);
        if (sinceBreak < splitOpenTime)
        {
            float rest = 1f - sinceBreak / splitOpenTime;
            return 1f - .45f * rest * rest * rest;
        }
        if (clock < SplitCloseStart) return 1f;
        float close = Mathf.Clamp01((clock - SplitCloseStart) / splitCloseTime);
        return 1f - close * close;
    }

    private ScreenSplitState SplitState()
    {
        float amount = SplitAmount();
        return new ScreenSplitState
        {
            point = transform.position,
            direction = lineDirection,
            gap = splitGap * amount,
            slide = splitSlide * amount,
            jag = splitJag * amount,
            strength = amount,
            fractureProgress = Mathf.Clamp01((clock - cutDelay) / SplitDuration),
        };
    }

    private void EndSplit()
    {
        if (splitStarted) splitEnded = true;
        ScreenSplitRenderer.End(this);
    }

    // 메인 카메라가 보는 월드 사각형을 사방으로 margin만큼 넓힌 것. 카메라가 없으면 주인 둘레의 대체 상자.
    private Rect ScreenRect(float margin)
    {
        var cam = Camera.main;
        if (cam != null)
        {
            if (cam.orthographic)
            {
                float halfH = cam.orthographicSize, halfW = halfH * cam.aspect;
                Vector2 c = cam.transform.position;
                return new Rect(c.x - halfW - margin, c.y - halfH - margin, 2f * (halfW + margin), 2f * (halfH + margin));
            }
            float depth = Mathf.Abs(transform.position.z - cam.transform.position.z);
            Vector3 a = cam.ViewportToWorldPoint(new Vector3(0f, 0f, depth));
            Vector3 b = cam.ViewportToWorldPoint(new Vector3(1f, 1f, depth));
            return Rect.MinMaxRect(Mathf.Min(a.x, b.x) - margin, Mathf.Min(a.y, b.y) - margin, Mathf.Max(a.x, b.x) + margin, Mathf.Max(a.y, b.y) + margin);
        }
        Vector2 center = OwnerPosition();
        Vector2 half = fallbackHalfSize + Vector2.one * margin;
        return new Rect(center - half, half * 2f);
    }

    // 점 origin에서 방향 direction으로 뻗은 직선이 사각형 안에 있는 구간 [near, far](direction 기준 거리)
    private static bool ClipLine(Vector2 origin, Vector2 direction, Rect area, out float near, out float far)
    {
        near = float.NegativeInfinity;
        far = float.PositiveInfinity;
        for (int axis = 0; axis < 2; axis++)
        {
            float o = origin[axis], d = direction[axis];
            float min = axis == 0 ? area.xMin : area.yMin, max = axis == 0 ? area.xMax : area.yMax;
            if (Mathf.Abs(d) < 1e-6f)
            {
                if (o < min || o > max) return false;
                continue;
            }
            float t1 = (min - o) / d, t2 = (max - o) / d;
            if (t1 > t2) (t1, t2) = (t2, t1);
            near = Mathf.Max(near, t1);
            far = Mathf.Min(far, t2);
        }
        return far > near;
    }

    // 베는 순간 한 번: 화면 사각형 안의 적을 모은다(벽 뒤 포함, 한 적의 여러 Collider는 한 번만).
    private void Collect()
    {
        hitRect = ScreenRect(screenMargin);
        if (owner == null || owner.Motor == null) return;   // 물을 모터가 없다
        RaycastMotor2D.InvalidateAttackQuerySync();         // LateUpdate에서도 이번 프레임의 위치를 보게 한다
        int count = owner.Motor.OverlapAttackBox(hitRect.center, hitRect.size, 0f, overlapResults, damageMask);
        for (int i = 0; i < count; i++)
        {
            Collider2D hit = overlapResults[i];
            overlapResults[i] = null;
            if (hit == null) continue;
            if (hit.GetComponentInParent<HealthBase>() is not EnemyHealth enemy) continue;
            if (DungeonPropTargeting.IsProp(enemy)) continue;   // 파괴형 던전 Prop은 일격필살 대상이 아니다(피해·적중 효과·게이지 환급 몫 모두 제외)
            if (!seen.Add(enemy)) continue;
            targets.Add(new Target { health = enemy, body = hit, lifeVersion = enemy.LifeVersion });
        }
    }

    private Vector3 OwnerPosition()
    {
        return owner != null && owner.gameObject.activeInHierarchy ? owner.transform.position : transform.position;
    }

    private void PlayFeedback(FeedbackDataSO feedback, Vector3 position)
    {
        if (feedback != null && FeedbackPlayer.Instance != null) FeedbackPlayer.Instance.Play(feedback, position, owner);
    }

    private void Strike(int index, WeaponBaseSO.DamageInfo hit)
    {
        var target = targets[index];
        EnemyHealth enemy = target.health;
        if (enemy == null || enemy.LifeVersion != target.lifeVersion || !enemy.isActiveAndEnabled || enemy.currentHP <= 0f) return;   // 그사이 쓰러졌다
        // 피해가 막힌 적(무적·등장 잠금)은 피해·게이지 보상·적중 효과를 모두 건너뛴다. 무적 적으로 게이지를 되찾지 않게 한다.
        if (enemy.IsDamageBlocked || (enemy.enemyShieldSystem != null && enemy.enemyShieldSystem.IsSpawnLocked)) return;
        Vector2 point = HitPoint(target);
        if (target.struck) hit.knockbackFacing = 0;
        if (enemy.enemyShieldSystem == null) enemy.TakeDamage(hit, point);
        else enemy.enemyShieldSystem.TakeDamage(hit, point);
        if (!IsRunning) return;
        PlayFeedback(hitFeedback, point);
        if (target.struck) return;
        target.struck = true;
        targets[index] = target;
        struck++;
        // 게이지 보상은 한 번 쓸 때 rewardedHits명까지만(적 수만큼 쌓여 비용이 다 돌아오지 않게)
        if (rewarded < rewardedHits)
        {
            rewarded++;
            onHitEnemy?.Invoke(owner, point);
        }
    }

    // 맞은 자리: 적의 몸 가운데를 판정 사각형(화면) 안으로 당긴 점(화면에 걸친 큰 적도 화면 안에서 이펙트가 난다)
    private Vector2 HitPoint(Target target)
    {
        Vector2 center = target.body != null && target.body.gameObject.activeInHierarchy
            ? (Vector2)target.body.bounds.center : (Vector2)target.health.transform.position;
        return new Vector2(Mathf.Clamp(center.x, hitRect.xMin, hitRect.xMax), Mathf.Clamp(center.y, hitRect.yMin, hitRect.yMax));
    }

    private void Apply()
    {
        if (trail == null) return;
        float age = running ? clock : 0f;
        // 첫 베기 자국을 균열이 생기기 전에 걷어 내고, 마지막 참격에서 다시 밝힌다.
        float fadeStart = drawTime + .1f;
        float fadeEnd = Mathf.Max(fadeStart + .05f, crackDelay * .91f);
        Color tint = trailTint;
        if (!cut) tint.a *= 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(fadeStart, fadeEnd, age));
        trail.color = tint;
        trail.enabled = tint.a > .001f;
        trail.GetPropertyBlock(block);
        block.SetVector(RectId, rect);
        block.SetFloat(SpanId, Mathf.Max(length, 0.01f));
        block.SetFloat(FullSpanId, Mathf.Max(length, 0.01f));   // 칼자국을 선 전체에 고르게 나눈다
        block.SetFloat(HeadId, head);
        block.SetFloat(AgeId, age - cutDelay);   // 베는 순간부터의 시간(음수 = 발도·가르기)
        block.SetFloat(ClockId, age);
        block.SetFloat(CutClockId, cutDelay);    // 베는 순간의 _Clock
        trail.SetPropertyBlock(block);
    }

    public override void ForceReturnToPool()
    {
        running = false;
        EndSplit();
        if (!IsStrikeComplete) SwordFinisherCinematic.End(owner, cinematicGeneration);
        base.ForceReturnToPool();
    }

    private void OnDisable()
    {
        running = false;
        EndSplit();
        if (!IsStrikeComplete) SwordFinisherCinematic.End(owner, cinematicGeneration);
        targets.Clear();
        seen.Clear();
    }
}
