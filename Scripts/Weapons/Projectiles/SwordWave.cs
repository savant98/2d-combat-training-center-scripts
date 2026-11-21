using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// 검기방출(SwordWaveWeaponSO)의 초승달 검기(2026-11-21 무장 특성 3차 요청으로 전면 개편).
// 사용자 요청 요지: "검기방출의 animation, effect, sound, 공격 범위까지 모두 바꿔야 해. 게임 엘소드의 레이븐이 쓰는 스킬 쇼크웨이브를 참고.
//   공중에서 검기를 날리는데 크기는 지금보다 더 커야 해." 참고 — 쇼크웨이브 디바이더: 내리긋는 참격이 큰 충격파를 아래 대각선으로 화면 밖까지 보낸다.
// 흐름
// - 도약(Leap): 기술이 시작되면 풀에서 꺼내 숨긴 채 Player의 도약 무적을 쥔다(SwordSkillInvulnerability). 기술 코루틴이 매 프레임 Hold로
//   심장 박동을 주고, 공격이 끊기면(피격·잡기의 StopAllCoroutines) LateUpdate가 알아채 무적을 풀고 풀로 돌아간다.
//   도약이 끝나면 EndLeap(무적 해제) → 발사 대기(Armed, 심장 박동은 계속).
// - 발사(Launch): 앞 아래 대각선으로 airSpeed로 날아간다(Air). 초승달 아래 끝이 바닥(platformLayer — 통과 발판은 지나간다)에 닿으면
//   SwordWaveLand 큐를 내고 바닥을 따라 slideSpeed로 미끄러진다(Slide, turnTime 동안 똑바로 선다). 낭떠러지에서는 다시 비스듬히 떨어진다.
//   벽(앞점이 지나는 길, 미끄러질 때는 바닥 위 세 높이)에 막히면 그 자리에서 터진다(SwordWaveLand 큐를 벽 바깥쪽으로). maxTravel을 다 가면 옅어진다.
// - 판정: 초승달 띠 위 hitSamples개 점이 이번 이동에서 지나간 길을 덮는 상자들(Player 모터의 OverlapAttackBox)로 적을 모은다.
//   한 적은 한 번만 모이고, 모인 순간부터 hitInterval 간격으로 hits타(강화 empoweredHits타)를 맞는다(옛 검기의 다단히트 규칙 그대로).
//   첫 타는 검기가 나아가는 쪽으로 민다(무기의 knockbackPower). 강화 검기가 처음 적중하면 필살 refundResolve스택을 한 번 돌려받는다(옛 규칙 그대로).
//   Player가 쓰러지거나 무장 특성이 초기화되면(PlayerWeaponTraits.Generation) 판정을 멈추고 옅어진다.
// - 모양은 Player/SwordWave 셰이더가 사각형 하나에 그린다(두꺼운 흰 심 + 청록·분홍 가장자리 + 빛 번짐 + 번개 + 뒤로 끌리는 빛줄 + 바닥 빛).
//   이 스크립트는 모양 값을 MaterialPropertyBlock으로 넘긴다. 시간 값은 발사 뒤 나이(_Age, 게임 시간)라 프레임과 무관하고 히트스톱 동안 멈춘다.
//   지나간 길에는 빛 조각(motes), 미끄러지는 동안 바닥 불꽃(groundSparks)·먼지(groundDust)를 Emit으로 뿌린다(월드 공간).
//   날기를 마친 뒤에도 그 입자가 사라질 때까지(lingerTime) 개체를 남기고(그림은 끈다), 다단히트가 끝나야 풀로 돌아간다.
// - 위치는 이 개체(캐릭터가 아닌 연출·판정 개체) 자신만 옮긴다. Player·적의 위치는 바꾸지 않는다. 물리 질의는 모두 Player 모터의 래퍼를 쓴다.
// - 좌표: 루트 = 초승달 앞점(가운데 줄의 가장 앞). 로컬 +x = 나아가는 쪽. 오른쪽을 보는 틀의 각도 alpha(아래로 = 음수)로 돌리고,
//   왼쪽을 보면 X 배율 -1로 뒤집는다(그래서 로컬 -y는 늘 바닥 쪽이다).
[DefaultExecutionOrder(110)]
public sealed class SwordWave : WorldEntityBase
{
    private enum Mode { Idle, Leap, Armed, Air, Slide, Fading, Bursting, Linger }

