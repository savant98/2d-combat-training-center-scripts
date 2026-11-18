using UnityEngine;

// Rifle Gear 3단계 '머신건 장착'의 장착 표시(2026-11-18, 1차 시안).
// 사용자 결정 "일정 시간 기본 공격이 머신건으로"에 맞춰, 장착된 동안 총에 회전 총열 묶음을 붙이고 남은 시간을 막대로 보여 준다. 판정은 없다.
// - 꺼내는 곳: MachineGunMountWeaponSO가 장착하는 순간 풀에서 꺼내 Begin을 부른다. Player.prefab은 건드리지 않고 Player를 따라다닌다(부모로 붙이지 않음).
// - 회전 총열: 총 모형(WeaponModel/StarModel)의 축을 따라 붙는다. 총열 4개가 총 축 둘레를 돌며 위아래로 오가고, 앞쪽에 온 관은 총 앞(정렬 순서 높음)에,
//   뒤쪽에 온 관은 총 뒤에 그려진다. 연사하는 동안(IsStarShotActive) 빨리, 쉬는 동안 천천히 돈다.
// - 남은 시간 막대: Player/SkillBattery 재질의 가로 배터리. 남은 시간 비율만큼 차 있고 끝 무렵에는 깜빡인다.
// - 보이는 조건: 장착한 자리(PrimaryOverrideSetIndex)를 지금 들고 있을 때만 보인다(자리를 바꾸면 숨고, 돌아오면 다시 보인다).
//   장착이 끝나면(PrimaryOverrideChanged — 시간 종료·무장 교체·게이지 초기화) 줄어들며 사라지고 풀로 돌아간다.
// - 자리: Player는 FixedMotionBody가 렌더 때 보간 위치로 옮겨 그리므로, 같은 보간 자세(FixedMotionBody.GetPresentationPose)를 따라간다.
//   PlayerStarShotAim(실행 순서 120)이 총을 조준 방향으로 돌린 뒤에 자리를 잡도록 실행 순서를 늦춘다.
// - 장착 오라(2026-11-18 2차, 사용자 지시 "player주위에 머신건 장착시의 추가 오오라 효과를 넣어줘"): Player 둘레의 청록 빛(Aura/Glow, 몸 뒤),
//   발밑에서 위로 오르는 빛줄기(Aura/Rising, 월드 좌표 입자), 연사하는 동안 일정 간격으로 퍼지는 고리(Aura/Pulse).
//   연사하면 세기(heat)가 올라 빛이 커지고 밝아지며 빛줄기가 늘어난다. 장착한 자리를 든 동안만 보이고(총 모형이 잠시 숨어도 오라는 남는다),
//   남은 시간 막대와 함께 끝 무렵에 깜빡인다. 장착이 끝나면 오라도 사라지고, 날던 빛줄기가 다 사라진 뒤 풀로 돌아간다.
[DefaultExecutionOrder(130)]
public class MachineGunMountVisual : WorldEntityBase
{
    private static readonly int FillId = Shader.PropertyToID("_Fill");
    private static readonly int FullId = Shader.PropertyToID("_Full");
    private static readonly int FlashId = Shader.PropertyToID("_Flash");
    private static readonly int AlphaId = Shader.PropertyToID("_Alpha");
    private static readonly int ClockId = Shader.PropertyToID("_Clock");

    private const string GunPath = "WeaponModel/StarModel";

    [Header("Parts")]
    [Tooltip("총 축에 맞춰 붙는 부품 묶음(회전 총열·고정 고리). 비우면 자식 'Mount'")]
    [SerializeField] private Transform mount;
    [Tooltip("총 축 둘레를 도는 총열(옆에서 본 관 Sprite). 비우면 Mount 아래 'Barrel'로 시작하는 자식")]
    [SerializeField] private SpriteRenderer[] barrels;
    [Tooltip("남은 시간 막대의 자리(월드 축, 돌지 않는다). 비우면 자식 'Timer'")]
    [SerializeField] private Transform timer;
    [Tooltip("남은 시간 막대(Player/SkillBattery 재질). 비우면 Timer 아래의 SpriteRenderer")]
    [SerializeField] private SpriteRenderer timerBar;

