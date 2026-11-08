using System;
using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEngine;

// PlayerLaser(Star 세트 기어 3단계 '파괴 광선')의 예고선부터 레이저 끝까지를 맡는 개체(2026-11-08 재디자인).
// 사용자 요청: "차지 시간은 1초고, 이때 공격 방향으로 직선 예고선이 나온다. Player의 다른 무장(StarShot·StarSpear 등)을 참고해 레이저·예고선까지 모두 새로 작업."
// 이어서: 작은 레이저 없이 예고 뒤 바로 큰 레이저를 쏘고, 지속시간은 2초, 시작·끝에 Admin LaserRain과 같은 임팩트를 넣는다.
// - 흐름: LaserWeaponSO가 준비 시작에 BeginCharge(예고선·충전), 실행 시작에 Fire를 부르고, 실행 시간(executeTime)이 끝나면 스스로 사라진다.
// - 위치·방향: 준비가 시작될 때 Player가 보는 쪽으로 방향을 고정하고, 포구는 매 고정 단계 Player를 따라간다(반동으로 밀려도 레이저가 본체에서 떨어지지 않는다).
// - 판정(규칙: RaycastMotor2D 기반): 벽은 모터의 RaycastEnvironment로, 피해 범위는 모터의 OverlapAttackBox로 찾는다.
//   벽 검사는 Player 중심에서 중심선 하나만 쏜다. 굵은 레이저를 원으로 검사하면 바닥에 서 있을 때 바로 바닥에 막힌다.
//   피해는 tickInterval마다 적마다 한 번이다.
// - 임팩트: 폭 변화는 Admin LaserRain 레이저(LaserBeam.prefab)와 같은 값·같은 식이다. 시작 순간 폭이 burstWidthMultiplier배까지 터졌다가 돌아오고, 끝날 때 fadeOutDuration 동안 0이 된다.
//   포구·벽의 효과가 같은 변화를 따라가고, 시작과 사라지는 순간에 임팩트 피드백을 재생한다. 터지는 동안에도 판정은 레이저 굵기를 넘지 않는다.
// - 레이저 자체는 앞으로 뻗어 나가지 않고 처음부터 전체 길이로 나타난다. 속도감은 레이저 둘레를 직선으로 빠르게 날아가는 작은 입자가 보여 준다.
//   입자는 레이저 끝을 넘지 않도록 수명을 남은 거리에 맞춘다.
// - 화면 흔들림: 레이저가 나오는 순간 시작해 꺼지는 순간(끝이든 취소든) 잦아든다. 길이를 미리 정하지 않아 지속시간을 바꿔도 어긋나지 않는다.
// - 취소: Player가 맞거나 대시·잡기 등으로 공격 상태에서 벗어나면 그 자리에서 빠르게 꺼진다. 코루틴에 기대지 않고 공격 상태를 직접 본다.
// - 외형: 값은 MaterialPropertyBlock으로 셰이더(Player/Laser, Player/LaserPreview)에 넘긴다. 시간은 Time 기준이라 히트스톱 때 함께 멈춘다.
// Player 이동(FixedUpdate) 뒤에 자리를 잡도록 실행 순서를 늦춘다.
[DefaultExecutionOrder(110)]
public class PlayerLaserBeam : WorldEntityBase
{
    private enum Phase { None, Charge, Fire, Linger }

    private const float Epsilon = 0.0001f;

    private static readonly int QuadSizeId = Shader.PropertyToID("_QuadSize");
    private static readonly int MarginId = Shader.PropertyToID("_Margin");
    private static readonly int LengthId = Shader.PropertyToID("_Length");
    private static readonly int ReachId = Shader.PropertyToID("_Reach");
    private static readonly int HalfWidthId = Shader.PropertyToID("_HalfWidth");
    private static readonly int FlowId = Shader.PropertyToID("_Flow");
    private static readonly int ChargeId = Shader.PropertyToID("_Charge");
    private static readonly int FlashId = Shader.PropertyToID("_Flash");
    private static readonly int AlphaId = Shader.PropertyToID("_Alpha");