    private static readonly int RectId = Shader.PropertyToID("_Rect");
    private static readonly int ArcId = Shader.PropertyToID("_Arc");
    private static readonly int AgeId = Shader.PropertyToID("_Age");
    private static readonly int FadeId = Shader.PropertyToID("_Fade");
    private static readonly int BurstId = Shader.PropertyToID("_Burst");
    private static readonly int SlideId = Shader.PropertyToID("_Slide");
    private static readonly int GroundId = Shader.PropertyToID("_Ground");
    private static readonly int EmpoweredId = Shader.PropertyToID("_Empowered");

    [Header("Look")]
    [Tooltip("초승달을 그리는 사각형(Player/SwordWave 셰이더). 루트의 자식이고, 위치·배율은 이 스크립트가 모양에 맞춘다.")]
    [SerializeField] private SpriteRenderer blade;
    [Tooltip("지나간 길에 뒤로 흩어지는 빛 조각(월드 공간, Emit만).")]
    [SerializeField] private ParticleSystem motes;
    [Tooltip("바닥을 미끄러지는 동안 닿은 자리에서 튀는 불꽃(월드 공간, Emit만).")]
    [SerializeField] private ParticleSystem groundSparks;
    [Tooltip("바닥을 미끄러지는 동안 닿은 자리에서 이는 먼지(월드 공간, Emit만).")]
    [SerializeField] private ParticleSystem groundDust;
    [Tooltip("초승달 반높이(칸). 높이는 2배(2026-11-21 지시 '지금보다 더 크게' — 옛 검기 약 3.4칸).")]
    [SerializeField, Min(0.5f)] private float halfHeight = 2.3f;
    [Tooltip("두 끝이 앞점보다 뒤로 처지는 거리(칸). 클수록 많이 휜다.")]
    [SerializeField, Min(0.1f)] private float tipBack = 1.15f;
    [Tooltip("가운데 두께(칸). 끝으로 갈수록 얇아진다.")]
    [SerializeField, Min(0.05f)] private float thickness = 0.8f;
    [Tooltip("사각형이 초승달 바깥으로 더 나가는 여유(빛 번짐, 칸).")]
    [SerializeField, Min(0f)] private float glowPad = 0.7f;
    [Tooltip("초승달 뒤로 끌리는 빛줄의 길이(칸, 사각형 여유).")]
    [SerializeField, Min(0f)] private float trailLength = 2.6f;
    [Tooltip("강화 검기(필살 소비)의 크기·판정 배율.")]
    [SerializeField, Min(1f)] private float empoweredScale = 1.25f;

    [Header("Flight")]
    [Tooltip("비스듬히 날아가는 속도(칸/초).")]
    [SerializeField, Min(1f)] private float airSpeed = 36f;
    [Tooltip("바닥을 미끄러지는 속도(칸/초).")]
    [SerializeField, Min(1f)] private float slideSpeed = 30f;
    [Tooltip("발사부터 사라질 때까지 나아가는 길이(칸). 화면 밖으로 나간 뒤 조금 더 간다.")]
    [SerializeField, Min(1f)] private float maxTravel = 26f;
    [Tooltip("바닥에 닿아 똑바로 서거나 낭떠러지에서 다시 기우는 시간(초).")]
    [SerializeField, Min(0.01f)] private float turnTime = 0.07f;
    [Tooltip("미끄러질 때 아래 끝이 바닥 속으로 묻히는 깊이(칸) — 바닥 빛이 바닥을 가르는 것처럼 보이게.")]
    [SerializeField, Min(0f)] private float groundSink = 0.15f;
    [Tooltip("미끄러지며 오르는 턱의 최대 높이와 따라 내려가는 최대 높이(칸). 더 높은 턱은 벽으로 보고 터진다.")]
    [SerializeField, Min(0.1f)] private float stepUp = 0.9f;
    [SerializeField, Min(0.1f)] private float stepDown = 1.1f;
    [Tooltip("다 날아간 뒤 옅어지는 시간과 벽에 막혀 터지는 시간(초).")]
    [SerializeField, Min(0.02f)] private float fadeTime = 0.12f;
    [SerializeField, Min(0.02f)] private float burstTime = 0.16f;

