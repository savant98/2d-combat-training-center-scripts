using System;
using System.Collections.Generic;
using UnityEngine;

// 돌진 베기(SwordDashWeaponSO)가 지나간 길에 남기는 궤적·번개·칼자국과 그 길의 판정(2026-11-17).
// 사용자 지시 "이동한 거리 사이의 공간에 다단히트(4타, 히트 판정은 한 번으로 그냥 연출로 4번 맞는 것) 데미지가 들어가",
//   "지나간 궤적을 확실히 보여 주고(이펙트가 0.5초는 남아 있어야 함), 칼자국 효과가 여러 방향(아주 약간의 텀을 두고 순차적으로)으로 추가로 나오도록".
//   2차: "데미지 틱 들어가는 속도를 빠르게, 이펙트도 더 빠르게 나타나게 해(1.5배)" — 타격 간격 0.1초 → 1/15초, 칼자국도 같은 비율로 빨라진다.
//   3차(2026-11-18): Sephiria '도'의 '특수 공격: 일섬'을 참고한 업그레이드 — 도착한 뒤 바로 베지 않고 납도(cutDelay) 뒤 '베는 순간'에
//   지나간 길의 적이 한꺼번에 베인다. 그 직전에 납도 소리(칼집에 칼이 들어가는 찰칵)를 내고, 마지막 타격은 마무리 적중 Feedback을 쓴다.
// - 돌진이 시작될 때 꺼낸다(풀). 돌진하는 동안 궤적이 Player를 따라 자란다. Player의 위치를 읽기만 하고 옮기지 않는다.
// - 돌진이 끝나면(Finish) 시작점부터 끝점까지의 상자를 물리 틱 두 번 동안만 켜서 그 안의 적을 한 번 모은다. 그 뒤로는 판정하지 않는다.
//   모인 적은 베는 순간(돌진이 끝나고 cutDelay 뒤)에 첫 타격을 받고, 그때부터 hitInterval 간격으로 hitCount번까지 맞는다(나중에 길로 들어온 적은 맞지 않는다).
//   피해·쉴드·넉백·게이지·적중 Feedback은 근접 판정(MeleeHitbox)과 같은 길을 쓴다.
// - Player의 공격이 도중에 끊겨(피격·잡기) Finish를 받지 못하면, 그때까지 지나온 길로 스스로 마무리한다.
// - 모양은 Player/SwordDashSlash 셰이더가 사각형 하나에 그리고, 이 스크립트는 값만 MaterialPropertyBlock으로 넘긴다.
//   궤적의 머리는 화면에 보이는 Player(FixedMotionBody의 보간 자세)를 따라간다. 시간은 게임 시간(Time.time)이라 히트스톱 동안 멈춘다.
//   셰이더의 _Age는 '베는 순간부터의 시간'(음수 = 돌진·납도 중)이고, _CutClock은 베는 순간의 _Clock이다.
// - 2026-11-21 무장 특성 3차 사용자 지시 "돌진 베기의 돌진에는 이제 무적": 돌진이 시작되는 Begin부터 도착(Finish)까지 Player 무적을 쥔다
//   (SwordSkillInvulnerability — 기존 IsInvincible을 쓰고 반격 성공 무적과 겹쳐도 안전). 공격이 끊겨 Track이 멈추면 스스로 Finish하며 풀고, 꺼질 때도 푼다.
[DefaultExecutionOrder(110)]   // FixedMotionPresentation(99)이 이 프레임의 보간 자세를 계산한 뒤에 읽는다
public class SwordDashSlash : WorldEntityBase
{
    private static readonly int RectId = Shader.PropertyToID("_Rect");
    private static readonly int SpanId = Shader.PropertyToID("_Span");
    private static readonly int HeadId = Shader.PropertyToID("_Head");
    private static readonly int AgeId = Shader.PropertyToID("_Age");
    private static readonly int ClockId = Shader.PropertyToID("_Clock");
    private static readonly int MarkGapId = Shader.PropertyToID("_MarkGap");
    private static readonly int CutClockId = Shader.PropertyToID("_CutClock");