    [Header("Mount Layout")]
    [Tooltip("총 소켓(총 모형 원점)에서 총 축을 따라 총열 묶음이 시작하는 거리(칸)")]
    [SerializeField] private float mountForward = 0.45f;
    [Tooltip("총 축에서 위로 띄우는 거리(칸)")]
    [SerializeField] private float mountUp = 0f;
    [Tooltip("총열이 총 축 둘레를 도는 반지름(칸, 옆에서 보면 위아래로 오가는 폭)")]
    [SerializeField] private float barrelRadius = 0.1f;
    [SerializeField] private int frontSortingOrder = 4;
    [SerializeField] private int backSortingOrder = 1;
    [Tooltip("앞쪽·뒤쪽에 온 관의 굵기 배율(앞이 굵어 보여 도는 것이 읽힌다)")]
    [SerializeField] private float frontThickness = 1.08f, backThickness = 0.82f;

    [Header("Spin")]
    [Tooltip("쉬는 동안·연사하는 동안 총열 묶음이 도는 빠르기(초당 바퀴)")]
    [SerializeField] private float idleSpin = 0.6f;
    [SerializeField] private float fireSpin = 3.5f;
    [Tooltip("빠르기가 목표를 따라가는 정도(1/초)")]
    [SerializeField, Min(0.01f)] private float spinResponse = 6f;

    [Header("Timer")]
    [Tooltip("Player 중심에서 막대 가운데까지(x는 보는 방향 쪽, 칸)")]
    [SerializeField] private Vector2 timerOffset = new Vector2(0.75f, 0.62f);
    [Tooltip("남은 비율이 이 값 이하면 막대가 깜빡인다")]
    [SerializeField, Range(0f, 1f)] private float warnFraction = 0.25f;
    [SerializeField, Min(0f)] private float warnBlinkRate = 7f;
    [Tooltip("방향을 바꾸면 막대가 반대쪽으로 미끄러져 가는 시간(초)")]
    [SerializeField, Min(0.01f)] private float sideSwitchTime = 0.12f;

    [Header("Motion")]
    [SerializeField, Min(0.01f)] private float appearTime = 0.12f;
    [Tooltip("장착 순간 부품이 커졌다 돌아오는 정도")]
    [SerializeField, Min(0f)] private float popScale = 0.22f;
    [SerializeField, Min(0.01f)] private float flashTime = 0.3f;

    [Header("Aura")]
    [Tooltip("몸 뒤에 깔리는 청록 빛(HammerSkillBlast 섬광 모양 Sprite). 비우면 자식 'Aura/Glow'")]
    [SerializeField] private SpriteRenderer auraGlow;
    [Tooltip("발밑에서 위로 오르는 빛줄기(월드 좌표 입자, 초당 개수를 이 개체가 정한다). 비우면 자식 'Aura/Rising'")]
    [SerializeField] private ParticleSystem auraRising;
    [Tooltip("연사하는 동안 일정 간격으로 퍼지는 고리(이 개체가 입자를 하나씩 낸다). 비우면 자식 'Aura/Pulse'")]
    [SerializeField] private ParticleSystem auraPulse;
    [Tooltip("빛의 지름(칸): 쉬는 동안 · 연사 중")]
    [SerializeField, Min(0f)] private float auraIdleSize = 2.4f, auraFireSize = 2.8f;
    [Tooltip("빛의 알파: 쉬는 동안 · 연사 중")]
    [SerializeField, Range(0f, 1f)] private float auraIdleAlpha = 0.22f, auraFireAlpha = 0.45f;
    [Tooltip("쉬는 동안 빛이 숨 쉬듯 어두워지는 폭(비율)과 빠르기(초당 번). 연사 중에는 숨 쉬지 않는다")]
    [SerializeField, Range(0f, 1f)] private float auraBreath = 0.15f;
    [SerializeField, Min(0f)] private float auraBreathRate = 1.2f;
    [Tooltip("빛줄기 초당 개수: 쉬는 동안 · 연사 중")]
    [SerializeField, Min(0f)] private float risingIdleRate = 10f, risingFireRate = 26f;
    [Tooltip("연사하는 동안 고리를 내는 간격(초)")]
    [SerializeField, Min(0.05f)] private float pulseInterval = 0.35f;
    [Tooltip("연사 세기(heat)가 0과 1 사이를 오가는 시간(초)")]
    [SerializeField, Min(0.01f)] private float heatTime = 0.15f;