    [Header("Parts")]
    [SerializeField] private SpriteRenderer previewLine;
    [SerializeField] private SpriteRenderer beamBody;
    [SerializeField] private Transform reticle;
    [SerializeField] private Transform muzzle;
    [SerializeField] private Transform muzzleStar;
    [SerializeField] private Transform impact;
    [SerializeField] private ParticleSystem chargeMotes;
    [SerializeField] private ParticleSystem sparks;
    [SerializeField] private ParticleSystem smoke;

    [Header("Preview")]
    [SerializeField, Min(0.01f)] private float previewReachTime = 0.12f;      // 포구에서 끝까지 뻗는 시간
    [SerializeField, Min(0.001f)] private float previewStartHalfWidth = 0.1f;
    [SerializeField, Min(0.001f)] private float previewEndHalfWidth = 0.055f; // 찰수록 가늘어져 조준이 모이는 느낌을 준다
    [SerializeField, Min(0f)] private float previewFlashLead = 0.18f;         // 발사 이만큼 전부터 흰빛으로 점멸
    [SerializeField, Min(1f)] private float previewBlinkRate = 30f;
    [SerializeField, Min(0f)] private float previewFlowMin = 14f, previewFlowMax = 36f;   // 화살표가 흐르는 속도(칸/초)
    [SerializeField, Min(0.01f)] private float previewQuadHeight = 1.4f;

    [Header("Beam (임팩트 폭 변화는 Admin LaserRain과 같은 값)")]
    [SerializeField, Min(1f)] private float burstWidthMultiplier = 2f;        // 시작 순간 폭이 터지는 배율
    [SerializeField, Min(0f)] private float burstDuration = 0.08f;            // 0에서 터진 폭까지 가는 시간
    [SerializeField, Min(0f)] private float settleDuration = 0.15f;           // 터진 폭이 제자리로 돌아오는 시간
    [SerializeField, Min(0.001f)] private float fadeOutDuration = 0.15f;      // 사라질 때 폭이 0이 되는 시간
    [SerializeField, Min(0f)] private float firstTickDelay = 0.05f;
    [SerializeField, Min(0f)] private float blockedOverlap = 0.15f;           // 벽에 닿으면 벽 속으로 조금 더 그려 틈을 없앤다
    [SerializeField, Min(0.01f)] private float beamMargin = 1.2f, beamEndMargin = 2f;

    [Header("Muzzle")]
    [SerializeField, Min(0f)] private float muzzleChargeStart = 0.35f, muzzleChargeEnd = 1f;
    [SerializeField, Min(0f)] private float muzzleFire = 1.6f, impactScale = 2f;
    [SerializeField, Min(0f)] private float capPopPower = 0.6f;               // 포구·벽 효과가 폭 변화를 따라가는 정도(1이면 폭과 같은 비율, 작을수록 덜 커진다)
    [SerializeField] private float spinCharge = 720f, spinFire = 900f;        // 포구 별 회전(도/초). 충전 때는 0에서 이 값까지 빨라진다
    [SerializeField, Min(0f)] private float reticleSpin = 220f;

    [Header("Exit")]
    [SerializeField, Min(0.001f)] private float cancelFade = 0.1f;            // 취소됐을 때 꺼지는 시간
    [SerializeField, Min(0f)] private float lingerAfterCancel = 0.45f;        // 튄 불꽃이 사라질 때까지 개체를 남겨 두는 시간
    [SerializeField, Min(0f)] private float lingerAfterFinish = 1.2f;         // 연기가 사라질 때까지 개체를 남겨 두는 시간

    [Header("Feedback")]
    [SerializeField] private FeedbackDataSO impactFeedback;                   // 레이저가 생기고 사라지는 순간(포구, 벽에 닿았으면 벽에도)
    [SerializeField] private FeedbackDataSO hitFeedback;
    [SerializeField] private SoundOptions loopSound;
    [SerializeField, Min(0f)] private float fireShake = 0.245f;               // 쏘는 동안 이어지는 화면 흔들림 세기

