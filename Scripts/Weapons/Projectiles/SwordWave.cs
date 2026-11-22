using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// 도약 무적을 관리하고, 발사된 초승달을 직선으로 보낸다.
// 첫 피해가 적용된 대상은 검기와 멀어져도 정해진 간격으로 후속 타격을 받는다.
[DefaultExecutionOrder(110)]
public sealed class SwordWave : WorldEntityBase
{
    private enum Mode { Idle, Leap, Armed, Flying, Fading, Linger }

    private static readonly int RectId = Shader.PropertyToID("_Rect");
    private static readonly int ArcId = Shader.PropertyToID("_Arc");
    private static readonly int AgeId = Shader.PropertyToID("_Age");
    private static readonly int FadeId = Shader.PropertyToID("_Fade");
    private static readonly int TrailId = Shader.PropertyToID("_Trail");
    private static readonly int EmpoweredId = Shader.PropertyToID("_Empowered");

    [Header("Look")]
    [Tooltip("초승달을 그리는 사각형(Player/SwordWave 셰이더).")]
    [SerializeField] private SpriteRenderer blade;
    [Tooltip("지나간 길에 뒤로 흩어지는 빛 조각(월드 공간, Emit만).")]
    [SerializeField] private ParticleSystem motes;
    [Tooltip("초승달 반높이(칸).")]
    [SerializeField, Min(0.5f)] private float halfHeight = 3.1f;
    [Tooltip("두 끝이 앞점보다 뒤로 처지는 거리(칸).")]
    [SerializeField, Min(0.1f)] private float tipBack = 1.55f;
    [Tooltip("가운데 두께(칸). 끝으로 갈수록 얇아진다.")]
    [SerializeField, Min(0.05f)] private float thickness = 1.08f;
    [Tooltip("초승달의 빛 번짐 여유(칸).")]
    [SerializeField, Min(0f)] private float glowPad = 0.85f;
    [Tooltip("초승달 뒤로 끌리는 빛줄의 길이(칸).")]
    [SerializeField, Min(0f)] private float trailLength = 8.4f;
    [Tooltip("강화 검기의 크기·판정 배율.")]
    [SerializeField, Min(1f)] private float empoweredScale = 1.25f;

    [Header("Flight")]
    [Tooltip("비스듬히 날아가는 속도(칸/초).")]
    [SerializeField, Min(1f)] private float airSpeed = 36f;
    [Tooltip("발사부터 옅어지기까지 나아가는 거리(칸).")]
    [SerializeField, Min(1f)] private float maxTravel = 26f;
    [Tooltip("다 날아간 뒤 옅어지는 시간(초).")]
    [SerializeField, Min(0.02f)] private float fadeTime = 0.12f;

    [Header("Hit")]
    [Tooltip("초승달 띠를 따라 적을 모으는 판정 점 수.")]
    [SerializeField, Min(2)] private int hitSamples = 5;
    [SerializeField, Min(1)] private int hits = 3;
    [SerializeField, Min(1)] private int empoweredHits = 6;
    [SerializeField, Min(0.01f)] private float hitInterval = 0.07f;
    [Tooltip("강화 검기의 첫 적중에서 돌려받는 필살 스택.")]
    [SerializeField, Min(0)] private int refundResolve = 2;

    [Header("Path Particles")]
    [SerializeField, Min(0.05f)] private float moteSpacing = 0.3f;
    [Tooltip("날기를 마친 뒤 잔여 입자가 사라질 때까지 남는 시간(초).")]
    [SerializeField, Min(0f)] private float lingerTime = 0.7f;

    private readonly Collider2D[] overlaps = new Collider2D[64];
    private readonly Dictionary<EnemyHealth, uint> targets = new();
    private readonly SwordSkillInvulnerability invulnerability = new();
    private MaterialPropertyBlock block;
    private PlayerController player;
    private PlayerWeaponTraits traits;
    private uint generation;
    private Action<CharacterController2D, Vector2> onHit;
    private LayerMask damageMask;
    private Mode mode;
    private bool empowered, refunded;
    private int facing = 1, activeSequences, heartbeat;
    private float scale = 1f, alpha;
    private float age, endStart, traveled, moteDue;
    private Vector2 pos, direction;
    private float arcR, arcPhi, arcW, arcH, arcBack;