    private PlayerController player;
    private PlayerWeaponAbility ability;
    private PlayerHealth health;
    private WeaponBaseSO weapon;
    private Transform gun;
    private MaterialPropertyBlock block;
    private int setIndex = -1;
    private bool running, ending, subscribed;
    private float appear, flash, age, spinPhase, spinRate, side = 1f;
    private float auraAppear, heat, pulseTimer, auraQuad = 6.2f, auraAlpha, auraSize, risingRate;

    // 검증·디버그가 읽기만 하는 값
    public bool IsShown => running && appear > 0.01f;
    public float Appear => appear;
    public float SpinRate => spinRate;
    public int SetIndex => setIndex;
    public bool IsAuraShown => running && auraAppear > 0.01f;
    public float AuraAlpha => auraAlpha;            // 이번 프레임 빛의 알파(깜빡임·숨쉬기 포함)
    public float AuraSize => auraSize;              // 이번 프레임 빛의 지름(칸)
    public float Heat => heat;                      // 연사 세기 0~1
    public float AuraRisingRate => risingRate;      // 빛줄기 초당 개수
    public int AuraParticleCount => (auraRising != null ? auraRising.particleCount : 0) + (auraPulse != null ? auraPulse.particleCount : 0);

    protected override void Awake()
    {
        base.Awake();
        block = new MaterialPropertyBlock();   // MonoBehaviour 필드 초기화 중에는 만들 수 없다
        if (mount == null) mount = transform.Find("Mount");
        if ((barrels == null || barrels.Length == 0) && mount != null)
        {
            var found = new System.Collections.Generic.List<SpriteRenderer>();
            foreach (Transform child in mount)
                if (child.name.StartsWith("Barrel") && child.TryGetComponent(out SpriteRenderer r)) found.Add(r);
            barrels = found.ToArray();
        }
        if (timer == null) timer = transform.Find("Timer");
        if (timerBar == null && timer != null) timerBar = timer.GetComponentInChildren<SpriteRenderer>(true);
        if (auraGlow == null) auraGlow = FindPart<SpriteRenderer>("Aura/Glow");
        if (auraRising == null) auraRising = FindPart<ParticleSystem>("Aura/Rising");
        if (auraPulse == null) auraPulse = FindPart<ParticleSystem>("Aura/Pulse");
        if (auraGlow != null && auraGlow.sprite != null) auraQuad = Mathf.Max(0.0001f, auraGlow.sprite.bounds.size.x);
    }

    private T FindPart<T>(string path) where T : Component
    {
        var part = transform.Find(path);
        return part != null ? part.GetComponent<T>() : null;
    }

    public override void OnSpawnedFromPool()
    {
        base.OnSpawnedFromPool();
        Unsubscribe();
        player = null; ability = null; health = null; weapon = null; gun = null;
        setIndex = -1;
        running = ending = false;
        appear = flash = age = spinPhase = 0f;
        spinRate = idleSpin;
        auraAppear = heat = pulseTimer = auraAlpha = auraSize = risingRate = 0f;
        if (mount != null) mount.localScale = Vector3.zero;
        ApplyTimer(0f, 0f);
        ClearAura();   // 풀에서 다시 꺼낼 때 지난 장착의 빛줄기·고리가 남지 않게
        ApplyAura();
    }

    public void Begin(PlayerController owner, PlayerWeaponAbility source, WeaponBaseSO overrideWeapon, int overrideSet)
    {
        this.owner = owner;
        player = owner;
        ability = source;
        weapon = overrideWeapon;
        setIndex = overrideSet;
        health = owner != null ? owner.Health : null;
        gun = owner != null && owner.model != null ? owner.model.transform.Find(GunPath) : null;
        running = true;
        ending = false;
        appear = 0f;
        flash = 1f;
        age = 0f;
        spinRate = idleSpin;
        auraAppear = heat = pulseTimer = auraAlpha = auraSize = risingRate = 0f;
        StartAura();
        side = owner != null && owner.Facing < 0 ? -1f : 1f;
        Unsubscribe();
        if (ability != null)
        {
            ability.PrimaryOverrideChanged += OnOverrideChanged;
            subscribed = true;
        }
        Place();
        ApplyTimer(Fraction(), 0f);
    }

    // 같은 장착이 다시 쓰여 남은 시간이 새로 정해졌다(저장 기술 등).
    public void Refresh()
    {
        if (!running) return;
        ending = false;
        flash = 1f;
    }

    public bool IsRunningFor(PlayerController owner, WeaponBaseSO overrideWeapon, int overrideSet)
        => running && !ending && !isReturning && gameObject.activeInHierarchy
           && player == owner && weapon == overrideWeapon && setIndex == overrideSet;