    [Header("Particles")]
    [SerializeField, Min(0f)] private float moteDensity = 10f;                // 직선으로 날아가는 작은 입자(레이저 1칸당 초당 개수). 길이와 무관하게 화면에서 같은 밀도로 보인다
    [SerializeField] private Vector2 moteSpeed = new Vector2(70f, 130f);      // 입자 속도(칸/초)
    [SerializeField] private Vector2 moteLife = new Vector2(0.12f, 0.26f);    // 입자 수명(초). 레이저 끝까지 남은 거리가 짧으면 더 줄어든다
    [SerializeField, Min(0f)] private float moteSideSpeed = 1.2f;             // 옆으로 흩어지는 속도(칸/초, 거의 직선)
    [SerializeField, Range(0f, 1f)] private float outsideMoteShare = 0.75f;   // 레이저 바깥 둘레로 나는 비율(나머지는 레이저 위를 지나간다)
    [SerializeField] private Vector2 outsideMoteRange = new Vector2(1.04f, 1.9f);   // 바깥 입자가 중심선에서 떨어진 거리(반폭 배수)
    [SerializeField, Min(0f)] private float impactSparkRate = 110f;           // 벽에 닿은 끝에서 튀는 불꽃(개/초)
    [SerializeField, Min(0.1f)] private float smokeSpacing = 1.1f;            // 끝난 뒤 레이저 길을 따라 남는 연기 간격(칸)

    private readonly Collider2D[] overlapResults = new Collider2D[64];
    private readonly HashSet<HealthBase> damagedTargets = new HashSet<HealthBase>();

    private MaterialPropertyBlock block;
    private LaserWeaponSO weapon;
    private PlayerController player;
    private Action<CharacterController2D, Vector2> onHitEnemy;
    private SoundHandle loopHandle;
    private CinemachineImpulseManager.ImpulseEvent heldShake;

    private Phase phase = Phase.None;
    private Phase exitFrom = Phase.None;   // 꺼지기 직전 단계(취소 때 그 모습에서 줄어든다)
    private float phaseAge, lingerDuration;
    private float flow, beamTime, starAngle, reticleAngle;
    private float nextTickAt;
    private float moteDebt, impactSparkDebt;
    private bool hasBlock, finishedNaturally, endImpactPlayed;
    private float quadSize = 6.2f;

    private Vector2 origin;
    private Vector2 direction = Vector2.right;
    private float forwardOffset;
    private float blockedDistance;
    private float halfWidth, judgeHalfWidth;
    private float flash;

    // 읽기 전용 상태(검증·디버그용)
    public bool IsCharging => phase == Phase.Charge && !isReturning;
    public bool IsFiring => phase == Phase.Fire && !isReturning;
    public float StageAge => phaseAge;
    public Vector2 Origin => origin;
    public Vector2 Direction => direction;
    public float BlockedDistance => blockedDistance;
    public bool HasBlock => hasBlock;
    public float Length => blockedDistance;
    public float HalfWidth => halfWidth;              // 보이는 반폭
    public float JudgeHalfWidth => judgeHalfWidth;    // 판정 반폭(터지는 폭은 뺀다)
    public int TickCount { get; private set; }

    protected override void Awake()
    {
        base.Awake();
        block = new MaterialPropertyBlock();   // MonoBehaviour 필드 초기화 중에는 만들 수 없다
        if (beamBody != null && beamBody.sprite != null) quadSize = beamBody.sprite.bounds.size.x;
    }

    public override void OnSpawnedFromPool()
    {
        base.OnSpawnedFromPool();
        ResetState();
    }

    public override void ForceReturnToPool()
    {
        ResetState();
        base.ForceReturnToPool();
    }

    private void OnDisable()
    {
        StopLoop();
        StopHeldShake();
    }

    private void ResetState()
    {
        StopLoop();
        StopHeldShake();
        phase = exitFrom = Phase.None;
        phaseAge = lingerDuration = 0f;
        flow = beamTime = starAngle = reticleAngle = 0f;
        nextTickAt = 0f;
        moteDebt = impactSparkDebt = 0f;
        hasBlock = finishedNaturally = endImpactPlayed = false;
        blockedDistance = 0f;
        halfWidth = judgeHalfWidth = 0f;
        flash = 0f;
        TickCount = 0;
        onHitEnemy = null;
        SetRenderers(false, false);
        if (reticle != null) reticle.gameObject.SetActive(false);
        if (muzzle != null) muzzle.gameObject.SetActive(false);
        if (impact != null) impact.gameObject.SetActive(false);
        ClearParticles(chargeMotes);
        ClearParticles(sparks);
        ClearParticles(smoke);
    }