    public bool IsLeapInvulnerable => invulnerability.Active;
    internal override bool PreserveAfterBossDefeat => player != null;
    public bool IsFlying => mode == Mode.Flying;
    public bool IsDone => mode == Mode.Linger || mode == Mode.Idle;
    public bool Empowered => empowered;
    public float Traveled => traveled;
    public float WaveHeight => arcH * 2f;
    public int TargetCount => targets.Count;
    public float HitInterval => hitInterval;
    public int HitsPerTarget => empowered ? empoweredHits : hits;
    public Vector2 Front => pos;

    private bool SameLife => player != null && player.Health != null && !player.Health.IsDead && traits != null && traits.Generation == generation;

    protected override void Awake()
    {
        base.Awake();
        block = new MaterialPropertyBlock();   // MonoBehaviour 필드 초기화 중에는 만들 수 없다
        if (blade != null) blade.enabled = false;
    }

    public override void OnSpawnedFromPool()
    {
        base.OnSpawnedFromPool();
        invulnerability.End();
        targets.Clear();
        player = null;
        traits = null;
        onHit = null;
        mode = Mode.Idle;
        empowered = refunded = false;
        activeSequences = 0;
        age = traveled = 0f;
        arcH = 0f;
        if (blade != null) blade.enabled = false;
        if (motes != null)
        {
            motes.Clear(true);
            motes.Play(true);
        }
    }

    // ================================================================= 도약
    /// <summary>기술이 시작되는 순간 부른다. 숨긴 채 Player의 도약 무적을 쥔다.</summary>
    public void BeginLeap(PlayerController owner)
    {
        if (owner == null) return;
        SetOwner(owner);
        mode = Mode.Leap;
        heartbeat = Time.frameCount;
        invulnerability.Begin(owner);
    }

    /// <summary>도약·발사 대기 동안 기술 코루틴이 매 프레임 부른다(끊김 감지). 도약 중이면 무적을 다시 확인한다.</summary>
    public void Hold()
    {
        heartbeat = Time.frameCount;
        if (mode == Mode.Leap) invulnerability.Hold();
    }

    /// <summary>도약이 끝났다(정점). 무적을 풀고 발사를 기다린다.</summary>
    public void EndLeap()
    {
        if (mode != Mode.Leap) return;
        invulnerability.End();
        mode = Mode.Armed;
        heartbeat = Time.frameCount;
    }

    private void SetOwner(PlayerController owner)
    {
        player = owner;
        traits = owner.AbilityManager != null && owner.AbilityManager.AttackAbility != null ? owner.AbilityManager.AttackAbility.Traits : null;
        generation = traits != null ? traits.Generation : 0;
    }

    /// <summary>검기 앞점을 기준으로 정해진 각도에서 지형을 통과해 날아간다.</summary>
    public void Launch(Vector2 front, int facing, float downAngle, bool empowered, WeaponBaseSO.DamageInfo info,
        LayerMask damageMask, Action<CharacterController2D, Vector2> onHitEnemy)
    {
        if (player == null && info.sourceOwner is PlayerController owner) SetOwner(owner);
        invulnerability.End();
        if (player == null) { mode = Mode.Idle; ForceReturnToPool(); return; }
        this.facing = facing >= 0 ? 1 : -1;
        this.empowered = empowered;
        this.damageMask = damageMask;
        damageInfo = info;
        onHit = onHitEnemy;
        scale = empowered ? empoweredScale : 1f;
        arcH = halfHeight * scale;
        arcBack = tipBack * scale;
        arcW = thickness * scale;
        arcR = (arcH * arcH + arcBack * arcBack) / (2f * arcBack);
        arcPhi = Mathf.Asin(Mathf.Clamp(arcH / arcR, 0f, 1f));
        alpha = -Mathf.Abs(downAngle);
        float radians = alpha * Mathf.Deg2Rad;
        direction = new Vector2(this.facing * Mathf.Cos(radians), Mathf.Sin(radians));
        pos = front;
        age = traveled = moteDue = 0f;
        refunded = false;
        targets.Clear();
        mode = Mode.Flying;
        Collect(pos);
        Render();
    }

    private void Update()
    {
        float dt = Time.deltaTime;
        switch (mode)
        {
            case Mode.Flying:
                Fly(dt);
                break;
            case Mode.Fading:
                age += dt;
                if (age - endStart >= fadeTime) StartLinger();
                break;
            case Mode.Linger:
                age += dt;
                if (age - endStart >= lingerTime && activeSequences == 0)
                {
                    mode = Mode.Idle;
                    ForceReturnToPool();
                }
                break;
        }
    }

    private void FixedUpdate()
    {
        if (mode == Mode.Leap) invulnerability.Hold();   // 물리 틱 사이에 반격 무적이 끝났어도 판정 전에 다시 켠다
    }