    [Header("Trail")]
    [SerializeField] private SpriteRenderer trail;
    [Tooltip("그리는 사각형이 시작점 뒤·끝점 앞으로 더 나가는 거리와 위아래 반높이(칸). 비스듬한 칼자국이 잘리지 않을 만큼 둔다.")]
    [SerializeField, Min(0f)] private float padBack = 2f;
    [SerializeField, Min(0f)] private float padFront = 2.5f;
    [SerializeField, Min(0.1f)] private float halfHeight = 2.4f;
    [Tooltip("돌진이 끝난 뒤 사라질 때까지(초). 납도 시간(cutDelay) + 셰이더의 궤적이 사라지는 시각·마지막 칼자국이 사라지는 시각보다 길어야 한다.")]
    [SerializeField, Min(0.1f)] private float lifeAfterDash = 1.1f;

    [Header("Cut")]
    [Tooltip("도착한 뒤 베는 순간까지(초) — 칼을 집어넣는 납도 시간. 이 동안 번개만 지지직거리고, 베는 순간 직선이 번쩍이며 첫 타격이 들어간다. 판정 상자를 켜 두는 시간(물리 틱 두 번)보다 길어야 한다.")]
    [SerializeField, Min(0.03f)] private float cutDelay = 0.12f;
    [Tooltip("납도 소리(칼집에 칼이 들어가는 찰칵)를 베는 순간보다 이만큼 먼저 낸다(초).")]
    [SerializeField, Min(0f)] private float sheatheLead = 0.03f;

    [Header("Hit")]
    [Tooltip("모인 적이 맞는 횟수와 간격(초). 판정은 한 번만 하고 같은 적들이 이 횟수만큼 맞는다.")]
    [SerializeField, Min(1)] private int hitCount = 4;
    [SerializeField, Min(0.01f)] private float hitInterval = 1f / 15f;
    [Tooltip("타격 한 번마다 그어지는 칼자국 수. 칼자국 간격 = 타격 간격 / 이 수.")]
    [SerializeField, Min(1)] private int marksPerHit = 2;
    [Tooltip("판정 상자: 시작점 뒤·끝점 앞으로 닿는 거리와 위아래 반높이(칸).")]
    [SerializeField, Min(0f)] private float hitBack = 0.5f;
    [SerializeField, Min(0f)] private float hitFront = 1f;
    [SerializeField, Min(0.1f)] private float hitHalfHeight = 1.4f;
    [Tooltip("돌진이 이 시간(초)을 넘도록 끝나지 않으면 지나온 길로 스스로 마무리한다.")]
    [SerializeField, Min(0.05f)] private float maxDashTime = 0.6f;

    private struct Target
    {
        public EnemyHealth health;
        public Collider2D body;
    }

    private const int CollectTicks = 2;   // 판정 상자를 켜 두는 물리 틱 수

    private readonly List<Target> targets = new();
    private readonly HashSet<HealthBase> seen = new();
    private readonly SwordSkillInvulnerability invulnerability = new();   // 돌진하는 동안의 Player 무적
    private MaterialPropertyBlock block;
    private BoxCollider2D box;
    private FeedbackDataSO hitFeedback, finalHitFeedback, sheatheFeedback, cutFeedback;
    private Action<CharacterController2D, Vector2> onHitEnemy;
    private bool resolveAwarded;
    private Vector4 rect;
    private int facing = 1;
    private float plannedLength, length, head, dashAge;
    private float beginTime;               // 돌진을 시작한 시각(번개가 깜빡이는 시각의 기준)
    private float finishTime;              // 돌진이 끝난 시각
    private float firstHitTime = -1f;      // 첫 타격을 준 시각(-1 = 아직 모인 적이 없다). 뒤의 타격은 여기서부터 hitInterval 간격이다
    private int hitsGiven, collectTicks, lastTrackFrame;
    private bool running, finished, sheathed, cutPlayed;