    // 준비 시작: 예고선과 충전 효과를 켠다.
    public void BeginCharge(CharacterController2D owner, LaserWeaponSO weapon, WeaponBaseSO.DamageInfo damageInfo,
        Action<CharacterController2D, Vector2> onHitEnemy)
    {
        Initialize(owner, damageInfo);
        this.weapon = weapon;
        this.onHitEnemy = onHitEnemy;
        player = owner as PlayerController;
        direction = Vector2.right * (owner != null && owner.Facing < 0 ? -1f : 1f);
        forwardOffset = weapon.spawnOffset.x;
        transform.rotation = Quaternion.Euler(0f, 0f, direction.x < 0f ? 180f : 0f);
        phase = Phase.Charge;
        phaseAge = 0f;
        UpdateOrigin();
        UpdateBlock();
        SetRenderers(true, false);
        if (reticle != null) reticle.gameObject.SetActive(true);
        if (muzzle != null) muzzle.gameObject.SetActive(true);
        if (muzzleStar != null) muzzleStar.gameObject.SetActive(false);   // 충전 중에는 Player의 코어가 포구에 나와 있다(발사 때 코어가 사라지며 이 별로 바뀐다)
        if (chargeMotes != null)
        {
            chargeMotes.Clear();
            chargeMotes.Play();
        }
        ApplyVisuals();
    }

    // 실행 시작: 레이저를 쏜다. Player의 공격 단계가 바뀌는 프레임에 무기가 바로 부른다.
    public void Fire()
    {
        if (phase != Phase.Charge || weapon == null) return;
        ClearParticles(chargeMotes);
        phase = Phase.Fire;
        phaseAge = 0f;
        beamTime = 0f;
        halfWidth = judgeHalfWidth = 0f;
        nextTickAt = firstTickDelay;
        flash = 1f;
        endImpactPlayed = false;
        TickCount = 0;
        owner.AddRecoil(weapon.recoilPower, owner.Facing);
        UpdateOrigin();
        UpdateBlock();
        SetRenderers(false, true);
        if (reticle != null) reticle.gameObject.SetActive(false);
        if (muzzle != null) muzzle.gameObject.SetActive(true);
        if (muzzleStar != null) muzzleStar.gameObject.SetActive(true);
        if (sparks != null) sparks.Play();
        if (smoke != null) smoke.Play();
        StartLoop(loopSound);
        StartHeldShake();
        PlayImpact();
        ApplyVisuals();
    }

    private void FixedUpdate()
    {
        if (phase == Phase.None || isReturning) return;
        if (owner == null || !owner.gameObject.activeInHierarchy)
        {
            ForceReturnToPool();
            return;
        }

        float dt = Time.fixedDeltaTime;
        phaseAge += dt;

        if (phase != Phase.Linger && !IsAttackAlive())
        {
            BeginExit(false);
            return;
        }

        switch (phase)
        {
            case Phase.Charge:
                UpdateOrigin();
                UpdateBlock();
                break;

            case Phase.Fire:
                UpdateOrigin();
                UpdateBlock();
                EvaluateBeam();
                UpdateDamage();
                EmitSparks(dt);
                if (!endImpactPlayed && phaseAge >= weapon.executeTime - fadeOutDuration - Epsilon)
                {
                    endImpactPlayed = true;
                    PlayImpact();
                }
                if (phaseAge >= weapon.executeTime - Epsilon) BeginExit(true);
                break;

            case Phase.Linger:
                if (phaseAge >= lingerDuration - Epsilon) ForceReturnToPool();
                break;
        }
    }

    private bool IsAttackAlive()
    {
        if (player == null || player.AbilityManager == null) return false;
        var abilities = player.AbilityManager;
        if (abilities.AttackAbility == null || abilities.AttackAbility.AttackPhase == AbilityState.None) return false;
        return abilities.StateMachine.CurrentState == PlayerState.Attack;
    }