    private void LateUpdate()
    {
        if (isReturning) return;
        // 도약·발사 대기 중에 기술 코루틴이 멈췄다(공격이 끊김) → 무적을 풀고 사라진다.
        if ((mode == Mode.Leap || mode == Mode.Armed) && heartbeat != Time.frameCount)
        {
            invulnerability.End();
            mode = Mode.Idle;
            ForceReturnToPool();
            return;
        }
        if (mode == Mode.Leap) invulnerability.Hold();
        Render();
    }

    private void OnDisable()
    {
        invulnerability.End();
    }

    private void Fly(float dt)
    {
        if (!SameLife) { BeginFade(); return; }
        if (dt <= 0f) return;

        // 각 판정점의 직선 이동을 상자로 덮으므로 프레임 이동거리가 길어도 그 사이의 적을 검사한다.
        Vector2 previous = pos;
        float distance = Mathf.Min(airSpeed * dt, maxTravel - traveled);
        age += dt;
        traveled += distance;
        pos += direction * distance;
        RaycastMotor2D.InvalidateAttackQuerySync();
        Collect(previous);
        EmitPath(previous, distance);
        if (traveled >= maxTravel) BeginFade();
    }

    private void BeginFade()
    {
        if (!IsFlying) return;
        mode = Mode.Fading;
        endStart = age;
    }

    private void StartLinger()
    {
        mode = Mode.Linger;
        endStart = age;
        if (blade != null) blade.enabled = false;
    }

    // 초승달 띠를 따라 각 점의 이번 프레임 이동 경로를 덮는 상자로 판정한다.
    private void Collect(Vector2 previous)
    {
        if (!SameLife || player.Motor == null) return;
        int n = Mathf.Max(2, hitSamples);
        float spacing = 2f * arcR * arcPhi * 0.85f / (n - 1);
        float distance = Vector2.Distance(previous, pos);
        float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
        Vector2 center = (previous + pos) * 0.5f;
        for (int i = 0; i < n && SameLife; i++)
        {
            float theta = arcPhi * Mathf.Lerp(-0.85f, 0.85f, i / (n - 1f));
            float w = Width(theta);
            float r = arcR - w * 0.5f;
            var local = new Vector2(-arcR + r * Mathf.Cos(theta), r * Mathf.Sin(theta));
            var size = new Vector2(distance + w + 0.3f * scale, spacing * 1.2f);
            int count = player.Motor.OverlapAttackBox(center + ToWorld(local), size, angle, overlaps, damageMask);
            for (int c = 0; c < count; c++)
            {
                var collider = overlaps[c];
                overlaps[c] = null;
                if (!SameLife || collider == null) continue;
                var enemy = collider.GetComponentInParent<EnemyHealth>();
                if (enemy != null && (!targets.TryGetValue(enemy, out uint previousLife) || previousLife != enemy.LifeVersion))
                    StartCoroutine(Strike(enemy, collider, enemy.LifeVersion));
            }
        }
    }

    private IEnumerator Strike(EnemyHealth enemy, Collider2D body, uint targetLife)
    {
        Vector2 point = HitPoint(enemy, body);
        var first = damageInfo;
        first.knockbackFacing = facing;
        first.knockbackOrigin = point - new Vector2(facing * 2f, 0f);
        if (!WeaponTraitEffects.ApplyEnemyHit(enemy, first, point) || !SameLife) yield break;

        targets[enemy] = targetLife;
        if (empowered && !refunded && refundResolve > 0)
        {
            refunded = true;
            traits.ChangeResolve(refundResolve);
        }
        onHit?.Invoke(player, point);
        var cue = empowered ? WeaponTraitCue.SwordWaveHeavyHit : WeaponTraitCue.SwordWaveHit;
        WeaponTraitFeedbackSO.Play(cue, point, direction);

        // 거리·겹침을 다시 검사하지 않는다. 풀에서 재사용된 적은 이전 타격을 이어받지 않는다.
        activeSequences++;
        var followUp = damageInfo;
        followUp.knockbackFacing = 0;
        var delay = new WaitForSeconds(hitInterval);
        int total = HitsPerTarget;
        for (int i = 1; i < total; i++)
        {
            yield return delay;
            if (!SameLife || enemy == null || !enemy.isActiveAndEnabled || enemy.LifeVersion != targetLife || enemy.currentHP <= 0f)
                break;
            point = HitPoint(enemy, body);
            if (WeaponTraitEffects.ApplyEnemyHit(enemy, followUp, point))
                WeaponTraitFeedbackSO.Play(cue, point, direction);
        }
        activeSequences--;
    }