    public bool IsRunning => running && !isReturning;
    public bool IsFinished => finished;
    public int Facing => facing;
    public float Length => length;                 // 지나온 길의 길이(칸). 돌진이 끝나면 고정된다
    public float Head => head;                     // 궤적이 그려진 곳까지의 거리(칸)
    public float Age => finished ? Time.time - finishTime : -1f;   // 돌진이 끝난 뒤 흐른 시간(초)
    public float CutDelay => cutDelay;                             // 도착 → 베는 순간(초)
    public float SheatheLead => sheatheLead;
    public bool Sheathed => sheathed;                              // 납도 소리를 냈는지
    public float FirstHitTime => firstHitTime;
    public int HitsGiven => hitsGiven;             // 지금까지 준 타격 수(모을 때의 첫 타격 포함)
    public int TargetCount => targets.Count;
    public int HitCount => hitCount;
    public float HitInterval => hitInterval;
    public float MarkGap => hitInterval / Mathf.Max(1, marksPerHit);
    public float LifeAfterDash => lifeAfterDash;
    public bool IsCollecting => box != null && box.enabled;
    public bool IsInvulnerable => invulnerability.Active;   // 돌진 무적을 쥐고 있는지(돌진 중 = true, 도착·끊김 뒤 = false)
    public Bounds HitBounds => box != null ? box.bounds : default;

    protected override void Awake()
    {
        base.Awake();
        box = col as BoxCollider2D;
        block = new MaterialPropertyBlock();   // MonoBehaviour 필드 초기화 중에는 만들 수 없다
        if (trail == null) trail = GetComponentInChildren<SpriteRenderer>(true);
        if (box != null) box.enabled = false;
    }

    public override void OnSpawnedFromPool()
    {
        base.OnSpawnedFromPool();
        targets.Clear();
        seen.Clear();
        hitFeedback = finalHitFeedback = sheatheFeedback = cutFeedback = null;
        onHitEnemy = null;
        resolveAwarded = false;
        running = finished = sheathed = cutPlayed = false;
        length = head = dashAge = 0f;
        firstHitTime = -1f;
        hitsGiven = collectTicks = 0;
        invulnerability.End();
        if (box != null) box.enabled = false;
        if (trail != null) trail.enabled = false;
    }

    // hitFeedback = 마지막이 아닌 타격, finalHitFeedback = 마지막(hitCount번째) 타격(없으면 hitFeedback), sheatheFeedback = 베기 직전의 납도 소리
    public void SetHit(FeedbackDataSO hitFeedback, FeedbackDataSO finalHitFeedback, FeedbackDataSO sheatheFeedback,
        FeedbackDataSO cutFeedback, Action<CharacterController2D, Vector2> onHitEnemy)
    {
        this.hitFeedback = hitFeedback;
        this.finalHitFeedback = finalHitFeedback != null ? finalHitFeedback : hitFeedback;
        this.sheatheFeedback = sheatheFeedback;
        this.cutFeedback = cutFeedback;
        this.onHitEnemy = onHitEnemy;
    }

    // 돌진이 시작되는 순간. 이 개체는 시작점에 놓여 있다. plannedLength = 막히지 않았을 때 나아갈 거리.
    public void Begin(int facing, float plannedLength)
    {
        this.facing = facing >= 0 ? 1 : -1;
        this.plannedLength = Mathf.Max(0.1f, plannedLength);
        running = true;
        finished = sheathed = cutPlayed = false;
        length = head = dashAge = 0f;
        firstHitTime = -1f;
        hitsGiven = 0;
        beginTime = Time.time;
        lastTrackFrame = Time.frameCount;
        if (owner is PlayerController player) invulnerability.Begin(player);   // 돌진하는 동안 무적(도착·끊김에 푼다)
        transform.localScale = new Vector3(this.facing, 1f, 1f);   // 왼쪽으로 돌진하면 그림과 판정을 함께 뒤집는다

        float width = this.plannedLength + padBack + padFront;
        rect = new Vector4(-padBack, -halfHeight, width, halfHeight * 2f);
        if (trail != null && trail.sprite != null)
        {
            Vector2 size = trail.sprite.bounds.size;
            trail.transform.localPosition = new Vector3(-padBack + width * 0.5f, 0f, 0f);
            trail.transform.localScale = new Vector3(width / size.x, halfHeight * 2f / size.y, 1f);
            trail.enabled = true;
        }
        Apply();
    }