    private bool IsOverrideActive()
        => ability != null && weapon != null && ability.PrimaryOverrideWeapon == weapon && ability.PrimaryOverrideSetIndex == setIndex;

    private void OnOverrideChanged(int changedSet)
    {
        if (running && !IsOverrideActive()) ending = true;
    }

    private float Fraction()
    {
        if (!IsOverrideActive()) return 0f;
        float duration = ability.PrimaryOverrideDuration;
        return duration > 0f ? Mathf.Clamp01(ability.PrimaryOverrideRemaining / duration) : 0f;
    }

    private void LateUpdate()
    {
        if (!running || isReturning) return;
        if (player == null || !player.gameObject.activeInHierarchy || ability == null)
        {
            ForceReturnToPool();
            return;
        }
        if (!ending && !IsOverrideActive()) ending = true;

        float dt = Time.deltaTime;
        bool dead = health != null && health.IsDead;
        bool holding = !ending && !dead && ability.CurrentSetIndex == setIndex;   // 장착한 자리를 들고 있다(오라)
        bool shown = holding && gun != null && gun.gameObject.activeInHierarchy;  // 총 모형도 보인다(총열·막대)
        appear = Mathf.MoveTowards(appear, shown ? 1f : 0f, dt / appearTime);
        auraAppear = Mathf.MoveTowards(auraAppear, holding ? 1f : 0f, dt / appearTime);
        if (ending && appear <= 0f && auraAppear <= 0f && AuraParticleCount == 0)
        {
            ReturnToPool();
            return;
        }

        age += dt;
        flash = Mathf.MoveTowards(flash, 0f, dt / flashTime);
        bool firing = shown && ability.IsStarShotActive;
        bool auraFiring = holding && ability.IsStarShotActive;
        heat = Mathf.MoveTowards(heat, auraFiring ? 1f : 0f, dt / heatTime);
        spinRate = Mathf.Lerp(spinRate, firing ? fireSpin : idleSpin, 1f - Mathf.Exp(-spinResponse * dt));
        spinPhase = Mathf.Repeat(spinPhase + spinRate * dt, 1f);
        side = Mathf.MoveTowards(side, player.Facing < 0 ? -1f : 1f, dt * 2f / sideSwitchTime);
        Place();

        float blink = 1f;
        float fraction = Fraction();
        if (!ending && fraction > 0f && fraction <= warnFraction)
            blink = 0.55f + 0.45f * (0.5f + 0.5f * Mathf.Cos(age * warnBlinkRate * Mathf.PI * 2f));
        ApplyTimer(fraction, appear * blink);
        UpdateAura(dt, auraFiring, blink);
    }

    // 오라: 나타남(auraAppear)·연사 세기(heat)·끝 무렵 깜빡임(blink)으로 빛의 크기·알파와 빛줄기 개수를 정하고, 연사 중에는 고리를 낸다.
    private void UpdateAura(float dt, bool firing, float blink)
    {
        float eased = 1f - (1f - auraAppear) * (1f - auraAppear);
        float breath = 1f - auraBreath * (1f - heat) * (0.5f + 0.5f * Mathf.Sin(age * auraBreathRate * Mathf.PI * 2f));
        auraSize = Mathf.Lerp(auraIdleSize, auraFireSize, heat) * Mathf.Lerp(0.6f, 1f, eased) * (1f + popScale * 0.5f * flash);
        auraAlpha = Mathf.Lerp(auraIdleAlpha, auraFireAlpha, heat) * eased * blink * breath;
        risingRate = Mathf.Lerp(risingIdleRate, risingFireRate, heat) * auraAppear;

        if (firing && auraPulse != null)
        {
            pulseTimer -= dt;
            if (pulseTimer <= 0f)
            {
                var emit = new ParticleSystem.EmitParams { position = Vector3.zero, applyShapeToPosition = false, startColor = new Color(1f, 1f, 1f, eased * blink) };
                auraPulse.Emit(emit, 1);
                pulseTimer = Mathf.Max(pulseTimer + pulseInterval, 0f);
            }
        }
        // 쉬는 동안에도 남은 간격은 줄어든다: 사이클 사이 한두 프레임 끊겨도 박자가 이어지고, 오래 쉬었다 쏘면 첫 프레임에 바로 고리를 낸다.
        else pulseTimer = Mathf.Max(0f, pulseTimer - dt);
        ApplyAura();
    }