    private void UpdateOrigin()
    {
        Vector2 ownerPosition = owner.transform.position;
        origin = ownerPosition + new Vector2(weapon.spawnOffset.x * direction.x, weapon.spawnOffset.y);
        transform.position = origin;
    }

    // Player 중심에서 중심선 하나로 첫 벽을 찾는다(포구가 얇은 벽을 넘어가 있어도 막히도록).
    private void UpdateBlock()
    {
        Vector2 from = origin - direction * forwardOffset;
        float reach = weapon.laserLength + forwardOffset;
        RaycastHit2D hit = owner.Motor.RaycastEnvironment(from, direction, reach, weapon.platformLayer);
        hasBlock = hit.collider != null;
        blockedDistance = hasBlock ? Mathf.Max(0f, hit.distance - forwardOffset) : weapon.laserLength;
    }

    // 임팩트는 포구에서, 벽에 닿아 있으면 벽에서도 터진다.
    private void PlayImpact()
    {
        if (impactFeedback == null || FeedbackPlayer.Instance == null) return;
        FeedbackPlayer.Instance.PlayExact(impactFeedback, origin, Quaternion.identity);
        if (hasBlock) FeedbackPlayer.Instance.PlayExact(impactFeedback, origin + direction * blockedDistance, Quaternion.identity);
    }

    // 끝이든 취소든 레이저를 끄고 튄 불꽃·연기가 사라질 때까지 개체를 남겨 둔다.
    private void BeginExit(bool natural)
    {
        exitFrom = phase;
        finishedNaturally = natural;
        phase = Phase.Linger;
        phaseAge = 0f;
        lingerDuration = natural ? lingerAfterFinish : lingerAfterCancel;
        StopLoop();
        StopHeldShake();
        ClearParticles(chargeMotes);
        if (natural)
        {
            EmitTrailSmoke();
            SetRenderers(false, false);
            if (muzzle != null) muzzle.gameObject.SetActive(false);
            if (impact != null) impact.gameObject.SetActive(false);
        }
    }

    // ===== 판정 =====
    private void UpdateDamage()
    {
        while (phaseAge >= nextTickAt - Epsilon && phaseAge < weapon.executeTime + Epsilon)
        {
            ApplyDamage();
            TickCount++;
            nextTickAt += weapon.tickInterval;
        }
    }

    private void ApplyDamage()
    {
        if (judgeHalfWidth <= 0f || owner == null || owner.Motor == null) return;
        // 포구 뒤로 반폭만큼 더 덮어 본체에 붙은 적도 맞게 한다.
        float reach = blockedDistance;
        float boxLength = reach + judgeHalfWidth;
        Vector2 center = origin + direction * ((reach - judgeHalfWidth) * 0.5f);
        var size = new Vector2(boxLength, judgeHalfWidth * 2f);
        float angle = direction.x < 0f ? 180f : 0f;

        RaycastMotor2D.PrepareAttackQueries();
        int count = owner.Motor.OverlapAttackBox(center, size, angle, overlapResults, weapon.damageMask);
        if (count <= 0) return;

        damagedTargets.Clear();
        for (int i = 0; i < count; i++)
        {
            Collider2D hit = overlapResults[i];
            if (hit == null) continue;
            HealthBase health = hit.GetComponentInParent<HealthBase>();
            if (health == null || !damagedTargets.Add(health)) continue;
            if (health is not EnemyHealth enemy) continue;
            if (enemy.enemyShieldSystem != null && enemy.enemyShieldSystem.IsSpawnLocked) continue;

            // 맞은 위치: 표적 중심을 레이저 중심선에 내린 점(효과가 레이저 위에서 난다)
            float along = Mathf.Clamp(Vector2.Dot((Vector2)hit.bounds.center - origin, direction), 0f, reach);
            Vector2 hitPoint = origin + direction * along;

            if (enemy.enemyShieldSystem == null) enemy.TakeDamage(damageInfo, hitPoint);
            else enemy.enemyShieldSystem.TakeDamage(damageInfo, hitPoint);
            onHitEnemy?.Invoke(owner, hitPoint);
            if (hitFeedback != null && FeedbackPlayer.Instance != null) FeedbackPlayer.Instance.Play(hitFeedback, hitPoint);
        }
    }