    // 돌진하는 동안 프레임마다 불린다. 호출이 끊기면(공격이 끊김) 다음 LateUpdate에서 스스로 마무리한다.
    public void Track(Vector3 ownerPosition)
    {
        if (!running || finished) return;
        lastTrackFrame = Time.frameCount;
        invulnerability.Hold();
        Advance(ownerPosition);
    }

    // 돌진이 끝난 순간(도착). 지나온 길의 상자를 잠깐 켜서 그 안의 적을 한 번 모은다. 베는 것은 납도(cutDelay) 뒤다.
    public void Finish(Vector3 ownerPosition)
    {
        if (!running || finished) return;
        invulnerability.End();   // 도착(또는 끊김) = 돌진 끝. 납도·베기 동안은 무적이 아니다
        Advance(ownerPosition);
        finished = true;
        finishTime = Time.time;
        firstHitTime = -1f;
        hitsGiven = 0;
        if (box != null)
        {
            box.size = new Vector2(length + hitBack + hitFront, hitHalfHeight * 2f);
            box.offset = new Vector2((length + hitFront - hitBack) * 0.5f, 0f);
            collectTicks = 0;
            box.enabled = true;
        }
        Apply();
    }

    private void Advance(Vector3 ownerPosition)
    {
        float moved = (ownerPosition.x - transform.position.x) * facing;
        length = Mathf.Clamp(Mathf.Max(length, moved), 0f, plannedLength);
    }

    private void FixedUpdate()
    {
        // FixedUpdate는 그 틱의 물리 계산보다 먼저 온다. 켠 뒤 물리 계산을 CollectTicks번 지나면 끈다.
        if (box != null && box.enabled && ++collectTicks > CollectTicks) box.enabled = false;
        if (running && !finished) invulnerability.Hold();   // 물리 틱 사이에 반격 무적이 끝났어도 판정 전에 다시 켠다
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (!finished || box == null || !box.enabled) return;
        if (other.GetComponentInParent<HealthBase>() is not EnemyHealth enemy || !seen.Add(enemy)) return;
        targets.Add(new Target { health = enemy, body = other });   // 모으기만 한다. 첫 타격은 베는 순간에 준다
    }

    private void LateUpdate()
    {
        if (!running || isReturning) return;
        float dt = Time.deltaTime;
        if (!finished)
        {
            dashAge += dt;
            bool ownerGone = owner == null || !owner.gameObject.activeInHierarchy;
            if (ownerGone || Time.frameCount != lastTrackFrame || dashAge > maxDashTime)
                Finish(ownerGone ? transform.position + Vector3.right * (facing * length) : owner.transform.position);
        }
        else
        {
            // 납도: 베는 순간보다 sheatheLead 먼저 칼집에 칼이 들어가는 소리를 한 번 낸다(모인 적이 없어도).
            float cutTime = finishTime + cutDelay;
            if (!sheathed && Time.time >= cutTime - sheatheLead)
            {
                sheathed = true;
                if (sheatheFeedback != null && FeedbackPlayer.Instance != null)
                    FeedbackPlayer.Instance.Play(sheatheFeedback, owner != null ? owner.transform.position : transform.position + Vector3.right * (facing * length));
            }
            // 첫 참격음은 히트 대상 유무와 무관하게 판정과 같은 프레임에 한 번 난다.
            if (!cutPlayed && Time.time >= cutTime)
            {
                cutPlayed = true;
                if (cutFeedback != null && FeedbackPlayer.Instance != null)
                    FeedbackPlayer.Instance.Play(cutFeedback, owner != null ? owner.transform.position : transform.position);
            }
            // n번째 타격(첫 타격 = 1)은 베는 순간으로부터 (n - 1) × hitInterval 뒤에 준다. 마지막 타격은 마무리 Feedback을 쓴다. 모인 적이 없으면 타격도 없다.
            while (targets.Count > 0 && hitsGiven < hitCount && Time.time >= cutTime + hitsGiven * hitInterval)
            {
                if (firstHitTime < 0f) firstHitTime = cutTime;
                var feedback = hitsGiven == hitCount - 1 ? finalHitFeedback : hitFeedback;
                for (int i = 0; i < targets.Count; i++) Strike(targets[i], feedback);
                hitsGiven++;
            }
            if (Age >= lifeAfterDash)
            {
                running = false;
                ForceReturnToPool();
                return;
            }
        }
        UpdateHead();
        Apply();
    }