    [Header("Hit")]
    [Tooltip("판정 점 수(초승달 띠를 따라). 점마다 이번 이동의 길을 덮는 상자 하나.")]
    [SerializeField, Min(2)] private int hitSamples = 5;
    [Tooltip("한 적이 맞는 횟수(일반·강화)와 간격(초). 옛 검기와 같다.")]
    [SerializeField, Min(1)] private int hits = 3;
    [SerializeField, Min(1)] private int empoweredHits = 6;
    [SerializeField, Min(0.01f)] private float hitInterval = 0.07f;
    [Tooltip("강화 검기가 처음 적중하면 돌려받는 필살 스택(한 번만).")]
    [SerializeField, Min(0)] private int refundResolve = 2;

    [Header("Path Particles")]
    [Tooltip("빛 조각·바닥 불꽃·먼지를 뿌리는 간격(나아간 칸).")]
    [SerializeField, Min(0.05f)] private float moteSpacing = 0.3f;
    [SerializeField, Min(0.05f)] private float sparkSpacing = 0.28f;
    [SerializeField, Min(0.05f)] private float dustSpacing = 0.6f;
    [Tooltip("날기를 마친 뒤 뿌린 입자가 사라질 때까지 개체를 남기는 시간(초). 입자 수명보다 길어야 한다.")]
    [SerializeField, Min(0f)] private float lingerTime = 0.7f;

    private const float MaxStep = 0.45f;      // 한 번에 나아가는 최대 거리(칸) — 빠른 검기도 지형·판정을 건너뛰지 않게 나눠 간다
    private const float FloorNormal = 0.55f;  // 이보다 위를 향한 면 = 바닥

    private readonly Collider2D[] overlaps = new Collider2D[64];
    private readonly HashSet<EnemyHealth> targets = new();
    private readonly SwordSkillInvulnerability invulnerability = new();
    private MaterialPropertyBlock block;
    private PlayerController player;
    private PlayerWeaponTraits traits;
    private uint generation;
    private Action<CharacterController2D, Vector2> onHit;
    private LayerMask damageMask, platformLayer;
    private Mode mode;
    private bool empowered, refunded;
    private int facing = 1, activeSequences, heartbeat;
    private float scale = 1f;
    private float alpha, airAlpha, turnFrom, turnTo, turnStart = -10f;
    private float age, endStart, groundY, traveled, slideLook;
    private float landFromY, landStart = -10f;   // 바닥에 앉을 때 앞점 높이를 RestHeight까지 turnTime 동안 옮긴다(앞점이 먼저 닿으면 1칸 넘게 튀지 않게)
    private float moteDue, sparkDue, dustDue;
    private Vector2 pos;
    private float arcR, arcPhi, arcW, arcH, arcBack;   // 지금 초승달 모양(칸, 배율 반영)

    /// <summary>도약 무적을 쥐고 있는지(도약하는 0.2초 동안 true).</summary>
    public bool IsLeapInvulnerable => invulnerability.Active;
    public bool IsFlying => mode == Mode.Air || mode == Mode.Slide;
    public bool IsSliding => mode == Mode.Slide;
    public bool IsDone => mode == Mode.Linger || mode == Mode.Idle;
    public bool Exploded => endedByBurst;   // 벽·턱에 막혀 터졌는지
    public bool Empowered => empowered;
    public float Traveled => traveled;
    public float WaveHeight => arcH * 2f;
    public int TargetCount => targets.Count;
    public float HitInterval => hitInterval;
    public int HitsPerTarget => empowered ? empoweredHits : hits;
    public Vector2 Front => pos;