    // ===== 외형 값 =====
    // 폭 변화는 LaserBeam(Admin LaserRain)과 같은 식이다: 0 → 터진 폭 → 제 폭, 끝나기 fadeOutDuration 전부터 0으로.
    private void EvaluateBeam()
    {
        float target = weapon.laserWidth * 0.5f;
        float burst = target * burstWidthMultiplier;
        float animated = target;
        if (burstDuration > 0f && phaseAge < burstDuration)
            animated = Mathf.Lerp(0f, burst, phaseAge / burstDuration);
        else if (settleDuration > 0f && phaseAge < burstDuration + settleDuration)
            animated = Mathf.Lerp(burst, target, (phaseAge - burstDuration) / settleDuration);
        float life = Mathf.Clamp01((weapon.executeTime - phaseAge) / fadeOutDuration);
        halfWidth = animated * life;
        judgeHalfWidth = Mathf.Min(halfWidth, target);
    }

    private void Update()
    {
        if (phase == Phase.None || isReturning) return;
        flash = Mathf.MoveTowards(flash, 0f, Time.deltaTime / 0.18f);
    }

    private void LateUpdate()
    {
        if (phase == Phase.None || isReturning) return;
        if (owner != null && phase != Phase.Linger) UpdateOrigin();
        ApplyVisuals();
    }

    private void ApplyVisuals()
    {
        float dt = Time.deltaTime;
        switch (phase)
        {
            case Phase.Charge:
                ApplyCharge(dt);
                break;
            case Phase.Fire:
                ApplyFiring(dt);
                break;
            case Phase.Linger:
                ApplyExit(dt);
                break;
        }
    }

    private void ApplyCharge(float dt)
    {
        float windup = weapon.windupTime;
        float charge = windup > 0f ? Mathf.Clamp01(phaseAge / windup) : 1f;
        float reachT = Mathf.Clamp01(phaseAge / previewReachTime);
        float reach = blockedDistance * EaseOut(reachT);
        float hw = Mathf.Lerp(previewStartHalfWidth, previewEndHalfWidth, charge * charge);
        bool flashing = windup - phaseAge <= previewFlashLead && Mathf.Repeat(phaseAge * previewBlinkRate, 1f) < 0.5f;
        flow += Mathf.Lerp(previewFlowMin, previewFlowMax, charge) * dt;
        DrawPreview(reach, hw, charge, flashing ? 1f : 0f, 1f);

        if (reticle != null)
        {
            reticle.localPosition = new Vector3(reach, 0f, 0f);
            reticleAngle += reticleSpin * dt;
            reticle.localRotation = Quaternion.Euler(0f, 0f, reticleAngle);
            float pulse = 0.85f + 0.2f * Mathf.Sin(phaseAge * 26f) + (flashing ? 0.35f : 0f);
            reticle.localScale = Vector3.one * (Mathf.Lerp(0.5f, 1f, EaseOut(reachT)) * pulse);
        }

        if (muzzle != null)
        {
            float grow = Mathf.Lerp(muzzleChargeStart, muzzleChargeEnd, EaseIn(charge));
            float tremble = 1f + (charge > 0.8f ? 0.08f * Mathf.Sin(phaseAge * 70f) : 0.03f * Mathf.Sin(phaseAge * 24f));
            muzzle.localScale = Vector3.one * (grow * tremble);
            SpinStar(spinCharge * charge * charge, dt);
        }
    }

    private void ApplyFiring(float dt)
    {
        float length = hasBlock ? blockedDistance + blockedOverlap : blockedDistance;
        beamTime += dt;
        DrawBeam(length, halfWidth, flash, 1f);

        // 포구·벽 효과는 폭 변화를 따라 터졌다가 함께 사라진다.
        float target = weapon.laserWidth * 0.5f;
        float pop = target > 0f ? Mathf.Pow(halfWidth / target, capPopPower) : 0f;
        if (muzzle != null)
        {
            muzzle.localScale = Vector3.one * (muzzleFire * pop * (1f + 0.09f * Mathf.Sin(phaseAge * 50f) + flash * 0.5f));
            SpinStar(spinFire, dt);
        }

        bool showImpact = hasBlock && halfWidth > 0.01f;
        if (impact != null)
        {
            if (impact.gameObject.activeSelf != showImpact) impact.gameObject.SetActive(showImpact);
            if (showImpact)
            {
                impact.localPosition = new Vector3(blockedDistance, 0f, 0f);
                impact.localScale = Vector3.one * (impactScale * pop * (1f + 0.12f * Mathf.Sin(phaseAge * 61f)));
            }
        }
    }