    private void Strike(Target target, FeedbackDataSO feedback)
    {
        EnemyHealth enemy = target.health;
        if (enemy == null || !enemy.isActiveAndEnabled || enemy.currentHP <= 0f) return;   // 그사이 쓰러졌다
        Vector2 point = HitPoint(target);
        var playerAttack = WeaponTraitEffects.Attack(owner);
        uint generation = playerAttack?.Traits.Generation ?? 0;
        bool applied = WeaponTraitEffects.ApplyEnemyHit(enemy, damageInfo, point);
        if (applied && !resolveAwarded && playerAttack != null && playerAttack.Traits.Generation == generation
            && owner is PlayerController player && !player.Health.IsDead)
        {
            resolveAwarded = true;
            player.AbilityManager.AttackAbility.Traits.ChangeResolve(1);
        }
        onHitEnemy?.Invoke(owner, point);
        if (feedback != null) FeedbackPlayer.Instance.Play(feedback, point);
    }

    // 맞은 자리: 적의 몸 가운데를 지나온 길의 상자 안으로 당긴 점(큰 적도 길 위에서 이펙트가 난다)
    private Vector2 HitPoint(Target target)
    {
        Vector2 center = target.body != null && target.body.gameObject.activeInHierarchy
            ? (Vector2)target.body.bounds.center : (Vector2)target.health.transform.position;
        Vector2 origin = transform.position;
        float near = origin.x - facing * hitBack, far = origin.x + facing * (length + hitFront);
        return new Vector2(Mathf.Clamp(center.x, Mathf.Min(near, far), Mathf.Max(near, far)),
            Mathf.Clamp(center.y, origin.y - hitHalfHeight, origin.y + hitHalfHeight));
    }

    // 궤적의 머리는 화면에 보이는 Player를 따라간다. 지나온 것보다 줄어들지 않고, 끝난 뒤에는 길 끝에 머문다.
    private void UpdateHead()
    {
        float limit = finished ? length : plannedLength;
        if (owner != null && owner.gameObject.activeInHierarchy && !(finished && Age > 0.05f))
        {
            FixedMotionBody.GetPresentationPose(owner.transform, out Vector3 shown, out _);
            head = Mathf.Max(head, (shown.x - transform.position.x) * facing);
        }
        else head = limit;
        head = Mathf.Clamp(head, 0f, limit);
    }

    private void Apply()
    {
        if (trail == null) return;
        trail.GetPropertyBlock(block);
        block.SetVector(RectId, rect);
        block.SetFloat(SpanId, finished ? Mathf.Max(length, 0.01f) : plannedLength);
        block.SetFloat(HeadId, head);
        block.SetFloat(AgeId, finished ? Age - cutDelay : -1f);                             // 베는 순간부터의 시간(음수 = 돌진·납도 중)
        block.SetFloat(ClockId, Time.time - beginTime);
        block.SetFloat(CutClockId, finished ? finishTime - beginTime + cutDelay : 0f);      // 베는 순간의 _Clock
        block.SetFloat(MarkGapId, MarkGap);
        trail.SetPropertyBlock(block);
    }

    private void OnDisable()
    {
        running = false;
        invulnerability.End();
        if (box != null) box.enabled = false;
        targets.Clear();
        seen.Clear();
    }
}