    private bool endedByBurst;
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
        empowered = refunded = endedByBurst = false;
        activeSequences = 0;
        age = traveled = slideLook = 0f;
        if (blade != null) blade.enabled = false;
        foreach (var system in new[] { motes, groundSparks, groundDust })
        {
            if (system == null) continue;
            system.Clear(true);
            system.Play(true);
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

    // ================================================================= 발사
    /// <summary>
    /// 검기를 내보낸다. from = 몸 중심(앞점까지 벽이 있으면 그 자리에서 터진다), front = 초승달 앞점, downAngle = 수평에서 아래로 기운 각도(도).
    /// </summary>
    public void Launch(Vector2 from, Vector2 front, int facing, float downAngle, bool empowered, WeaponBaseSO.DamageInfo info,
        LayerMask damageMask, LayerMask platformLayer, Action<CharacterController2D, Vector2> onHitEnemy)
    {
        if (player == null && info.sourceOwner is PlayerController owner) SetOwner(owner);
        invulnerability.End();
        if (player == null) { mode = Mode.Idle; ForceReturnToPool(); return; }
        this.facing = facing >= 0 ? 1 : -1;
        this.empowered = empowered;
        this.damageMask = damageMask;
        this.platformLayer = platformLayer;
        damageInfo = info;
        onHit = onHitEnemy;
        scale = empowered ? empoweredScale : 1f;
        arcH = halfHeight * scale;
        arcBack = tipBack * scale;
        arcW = thickness * scale;
        arcR = (arcH * arcH + arcBack * arcBack) / (2f * arcBack);
        arcPhi = Mathf.Asin(Mathf.Clamp(arcH / arcR, 0f, 1f));
        airAlpha = -Mathf.Abs(downAngle);
        alpha = turnFrom = turnTo = airAlpha;
        turnStart = -10f;
        age = traveled = slideLook = 0f;
        moteDue = 0f;
        sparkDue = dustDue = 0f;
        refunded = endedByBurst = false;
        targets.Clear();
        mode = Mode.Air;
        if (blade != null) blade.enabled = true;

        // 몸 중심에서 앞점까지 벽이 있으면 그 자리에서 터진다(벽에 붙어서 쓴 경우).
        Vector2 reach = front - from;
        float reachLength = reach.magnitude;
        if (reachLength > 0.01f)
        {
            var wall = Ray(from, reach / reachLength, reachLength);
            if (wall.collider != null)
            {
                pos = wall.point - reach / reachLength * 0.05f;
                Collect(pos, alpha);
                Explode(wall.point, wall.normal);
                Render();
                return;
            }
        }
        pos = front;
        // 낮은 자리(땅 가까이·낮은 천장 아래)에서 쏘면 아래 끝이 이미 바닥 밑이다 → 그 바닥에서 바로 미끄러진다.
        Vector2 tip = pos + ToWorld(LowTipLocal, alpha);
        var floor = Ray(new Vector2(tip.x, pos.y), Vector2.down, Mathf.Max(0f, pos.y - tip.y) + 0.05f);
        if (floor.collider != null && floor.normal.y >= FloorNormal && floor.distance > 0.001f) Land(floor.point.y);
        Collect(pos, alpha);
        Render();
    }

    // ================================================================= 매 프레임
    private void Update()
    {
        float dt = Time.deltaTime;
        switch (mode)
        {
            case Mode.Air:
            case Mode.Slide:
                Fly(dt);
                break;
            case Mode.Fading:
            case Mode.Bursting:
                age += dt;
                if (age - endStart >= (mode == Mode.Fading ? fadeTime : burstTime)) StartLinger();
                break;
            case Mode.Linger:
                age += dt;
                if (age - endStart >= lingerTime && activeSequences <= 0)
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
        float speed = mode == Mode.Air ? airSpeed : slideSpeed;
        float distance = speed * dt;
        int steps = Mathf.Max(1, Mathf.CeilToInt(distance / MaxStep));
        float d = distance / steps, sub = dt / steps;
        RaycastMotor2D.InvalidateAttackQuerySync();   // 이번 프레임 적의 위치로 판정한다
        for (int i = 0; i < steps && IsFlying; i++)
        {
            age += sub;
            Step(d);
        }
        if (IsFlying && traveled >= maxTravel) BeginFade();
        float lookTarget = mode == Mode.Slide ? 1f : 0f;
        slideLook = Mathf.MoveTowards(slideLook, lookTarget, dt / 0.08f);
    }

    // 한 걸음(d칸). 지형에 닿으면 바닥에 내려앉거나(Land) 터진다(Explode).
    private void Step(float d)
    {
        Vector2 prevPos = pos;
        float prevAlpha = alpha;
        UpdateTurn();
        if (mode == Mode.Air)
        {
            Vector2 dir = Direction(airAlpha);
            // 앞점의 길: 벽이면 터지고, 바닥이면(가파른 내리막 등 아래 끝보다 먼저 닿음) 내려앉는다.
            var front = Ray(pos, dir, d + 0.05f);
            if (front.collider != null && front.normal.y < FloorNormal)
            {
                Advance(dir * Mathf.Max(0f, front.distance - 0.05f));
                Collect(prevPos, prevAlpha);
                Explode(front.point, front.normal);
                return;
            }
            // 아래 끝의 길: 바닥에 닿으면 미끄러지기 시작한다.
            // 낭떠러지에서 막 떨어지기 시작한 걸음에는 아래 끝이 아직 떠나온 발판 속(바닥에 묻힌 깊이)에 있다 — 지형 속에서 시작한 광선(거리 0)은 무시한다.
            Vector2 tip = pos + ToWorld(LowTipLocal, alpha);
            var ground = Ray(tip, dir, d + 0.02f);
            if (ground.collider != null && ground.distance > 0.001f)
            {
                Advance(dir * Mathf.Max(0f, ground.distance));
                Collect(prevPos, prevAlpha);
                if (ground.normal.y >= FloorNormal) { Land(ground.point.y); return; }
                // 아래 끝이 낮은 턱의 옆면에 닿았다: 턱 윗면이 stepUp 안이면 그 위에 내려앉고, 아니면 터진다.
                var top = Ray(ground.point + new Vector2(facing * 0.06f, stepUp + 0.05f), Vector2.down, stepUp + 0.1f);
                if (top.collider != null && top.normal.y >= FloorNormal && top.distance > 0.001f) Land(top.point.y);
                else Explode(ground.point, ground.normal);
                return;
            }
            if (front.collider != null)
            {
                Advance(dir * Mathf.Max(0f, front.distance - 0.02f));
                Collect(prevPos, prevAlpha);
                Land(front.point.y);
                return;
            }
            Advance(dir * d);
        }
        else
        {
            Vector2 dir = new Vector2(facing, 0f);
            // 벽: 바닥 위 세 높이. 가장 낮은 줄(stepUp 바로 위)보다 낮은 턱은 아래의 바닥 따라가기가 오른다.
            if (WallAhead(dir, d, stepUp + 0.05f, prevPos, prevAlpha) || WallAhead(dir, d, arcH, prevPos, prevAlpha)
                || WallAhead(dir, d, arcH * 1.7f, prevPos, prevAlpha))
                return;
            float nextX = pos.x + facing * d;
            var floor = Ray(new Vector2(nextX, groundY + stepUp + 0.05f), Vector2.down, stepUp + stepDown + 0.05f);
            traveled += d;
            pos.x = nextX;
            if (floor.collider != null && floor.normal.y >= FloorNormal && floor.distance > 0.001f)
                groundY = floor.point.y;
            else if (floor.collider != null)
            {
                // 바닥 조사 줄이 지형 속에서 시작했다(가장 낮은 벽 줄과 바닥 사이의 턱) — 벽으로 본다.
                Collect(prevPos, prevAlpha);
                Explode(new Vector2(pos.x, groundY + stepUp * 0.5f), new Vector2(-facing, 0f));
                return;
            }
            else
            {
                // 낭떠러지: 다시 비스듬히 떨어진다.
                mode = Mode.Air;
                StartTurn(airAlpha);
            }
            if (mode == Mode.Slide)
            {
                float rest = groundY + RestHeight(alpha);
                float k = Mathf.Clamp01((age - landStart) / turnTime);
                pos.y = k < 1f ? Mathf.Lerp(landFromY, rest, k * k * (3f - 2f * k)) : rest;
            }
        }
        Collect(prevPos, prevAlpha);
        EmitPath(prevPos);
    }

    private bool WallAhead(Vector2 dir, float d, float height, Vector2 prevPos, float prevAlpha)
    {
        var wall = Ray(new Vector2(pos.x - facing * 0.02f, groundY + height), dir, d + 0.05f);
        if (wall.collider == null || Mathf.Abs(wall.normal.x) <= 0.5f) return false;
        float reach = Mathf.Max(0f, wall.distance - 0.07f);
        traveled += reach;
        pos.x += facing * reach;
        Collect(prevPos, prevAlpha);
        Explode(wall.point, wall.normal);
        return true;
    }

    private void Advance(Vector2 delta)
    {
        pos += delta;
        traveled += delta.magnitude;
    }

    // 바닥(높이 y)에 내려앉아 미끄러지기 시작한다. 닿은 자리에서 SwordWaveLand(바닥 = +y)를 낸다.
    // 앞점 높이는 지금 높이에서 RestHeight까지 turnTime 동안 옮긴다(아래 끝보다 앞점이 먼저 높은 턱 윗면에 닿은 경우에도 한 프레임에 튀지 않는다).
    // 효과 크기는 피드백의 입자 Prefab이 정한다(표 size 0 — 강화 검기도 같은 착지 효과).
    private void Land(float y)
    {
        groundY = y;
        mode = Mode.Slide;
        StartTurn(0f);
        landFromY = pos.y;
        landStart = age;
        Vector2 contact = pos + ToWorld(LowTipLocal, alpha);
        contact.y = y;
        WeaponTraitFeedbackSO.Play(WeaponTraitCue.SwordWaveLand, contact, Vector2.right);
    }

    // 벽(또는 오를 수 없는 턱)에 막혀 그 자리에서 터진다. 효과의 +y가 막힌 면의 바깥을 향하게 돌린다.
    private void Explode(Vector2 point, Vector2 normal)
    {
        if (normal.sqrMagnitude < 0.01f) normal = new Vector2(-facing, 0f);
        mode = Mode.Bursting;
        endedByBurst = true;
        endStart = age;
        WeaponTraitFeedbackSO.Play(WeaponTraitCue.SwordWaveLand, point, new Vector2(normal.y, -normal.x));
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

    private void StartTurn(float target)
    {
        turnFrom = alpha;
        turnTo = target;
        turnStart = age;
    }

    private void UpdateTurn()
    {
        float k = Mathf.Clamp01((age - turnStart) / turnTime);
        alpha = Mathf.LerpAngle(turnFrom, turnTo, k * k * (3f - 2f * k));
    }

    // ================================================================= 판정
    // 초승달 띠 위 hitSamples개 점마다, 지난 자세에서 지금 자세까지 그 점이 지나간 길을 덮는 상자로 적을 모은다.
    private void Collect(Vector2 prevPos, float prevAlpha)
    {
        if (!SameLife || player.Motor == null) return;
        int n = Mathf.Max(2, hitSamples);
        float spacing = 2f * arcR * arcPhi * 0.85f / (n - 1);
        for (int i = 0; i < n; i++)
        {
            float theta = arcPhi * Mathf.Lerp(-0.85f, 0.85f, i / (n - 1f));
            float w = Width(theta);
            float r = arcR - w * 0.5f;
            var local = new Vector2(-arcR + r * Mathf.Cos(theta), r * Mathf.Sin(theta));
            Vector2 a = prevPos + ToWorld(local, prevAlpha), b = pos + ToWorld(local, alpha);
            Vector2 path = b - a;
            float length = path.magnitude;
            Vector2 travel = Direction(alpha);
            float angle = length > 0.01f ? Mathf.Atan2(path.y, path.x) * Mathf.Rad2Deg : Mathf.Atan2(travel.y, travel.x) * Mathf.Rad2Deg;
            var size = new Vector2(length + w + 0.3f * scale, spacing * 1.2f);
            int count = player.Motor.OverlapAttackBox((a + b) * 0.5f, size, angle, overlaps, damageMask);
            for (int c = 0; c < count && SameLife; c++)
            {
                var collider = overlaps[c];
                overlaps[c] = null;
                var enemy = collider != null ? collider.GetComponentInParent<EnemyHealth>() : null;
                if (enemy != null && !targets.Contains(enemy))
                    StartCoroutine(Strike(enemy, collider));
            }
        }
    }

    private IEnumerator Strike(EnemyHealth enemy, Collider2D body)
    {
        Vector2 point = HitPoint(enemy, body);
        // 첫 타는 검기가 나아가는 쪽으로 민다(밀리는 방향 = 적 위치 - 출처). 뒤의 타는 밀지 않는다.
        var first = damageInfo;
        first.knockbackFacing = facing;
        first.knockbackOrigin = point - new Vector2(facing * 2f, 0f);
        if (!WeaponTraitEffects.ApplyEnemyHit(enemy, first, point) || !SameLife) yield break;
        targets.Add(enemy);
        if (empowered && !refunded && refundResolve > 0)
        {
            refunded = true;
            traits.ChangeResolve(refundResolve);
        }
        onHit?.Invoke(player, point);
        var cue = empowered ? WeaponTraitCue.SwordWaveHeavyHit : WeaponTraitCue.SwordWaveHit;
        // 효과의 +x = 검기가 공중에서 내려오는 방향(오른쪽 '\', 왼쪽 '/'). 칼자국 층은 로컬 0도라 이 방향을 따른다.
        var axis = Direction(airAlpha);
        WeaponTraitFeedbackSO.Play(cue, point, axis);
        activeSequences++;
        var followUp = damageInfo;
        followUp.knockbackFacing = 0;
        var delay = new WaitForSeconds(hitInterval);
        int total = empowered ? empoweredHits : hits;
        for (int i = 1; i < total; i++)
        {
            yield return delay;
            if (!SameLife) break;
            point = HitPoint(enemy, body);
            if (!WeaponTraitEffects.ApplyEnemyHit(enemy, followUp, point) || !SameLife) break;
            WeaponTraitFeedbackSO.Play(cue, point, axis);
        }
        activeSequences--;
    }

    // 맞은 자리: 적의 몸에서 검기 앞점에 가장 가까운 점(적이 밀려나도 몸을 따라간다)
    private Vector2 HitPoint(EnemyHealth enemy, Collider2D body)
    {
        if (body != null && body.enabled && body.gameObject.activeInHierarchy) return body.ClosestPoint(pos);
        return enemy != null ? (Vector2)enemy.transform.position : pos;
    }

    // ================================================================= 길의 입자
    private void EmitPath(Vector2 prevPos)
    {
        float moved = (pos - prevPos).magnitude;
        if (moved <= 1e-4f) return;
        Vector2 travel = Direction(alpha);
        Vector2 side = new Vector2(-travel.y, travel.x);
        if (motes != null)
        {
            float spacing = moteSpacing / (empowered ? 1.5f : 1f);
            for (moteDue -= moved; moteDue <= 0f; moteDue += spacing)
            {
                float theta = arcPhi * UnityEngine.Random.Range(-0.92f, 0.92f);
                float depth = UnityEngine.Random.Range(0f, Width(theta));
                var local = new Vector2(-arcR + (arcR - depth) * Mathf.Cos(theta), (arcR - depth) * Mathf.Sin(theta));
                var p = new ParticleSystem.EmitParams
                {
                    position = World3(pos + ToWorld(local, alpha)),
                    velocity = -travel * UnityEngine.Random.Range(2f, 6.5f) + side * UnityEngine.Random.Range(-1.6f, 1.6f),
                };
                motes.Emit(p, 1);
            }
        }
        if (mode != Mode.Slide) return;
        Vector2 contact = pos + ToWorld(LowTipLocal, alpha);
        contact.y = groundY + 0.04f;
        if (groundSparks != null)
        {
            for (sparkDue -= moved; sparkDue <= 0f; sparkDue += sparkSpacing)
            {
                var p = new ParticleSystem.EmitParams
                {
                    position = World3(contact + new Vector2(UnityEngine.Random.Range(-0.3f, 0.3f), 0f)),
                    velocity = new Vector2(-facing * UnityEngine.Random.Range(1.5f, 7f), UnityEngine.Random.Range(2.5f, 7.5f)),
                };
                groundSparks.Emit(p, 1);
            }
        }
        if (groundDust != null)
        {
            for (dustDue -= moved; dustDue <= 0f; dustDue += dustSpacing)
            {
                var p = new ParticleSystem.EmitParams
                {
                    position = World3(contact + new Vector2(UnityEngine.Random.Range(-0.4f, 0.2f), 0.1f)),
                    velocity = new Vector2(-facing * UnityEngine.Random.Range(0.4f, 1.8f), UnityEngine.Random.Range(0.2f, 0.9f)),
                };
                groundDust.Emit(p, 1);
            }
        }
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
        float burst = mode == Mode.Bursting ? Mathf.Clamp01((age - endStart) / burstTime) : 0f;
        float fade = mode == Mode.Fading ? 1f - Smooth01((age - endStart) / fadeTime)
            : mode == Mode.Bursting ? 1f - Smooth01(burst) : 1f;
        // 사각형: 앞점(0) 앞으로 glowPad, 뒤로 끝이 처진 거리 + 빛줄, 위아래로 반높이 + glowPad. 터질 때는 1.35배까지 벌어진다.
        float grow = 1f + 0.35f * burst;
        float front = glowPad * grow;
        float back = (arcBack + trailLength * scale + glowPad) * grow;
        float half = (arcH + glowPad) * grow;
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
        block.SetFloat(BurstId, burst);
        block.SetFloat(SlideId, slideLook);
        block.SetFloat(GroundId, mode == Mode.Slide || slideLook > 0f ? groundY - pos.y : -100f);
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

    private Vector2 LowTipLocal => new Vector2(-arcBack, -arcH);

    // 미끄러질 때 앞점이 바닥에서 떠 있는 높이: 아래 끝이 바닥에 groundSink만큼 묻힌다.
    private float RestHeight(float a) => -ToWorld(LowTipLocal, a).y - groundSink * scale;

    private Vector2 Direction(float a)
    {
        float r = a * Mathf.Deg2Rad;
        return new Vector2(facing * Mathf.Cos(r), Mathf.Sin(r));
    }

    // 오른쪽을 보는 틀의 로컬 점을 각도 a로 돌리고 보는 방향으로 뒤집은 월드 변위
    private Vector2 ToWorld(Vector2 local, float a)
    {
        float r = a * Mathf.Deg2Rad, c = Mathf.Cos(r), s = Mathf.Sin(r);
        return new Vector2(facing * (local.x * c - local.y * s), local.x * s + local.y * c);
    }

    private Vector3 World3(Vector2 at) => new Vector3(at.x, at.y, transform.position.z);

    private static float Smooth01(float t)
    {
        t = Mathf.Clamp01(t);
        return t * t * (3f - 2f * t);
    }

    private RaycastHit2D Ray(Vector2 origin, Vector2 direction, float distance)
    {
        if (player == null || player.Motor == null || distance <= 0f) return default;
        return player.Motor.RaycastEnvironment(origin, direction, distance, platformLayer);
    }
}