    // 취소됐을 때 그 모습에서 빠르게 줄어든다. 끝까지 쏜 뒤에는 이미 꺼져 있다.
    private void ApplyExit(float dt)
    {
        if (finishedNaturally) return;
        float k = 1f - Mathf.Clamp01(phaseAge / cancelFade);
        if (k <= 0f)
        {
            SetRenderers(false, false);
            if (reticle != null) reticle.gameObject.SetActive(false);
            if (muzzle != null) muzzle.gameObject.SetActive(false);
            if (impact != null) impact.gameObject.SetActive(false);
            return;
        }
        if (exitFrom == Phase.Charge)
        {
            float charge = weapon.windupTime > 0f ? Mathf.Clamp01(phaseAge / weapon.windupTime) : 1f;
            DrawPreview(blockedDistance, previewStartHalfWidth * k, charge, 0f, k);
        }
        else
        {
            beamTime += dt;
            DrawBeam(blockedDistance, halfWidth * k, 0f, k);
        }
        if (muzzle != null) muzzle.localScale *= 1f - Mathf.Min(1f, dt / cancelFade);
    }

    private void SpinStar(float speed, float dt)
    {
        if (muzzleStar == null) return;
        starAngle += speed * dt;
        muzzleStar.localRotation = Quaternion.Euler(0f, 0f, starAngle);
    }

    private void DrawPreview(float reach, float hw, float charge, float previewFlash, float alpha)
    {
        if (previewLine == null) return;
        const float margin = 1f, endMargin = 1f;
        float quadLength = margin + Mathf.Max(reach, 0f) + endMargin;
        Transform t = previewLine.transform;
        t.localPosition = new Vector3(quadLength * 0.5f - margin, 0f, 0f);
        t.localScale = new Vector3(quadLength / quadSize, previewQuadHeight / quadSize, 1f);
        previewLine.GetPropertyBlock(block);
        block.SetVector(QuadSizeId, new Vector4(quadLength, previewQuadHeight, 0f, 0f));
        block.SetFloat(MarginId, margin);
        block.SetFloat(ReachId, Mathf.Max(reach, 0f));
        block.SetFloat(HalfWidthId, Mathf.Max(hw, 0.001f));
        block.SetFloat(FlowId, flow);
        block.SetFloat(ChargeId, charge);
        block.SetFloat(FlashId, previewFlash);
        block.SetFloat(AlphaId, alpha);
        previewLine.SetPropertyBlock(block);
    }

    private void DrawBeam(float length, float hw, float flashValue, float alpha)
    {
        if (beamBody == null) return;
        float quadLength = beamMargin + Mathf.Max(length, 0f) + beamEndMargin;
        // 번짐이 끝나는 높이(셰이더 번짐 길이의 약 5배) + 여유
        float wide = Mathf.Max(hw, 0.05f);
        float quadHeight = 2f * (wide * 1.3f + 5f * (0.14f + 0.38f * wide));
        Transform t = beamBody.transform;
        t.localPosition = new Vector3(quadLength * 0.5f - beamMargin, 0f, 0f);
        t.localScale = new Vector3(quadLength / quadSize, quadHeight / quadSize, 1f);
        beamBody.GetPropertyBlock(block);
        block.SetVector(QuadSizeId, new Vector4(quadLength, quadHeight, 0f, 0f));
        block.SetFloat(MarginId, beamMargin);
        block.SetFloat(LengthId, Mathf.Max(length, 0.001f));
        block.SetFloat(HalfWidthId, Mathf.Max(hw, 0.001f));
        block.SetFloat(FlowId, beamTime);
        block.SetFloat(FlashId, flashValue);
        block.SetFloat(AlphaId, alpha);
        beamBody.SetPropertyBlock(block);
    }