    // 맞은 자리: 적의 몸에서 검기 앞점에 가장 가까운 점(적이 밀려나도 몸을 따라간다)
    private Vector2 HitPoint(EnemyHealth enemy, Collider2D body)
    {
        if (body != null && body.enabled && body.gameObject.activeInHierarchy) return body.ClosestPoint(pos);
        return enemy != null ? (Vector2)enemy.transform.position : pos;
    }

    private void EmitPath(Vector2 from, float distance)
    {
        if (motes == null || distance <= 0f) return;
        float spacing = moteSpacing / (empowered ? 1.5f : 1f);
        Vector2 side = new Vector2(-direction.y, direction.x);
        for (; moteDue <= distance; moteDue += spacing)
        {
            float theta = arcPhi * UnityEngine.Random.Range(-0.92f, 0.92f);
            float depth = UnityEngine.Random.Range(0f, Width(theta));
            var local = new Vector2(-arcR + (arcR - depth) * Mathf.Cos(theta), (arcR - depth) * Mathf.Sin(theta));
            var particle = new ParticleSystem.EmitParams
            {
                position = World3(from + direction * moteDue + ToWorld(local)),
                velocity = -direction * UnityEngine.Random.Range(2f, 6.5f) + side * UnityEngine.Random.Range(-1.6f, 1.6f),
            };
            motes.Emit(particle, 1);
        }
        moteDue -= distance;
    }

    // ================================================================= 그리기
    private void Render()
    {
        if (mode == Mode.Idle || mode == Mode.Leap || mode == Mode.Armed || mode == Mode.Linger)
        {
            if (blade != null && blade.enabled) blade.enabled = false;
            return;
        }
        transform.SetPositionAndRotation(World3(pos), Quaternion.Euler(0f, 0f, facing * alpha));
        transform.localScale = new Vector3(facing, 1f, 1f);
        if (blade == null) return;
        float fade = mode == Mode.Fading ? 1f - Smooth01((age - endStart) / fadeTime) : 1f;
        // 초승달 둘레의 빛 번짐과 뒤쪽 잔상까지 사각형 안에 담는다.
        float front = glowPad;
        float wakeLength = trailLength * scale;
        float back = arcBack + wakeLength + glowPad;
        float half = arcH + glowPad;
        var rect = new Vector4(-back, -half, back + front, half * 2f);
        if (blade.sprite != null)
        {
            Vector2 size = blade.sprite.bounds.size;
            blade.transform.localPosition = new Vector3(rect.x + rect.z * 0.5f, 0f, 0f);
            blade.transform.localRotation = Quaternion.identity;
            blade.transform.localScale = new Vector3(rect.z / size.x, rect.w / size.y, 1f);
        }
        if (!blade.enabled) blade.enabled = true;
        blade.GetPropertyBlock(block);
        block.SetVector(RectId, rect);
        block.SetVector(ArcId, new Vector4(arcR, arcPhi, arcW, arcH));
        block.SetFloat(AgeId, age);
        block.SetFloat(FadeId, fade);
        block.SetVector(TrailId, new Vector4(wakeLength, Mathf.Min(wakeLength, traveled), 0f, 0f));
        block.SetFloat(EmpoweredId, empowered ? 1f : 0f);
        blade.SetPropertyBlock(block);
    }

    // ================================================================= 모양 계산
    // 초승달: 앞점에서 arcR 뒤의 중심을 도는 원호(바깥 가장자리). 각도 theta(0 = 앞, ±arcPhi = 두 끝)에서 두께 Width(theta).
    private float Width(float theta)
    {
        float u = theta / Mathf.Max(arcPhi, 1e-3f);
        return arcW * Mathf.Pow(Mathf.Max(0f, 1f - u * u), 0.7f);
    }

    // 오른쪽을 보는 틀의 로컬 점을 발사 각도로 돌리고 보는 방향으로 뒤집은 월드 변위
    private Vector2 ToWorld(Vector2 local)
    {
        float c = Mathf.Abs(direction.x), s = direction.y;
        return new Vector2(facing * (local.x * c - local.y * s), local.x * s + local.y * c);
    }

    private Vector3 World3(Vector2 at) => new Vector3(at.x, at.y, transform.position.z);

    private static float Smooth01(float t)
    {
        t = Mathf.Clamp01(t);
        return t * t * (3f - 2f * t);
    }
}