    private void ApplyAura()
    {
        if (auraGlow != null)
        {
            float scale = auraSize / auraQuad;
            auraGlow.transform.localScale = new Vector3(scale, scale, 1f);
            auraGlow.color = new Color(1f, 1f, 1f, Mathf.Clamp01(auraAlpha));
            auraGlow.enabled = auraAlpha > 0.001f;
        }
        if (auraRising != null)
        {
            var emission = auraRising.emission;
            emission.rateOverTimeMultiplier = risingRate;
        }
    }

    private void StartAura()
    {
        ApplyAura();
        if (auraRising != null && !auraRising.isPlaying) auraRising.Play(true);
        if (auraPulse != null && !auraPulse.isPlaying) auraPulse.Play(true);
    }

    private void ClearAura()
    {
        if (auraRising != null) auraRising.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        if (auraPulse != null) auraPulse.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
    }

    private void Place()
    {
        if (player == null) return;
        FixedMotionBody.GetPresentationPose(player.transform, out Vector3 bodyPosition, out _);
        transform.SetPositionAndRotation(bodyPosition, Quaternion.identity);
        transform.localScale = Vector3.one;

        float facing = player.Facing < 0 ? -1f : 1f;
        if (timer != null)
            timer.SetPositionAndRotation(bodyPosition + new Vector3(timerOffset.x * side, timerOffset.y, 0f), Quaternion.identity);

        if (mount == null) return;
        float eased = 1f - (1f - appear) * (1f - appear);
        float size = eased * (1f + popScale * flash);
        if (gun == null)
        {
            mount.localScale = Vector3.zero;
            return;
        }

        // 총의 보간 자세: 위치는 그대로, 축은 렌더 회전만큼 돌린다(좌우 반전은 TransformVector가 담는다).
        FixedMotionBody.GetPresentationPose(gun, out Vector3 gunPosition, out Quaternion gunRotation);
        Quaternion turn = gunRotation * Quaternion.Inverse(gun.rotation);
        Vector2 axis = turn * gun.TransformVector(Vector3.right);
        Vector2 up = turn * gun.TransformVector(Vector3.up);
        if (axis.sqrMagnitude < 0.000001f) axis = new Vector2(facing, 0f);
        axis.Normalize();
        up = up.sqrMagnitude < 0.000001f ? Vector2.up : up.normalized;
        float sign = axis.x < 0f ? -1f : 1f;
        Vector2 anchor = (Vector2)gunPosition + axis * mountForward + up * mountUp;
        float angle = Mathf.Atan2(axis.y * sign, axis.x * sign) * Mathf.Rad2Deg;
        mount.SetPositionAndRotation(anchor, Quaternion.Euler(0f, 0f, angle));
        mount.localScale = new Vector3(sign * size, size, 1f);

        if (barrels == null) return;
        int count = barrels.Length;
        for (int i = 0; i < count; i++)
        {
            var barrel = barrels[i];
            if (barrel == null) continue;
            float a = (spinPhase + (float)i / count) * Mathf.PI * 2f;
            float depth = Mathf.Cos(a);   // 1 = 보는 쪽(앞), -1 = 뒤
            barrel.transform.localPosition = new Vector3(0f, barrelRadius * Mathf.Sin(a), 0f);
            barrel.transform.localScale = new Vector3(1f, Mathf.Lerp(backThickness, frontThickness, (depth + 1f) * 0.5f), 1f);
            barrel.sortingOrder = depth >= 0f ? frontSortingOrder : backSortingOrder;
        }
    }

    private void ApplyTimer(float fraction, float alpha)
    {
        if (timerBar == null || block == null) return;
        timerBar.GetPropertyBlock(block);
        block.SetFloat(FillId, fraction);
        block.SetFloat(FullId, 1f);
        block.SetFloat(FlashId, flash);
        block.SetFloat(AlphaId, alpha);
        block.SetFloat(ClockId, age);
        timerBar.SetPropertyBlock(block);
    }

    private void Unsubscribe()
    {
        if (subscribed && ability != null) ability.PrimaryOverrideChanged -= OnOverrideChanged;
        subscribed = false;
    }

    protected override void ReturnToPool()
    {
        Unsubscribe();
        running = false;
        base.ReturnToPool();
    }

    private void OnDisable()
    {
        Unsubscribe();
    }
}