    private void SetRenderers(bool showPreview, bool showBeam)
    {
        if (previewLine != null) previewLine.enabled = showPreview;
        if (beamBody != null) beamBody.enabled = showBeam;
    }

    // ===== 입자 =====
    private void EmitSparks(float dt)
    {
        if (sparks == null || halfWidth <= 0.01f) return;
        float length = blockedDistance;
        Vector2 side = new Vector2(-direction.y, direction.x);

        // 속도감: 작은 입자가 레이저 둘레를 직선으로 빠르게 날아간다. 터지는 동안의 폭을 따라가지 않도록 제 반폭을 기준으로 둔다.
        float target = weapon.laserWidth * 0.5f;
        moteDebt += moteDensity * length * dt;
        for (; moteDebt >= 1f; moteDebt -= 1f)
        {
            float along = UnityEngine.Random.Range(0f, length);
            float speed = UnityEngine.Random.Range(moteSpeed.x, moteSpeed.y);
            float life = Mathf.Min(UnityEngine.Random.Range(moteLife.x, moteLife.y), (length - along) / speed);   // 벽이나 레이저 끝을 넘지 않는다
            if (life <= 0.02f) continue;
            bool outside = UnityEngine.Random.value < outsideMoteShare;
            float offset = outside
                ? (UnityEngine.Random.value < 0.5f ? -1f : 1f) * UnityEngine.Random.Range(outsideMoteRange.x, outsideMoteRange.y) * target
                : UnityEngine.Random.Range(-0.95f, 0.95f) * target;
            var p = new ParticleSystem.EmitParams
            {
                position = origin + direction * along + side * offset,
                velocity = direction * speed + side * UnityEngine.Random.Range(-moteSideSpeed, moteSideSpeed),
                startLifetime = life,
            };
            sparks.Emit(p, 1);
        }

        if (!hasBlock) return;
        impactSparkDebt += impactSparkRate * dt;
        Vector2 end = origin + direction * blockedDistance;
        for (; impactSparkDebt >= 1f; impactSparkDebt -= 1f)
        {
            float angle = UnityEngine.Random.Range(-80f, 80f) * Mathf.Deg2Rad;
            Vector2 back = -direction * Mathf.Cos(angle) + side * Mathf.Sin(angle);
            var p = new ParticleSystem.EmitParams
            {
                position = end + side * (UnityEngine.Random.Range(-1f, 1f) * halfWidth),
                velocity = back * UnityEngine.Random.Range(9f, 20f),
            };
            sparks.Emit(p, 1);
        }
    }

    // 레이저가 지나간 길을 따라 연기를 남긴다.
    private void EmitTrailSmoke()
    {
        if (smoke == null) return;
        float length = blockedDistance;
        Vector2 side = new Vector2(-direction.y, direction.x);
        float spread = weapon.laserWidth * 0.4f;
        for (float d = smokeSpacing * 0.5f; d <= length; d += smokeSpacing)
        {
            var p = new ParticleSystem.EmitParams
            {
                position = origin + direction * d + side * UnityEngine.Random.Range(-spread, spread),
                velocity = side * UnityEngine.Random.Range(-0.5f, 0.5f) + direction * UnityEngine.Random.Range(0.3f, 1.2f),
            };
            smoke.Emit(p, 1);
        }
    }

    private static void ClearParticles(ParticleSystem system)
    {
        if (system != null) system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
    }

    // ===== 화면 흔들림 =====
    private void StartHeldShake()
    {
        StopHeldShake();
        if (FeedbackPlayer.Instance != null) heldShake = FeedbackPlayer.Instance.BeginHeldShake(fireShake, origin);
    }

    private void StopHeldShake()
    {
        if (heldShake == null) return;
        if (FeedbackPlayer.Instance != null) FeedbackPlayer.Instance.EndHeldShake(heldShake);
        heldShake = null;
    }

    // ===== 소리 =====
    private void StartLoop(SoundOptions sound)
    {
        StopLoop();
        if (sound == null || sound.clip == null || SoundManager.Instance == null) return;
        loopHandle = SoundManager.Instance.PlayLoop(sound, transform.position, null);
    }

    private void StopLoop()
    {
        loopHandle?.Stop();
        loopHandle = null;
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
