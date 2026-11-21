using System.Collections.Generic;
using UnityEngine;

// 레일건 총열 과열·충전 표시(2026-11-21 무장 특성 3차, 사용자 요청 "레일건은 과부하 게이지를 총열 자체가 과열되게 만들어 줘" — 사용자 검수 전 시안).
// 옛 표시(PlayerRailgunAim.DrawHeat: 총신 축 위 LineRenderer 한 줄, Sprites/Default)를 대신한다. 게이지 숫자·배터리는 HUD 담당(PlayerTraitGaugeVisual·WeaponTraitHud)이 맡는다.
// PlayerRailgunAim이 레일건 모형(Model/WeaponModel/RailgunModel)에 실행 중에 붙인다. Player.prefab은 고치지 않는다.
// - 덧그림: 모형의 자식으로 같은 Sprite를 쓰는 SpriteRenderer(RailgunBarrelHeat)를 하나 두고 Player/RailBarrelHeat(_Mode 0)로 총 실루엣 안의 총열만 칠한다.
//   열(PlayerWeaponTraits.Heat)이 오를수록 총구 끝부터 검붉은 → 붉은 → 주황-흰으로 달아오르고 코일 고리가 앞에서부터 차례로 달아오른다.
//   과열 잠금 중에는 총열 전체가 새빨갛게 일렁인다. 레일건 충전 지속 중에는 레일 안쪽 면을 따라 분홍-청록 전류가 흐르고, 충전 준비 중에는 코일이
//   뒤에서 앞으로 차례로 켜진다. 기본 사격 순간 총구 쪽이 번쩍, 충전 추가타를 쏠 때마다 코일이 분홍으로 번쩍인다.
//   4차(2026-11-21, 사용자 요청 "레일건 충전 지속시간도 이펙트로 표시"): 충전 중 레일은 남은 시간만큼만 뒤에서부터 물들고(끝에 밝은 띠),
//   코일 고리 세 개가 남은 1/3마다 앞에서부터 꺼지며, 남은 1.25초부터 깜빡인다. 몸 둘레의 큰 표시는 RailChargeAura가 맡는다.
//   위치·회전·반동·좌우 반전은 부모를 따라가므로 따로 맞추지 않는다(셰이더 좌표가 Sprite 로컬이다).
// - 입자(월드 공간, Emit으로만 뿜음, Player 아래에 둬 총이 숨어도 남은 입자는 끝까지 보인다):
//   식는 동안(사격을 멈추고 0.5초 뒤 열이 내려갈 때) 총구·통풍구에서 흰 증기 몇 가닥, 과열 잠금 중에는 연기 덩어리와 불티,
//   충전 지속 중에는 총구에 작은 분홍·청록 전류 불꽃. 냉각 시작·과열·잠금 해제 순간의 큰 묶음은 표 큐(RailVent·RailOverheat·RailReady)가 낸다.
// - 재질은 Player의 레일건 무기 에셋(RailgunWeaponSO.barrelHeatMaterial·steamMaterial·sparkMaterial·smokeMaterial)에서 읽는다(생성 도구 RailgunTraitSetup).
// - 보이는 값은 규칙 값을 그대로 쓰지 않고 짧게 따라간다(잠금 해제 순간 열이 0이 되어도 붉은빛이 잠깐 식으며 꺼진다).
//   시간은 Time.deltaTime·Time.time이라 히트스톱 때 함께 멈춘다. 모형이 꺼지면 LateUpdate가 돌지 않고 덧그림도 함께 숨는다.
[DefaultExecutionOrder(125)]
public sealed class RailgunBarrelVisual : MonoBehaviour
{
    public const string OverlayName = "RailgunBarrelHeat";
    public const string FxName = "RailgunBarrelFx";
    // 레일건 총(PlayerRailgun.png) 모형 좌표(칸, tools/Art/PlayerRailgunModel.py). Sprite 로컬 = 모형 좌표 / ModelScale.
    const float ModelScale = 1.3f;
    const float RailBack = 0.44f, RailTip = 1.76f, RailInner = 0.105f, RailOuter = 0.2f;
    static readonly Vector2[] Vents = { new(0.31f, 0.08f), new(0.4f, 0.08f) };   // 몸통 통풍구 두 개의 위 끝
    // ===== 판단값(사용자 검수 대상) =====
    const float HeatRise = 8f, HeatFall = 1.4f;      // 보이는 열이 규칙 값을 따라가는 빠르기(초당)
    const float LockHeat = 0.55f;                    // 잠금 중 열 층의 값(풀릴 때 붉은빛에서 식으며 꺼진다)
    const float LockRise = 5f, LockFall = 1.6f;      // 잠금 덧그림이 켜지는(0.2초)·꺼지는(약 0.6초) 빠르기
    const float LockDim = 0.75f;                     // 잠금이 끝날 무렵의 잠금 층 세기(10초 동안 1 → 이 값)
    const float ChargeBlinkFrom = 1.25f;             // 충전이 이만큼 남으면 깜빡이기 시작한다(초, RailChargeAura와 같은 값)
    const float ChargeBlinkLow = 0.3f;
    const float PulseTime = 0.12f, EchoTime = 0.16f;
    const float CoolSteamFrom = 0.12f;               // 이 열 아래로는 김을 내지 않는다
    const float CoolSteamSlow = 0.5f, CoolSteamFast = 0.18f;   // 식을 때 김 간격(열 낮음 ↔ 높음)
    const float LockSmokeInterval = 0.17f, LockEmberInterval = 0.07f, LockHissInterval = 0.42f;
    const float ChargeArcInterval = 0.16f;

    private PlayerRailgunAim aim;
    private PlayerController player;
    private PlayerWeaponAbility attack;
    private RailgunWeaponSO rail;
    private SpriteRenderer gunRenderer, overlay;
    private MaterialPropertyBlock block;
    private Transform fxRoot;
    private ParticleSystem steam, sparks, smoke;
    private float heat, lockShown, charge, chargeUp, pulse, echo, lastTraitHeat;
    private float chargeLeft = 1f, chargeBlink = 1f, chargeBlinkPhase;
    private bool auraReady;   // 몸 둘레 충전 표시(RailChargeAura)를 붙였는가
    private float chargeUpStart, chargeUpDuration = -1f;
    private float steamClock, smokeClock, emberClock, hissClock, arcClock;
    private bool snap = true, wasLocked;

    public SpriteRenderer Overlay => overlay;
    public bool IsShowing => overlay != null && overlay.enabled;
    public float ShownHeat => heat;
    public float ShownLock => lockShown;
    public float ShownCharge => charge;
    public float ShownChargeUp => chargeUp;
    /// <summary>덧그림이 보여 주는 남은 충전 비율(0~1, 물든 레일 길이).</summary>
    public float ShownChargeLeft => chargeLeft;
    public bool IsChargeBlinking => chargeBlink < 1f;

    /// <summary>기본 사격 순간(RailgunWeaponSO.Fire): 총구 쪽이 잠깐 더 달아오르고, 뜨거우면 불티가 튄다.</summary>
    public void Shot()
    {
        pulse = 1f;
        if (heat >= 0.45f && Bind() && EnsureParticles())
            EmitSparks(Muzzle(), Heading(), 2, HotSpark(), 1.4f, 0.04f);
    }

    /// <summary>충전 추가타를 쏜 순간(RailEchoShot): 코일·레일 안쪽 면이 분홍으로 번쩍, 총구에 전류 불꽃.</summary>
    public void Echo()
    {
        echo = 1f;
        if (Bind() && EnsureParticles())
            EmitSparks(Muzzle(), Heading(), 3, ChargeSpark(), 2.2f, 0.05f);
    }

    /// <summary>레일건 충전 준비 시작(RailgunChargeWeaponSO.OnWindupStarted): 준비 시간 동안 코일이 뒤에서 앞으로 차례로 켜진다.</summary>
    public void BeginChargeUp(float duration)
    {
        chargeUpStart = Time.time;
        chargeUpDuration = Mathf.Max(0.05f, duration);
    }

    private void OnEnable() => snap = true;

    private void LateUpdate()
    {
        if (!Bind())
        {
            Hide();
            return;
        }
        var traits = attack.Traits;
        float dt = Time.deltaTime;
        // 기술을 거치지 않고 충전이 걸린 경우(검증 도구 등)에도 몸 둘레 충전 표시가 있게 한다
        if (!auraReady && traits.IsRailCharged) auraReady = RailChargeAura.Ensure(player) != null;
        bool locked = traits.OverheatRemaining > 0f;
        float traitHeat = locked ? 1f : traits.Heat;
        bool cooling = !locked && traitHeat < lastTraitHeat - 1e-5f && traitHeat > 0.01f;
        lastTraitHeat = traitHeat;

        float heatTarget = locked ? LockHeat : traitHeat;
        float lockTarget = locked ? Mathf.Lerp(LockDim, 1f, traits.OverheatRemaining / PlayerWeaponTraits.OverheatDuration) : 0f;
        float chargeTarget = traits.IsRailCharged ? 1f : 0f;
        // 남은 충전 = 물든 레일 길이(끝나면 길이가 0이 되며 자연히 사라진다), 끝나기 직전 깜빡임
        float remaining = traits.ChargeRemaining;
        chargeLeft = traits.IsRailCharged ? Mathf.Clamp01(remaining / PlayerWeaponTraits.ChargeDuration) : 0f;
        if (traits.IsRailCharged && remaining < ChargeBlinkFrom)
        {
            chargeBlinkPhase += Mathf.Lerp(10f, 4f, remaining / ChargeBlinkFrom) * dt;
            chargeBlink = Mathf.Repeat(chargeBlinkPhase, 1f) < 0.55f ? 1f : ChargeBlinkLow;
        }
        else
        {
            chargeBlinkPhase = 0f;
            chargeBlink = 1f;
        }
        if (snap)
        {
            // 총을 다시 든 순간에는 지금 상태에서 바로 시작한다(꺼져 있던 동안의 변화를 다시 보여 주지 않는다).
            heat = heatTarget;
            lockShown = lockTarget;
            charge = chargeTarget;
            wasLocked = locked;
            snap = false;
        }
        heat = Mathf.MoveTowards(heat, heatTarget, dt * (heatTarget > heat ? HeatRise : HeatFall));
        lockShown = Mathf.MoveTowards(lockShown, lockTarget, dt * (lockTarget > lockShown ? LockRise : LockFall));
        charge = Mathf.MoveTowards(charge, chargeTarget, dt * 5f);
        if (chargeUpDuration > 0f)
        {
            float progress = (Time.time - chargeUpStart) / chargeUpDuration;
            if (attack.AttackPhase == AbilityState.Windup && progress < 1.5f) chargeUp = Mathf.Clamp01(progress);
            else chargeUpDuration = -1f;
        }
        if (chargeUpDuration <= 0f) chargeUp = Mathf.MoveTowards(chargeUp, 0f, dt * 4f);
        pulse = Mathf.Max(0f, pulse - dt / PulseTime);
        echo = Mathf.Max(0f, echo - dt / EchoTime);
        if (locked && !wasLocked) smokeClock = emberClock = hissClock = 0f;   // 잠금 순간부터 바로 연기·불티
        wasLocked = locked;

        DrawOverlay();
        if (EnsureParticles()) EmitStateFx(locked, cooling, dt);
    }

    private void DrawOverlay()
    {
        bool visible = gunRenderer.enabled && (heat > 0.002f || lockShown > 0.002f || charge > 0.002f || chargeUp > 0.002f || pulse > 0f || echo > 0f);
        if (!visible || !EnsureOverlay())
        {
            Hide();
            return;
        }
        overlay.enabled = true;
        overlay.sprite = gunRenderer.sprite;
        overlay.flipX = gunRenderer.flipX;
        overlay.flipY = gunRenderer.flipY;
        overlay.sortingLayerID = gunRenderer.sortingLayerID;
        overlay.sortingOrder = gunRenderer.sortingOrder + 1;   // 총 바로 위(코어와 같은 순서 — 코어 자리는 덮지 않는다). 충전 빛(+2)은 그 위에 더해진다
        overlay.GetPropertyBlock(block);
        block.SetFloat("_Mode", 0f);
        block.SetFloat("_Heat", heat);
        block.SetFloat("_Lock", lockShown);
        block.SetFloat("_Charge", charge);
        block.SetFloat("_ChargeUp", chargeUp);
        block.SetFloat("_ChargeLeft", chargeLeft);
        block.SetFloat("_ChargeBlink", chargeBlink);
        block.SetFloat("_Pulse", pulse * pulse);
        block.SetFloat("_Echo", echo * echo);
        block.SetFloat("_Clock", Time.time);
        overlay.SetPropertyBlock(block);
    }

    // ===== 입자 =====
    private void EmitStateFx(bool locked, bool cooling, float dt)
    {
        if (!gunRenderer.enabled) return;
        Vector2 heading = Heading();
        if (locked)
        {
            // 과열 잠금: 총구·통풍구에서 연기 덩어리, 총열을 따라 불티, 통풍구에서 가끔 김
            smokeClock -= dt;
            if (smokeClock <= 0f)
            {
                smokeClock = Mathf.Max(smokeClock + LockSmokeInterval * Random.Range(0.75f, 1.25f), -LockSmokeInterval);
                bool fromMuzzle = Random.value < 0.6f;
                Vector3 at = fromMuzzle ? Muzzle() : Vent(Random.Range(0, Vents.Length));
                Vector2 v = Vector2.up * Random.Range(0.6f, 1.05f) + heading * (fromMuzzle ? Random.Range(0.2f, 0.5f) : 0.05f)
                            + Random.insideUnitCircle * 0.15f;
                smoke.Emit(new ParticleSystem.EmitParams { position = at + (Vector3)(Random.insideUnitCircle * 0.05f), velocity = v }, 1);
            }
            emberClock -= dt;
            if (emberClock <= 0f)
            {
                emberClock = Mathf.Max(emberClock + LockEmberInterval * Random.Range(0.6f, 1.4f), -LockEmberInterval);
                EmitSparks(BarrelPoint(), heading, 1, HotSpark(), 1.6f, 0.03f, true);
            }
            hissClock -= dt;
            if (hissClock <= 0f)
            {
                hissClock = Mathf.Max(hissClock + LockHissInterval * Random.Range(0.7f, 1.3f), -LockHissInterval);
                EmitSteam(Vent(Random.Range(0, Vents.Length)), heading, 0.75f);
            }
            return;
        }
        if (cooling && heat > CoolSteamFrom)
        {
            // 식는 동안: 열이 높을수록 자주, 총구에서 더 많이(통풍구 35%)
            float interval = Mathf.Lerp(CoolSteamSlow, CoolSteamFast, Mathf.InverseLerp(CoolSteamFrom, 1f, heat));
            steamClock -= dt;
            if (steamClock <= 0f)
            {
                steamClock = Mathf.Max(steamClock + interval * Random.Range(0.8f, 1.2f), -interval);
                bool fromMuzzle = Random.value < 0.65f;
                EmitSteam(fromMuzzle ? Muzzle() : Vent(Random.Range(0, Vents.Length)), heading, Mathf.Lerp(0.6f, 1f, heat));
            }
        }
        if (charge > 0.05f)
        {
            // 충전 지속 중: 남은 충전 끝(물든 레일의 앞 끝)에서 작은 전류 불꽃 — 시간이 지날수록 총구에서 뒤로 물러난다
            arcClock -= dt * charge;
            if (arcClock <= 0f)
            {
                arcClock = Mathf.Max(arcClock + ChargeArcInterval * Random.Range(0.6f, 1.4f), -ChargeArcInterval);
                Vector3 at = RailPoint(Mathf.Clamp01(chargeLeft - Random.Range(0f, 0.15f)));
                EmitSparks(at, heading, 1, ChargeSpark(), 1.1f, 0.03f);
            }
        }
    }

    private void EmitSteam(Vector3 at, Vector2 heading, float alpha)
    {
        var p = new ParticleSystem.EmitParams
        {
            position = at + (Vector3)(Random.insideUnitCircle * 0.03f),
            velocity = Vector2.up * Random.Range(0.45f, 0.8f) + heading * Random.Range(0.05f, 0.25f) + Vector2.right * Random.Range(-0.1f, 0.1f),
            startColor = new Color(1f, 1f, 1f, alpha),
        };
        steam.Emit(p, 1);
    }

    private void EmitSparks(Vector3 at, Vector2 heading, int count, ParticleSystem.MinMaxGradient colors, float speed, float spread, bool upward = false)
    {
        for (int i = 0; i < count; i++)
        {
            Vector2 v = upward
                ? Vector2.up * Random.Range(0.6f, 1.6f) + Random.insideUnitCircle * 1.1f
                : heading * Random.Range(0.4f, 1f) + Vector2.up * Random.Range(0.1f, 0.6f) + Random.insideUnitCircle * 0.6f;
            var p = new ParticleSystem.EmitParams
            {
                position = at + (Vector3)(Random.insideUnitCircle * spread),
                velocity = v * speed,
                startColor = colors.Evaluate(Random.value, Random.value),
            };
            sparks.Emit(p, 1);
        }
    }

    // 불티: 주황·붉은·노랑 / 전류: 분홍·청록(레일건 충전색)
    private static ParticleSystem.MinMaxGradient HotSpark() =>
        new(new Color(1f, 0.32f, 0.06f, 1f), new Color(1f, 0.75f, 0.3f, 1f));

    private static ParticleSystem.MinMaxGradient ChargeSpark() =>
        new(new Color(1f, 0.43f, 0.82f, 1f), new Color(0.3f, 0.88f, 1f, 1f));

    // ===== 위치(물리 자세 + Player 화면 보간 차이 — 파티클은 월드 공간에 바로 남으므로) =====
    private Vector3 Presented(Vector3 world)
    {
        if (player == null) return world;
        FixedMotionBody.GetPresentationPose(player.transform, out var presented, out _);
        return world + (presented - player.transform.position);
    }

    private Vector3 Muzzle() => Presented(aim != null && aim.Muzzle != null ? aim.Muzzle.position : transform.TransformPoint(new Vector3(RailTip / ModelScale, 0f, 0f)));

    private Vector3 Vent(int index) => Presented(transform.TransformPoint(Vents[index] / ModelScale));

    // 레일 위·아래 면의 점. along: 0 = 레일 뒤 끝, 1 = 총구 끝
    private Vector3 RailPoint(float along)
    {
        float x = Mathf.Lerp(RailBack, RailTip, along);
        float y = Random.Range(RailInner, RailOuter) * (Random.value < 0.5f ? -1f : 1f);
        return Presented(transform.TransformPoint(new Vector3(x / ModelScale, y / ModelScale, 0f)));
    }

    // 잠금 중 불티가 튀는 곳: 총구 쪽에 몰린다
    private Vector3 BarrelPoint() => RailPoint(1f - Random.value * Random.value);

    private Vector2 Heading()
    {
        Vector2 axis = transform.TransformVector(Vector3.right);
        return axis.sqrMagnitude > 1e-6f ? axis.normalized : Vector2.right;
    }

    // ===== 준비 =====
    private bool Bind()
    {
        if (aim == null) aim = GetComponent<PlayerRailgunAim>();
        if (gunRenderer == null) gunRenderer = GetComponent<SpriteRenderer>();
        if (player == null) player = GetComponentInParent<PlayerController>(true);
        if (attack == null && player != null && player.AbilityManager != null) attack = player.AbilityManager.AttackAbility;
        if (rail == null && player != null) rail = RailgunWeaponSO.FindFor(player);
        return attack != null && attack.Traits != null && gunRenderer != null;
    }

    private bool EnsureOverlay()
    {
        if (overlay != null) return true;
        var material = rail != null ? rail.barrelHeatMaterial : null;
        if (material == null) return false;
        var obj = new GameObject(OverlayName) { hideFlags = HideFlags.DontSave };
        obj.layer = gameObject.layer;
        obj.transform.SetParent(transform, false);
        overlay = obj.AddComponent<SpriteRenderer>();
        overlay.sharedMaterial = material;
        overlay.color = Color.white;
        overlay.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        overlay.receiveShadows = false;
        block = new MaterialPropertyBlock();
        return true;
    }

    private bool EnsureParticles()
    {
        if (steam != null && sparks != null && smoke != null) return true;
        if (rail == null || rail.steamMaterial == null || rail.sparkMaterial == null || rail.smokeMaterial == null) return false;
        if (fxRoot == null)
        {
            fxRoot = new GameObject(FxName) { hideFlags = HideFlags.DontSave }.transform;
            fxRoot.SetParent(player != null ? player.transform : transform, false);
        }
        steam = NewSystem("Steam", rail.steamMaterial, 9, 48, new Vector2(0.7f, 1.1f), -0.03f);
        var main = steam.main;
        main.startSize3D = true;
        main.startSizeX = new ParticleSystem.MinMaxCurve(0.26f, 0.38f);
        main.startSizeY = new ParticleSystem.MinMaxCurve(0.7f, 1.0f);
        main.startSizeZ = 1f;
        steam.GetComponent<ParticleSystemRenderer>().pivot = new Vector3(0f, 0.5f, 0f);   // 가닥 아래 끝이 나오는 곳에서 시작하게
        Drag(steam, 1.2f);

        sparks = NewSystem("Sparks", rail.sparkMaterial, 11, 80, new Vector2(0.25f, 0.5f), 0.35f);
        main = sparks.main;
        main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.09f);
        Drag(sparks, 4f);
        var sparkRenderer = sparks.GetComponent<ParticleSystemRenderer>();
        sparkRenderer.renderMode = ParticleSystemRenderMode.Stretch;
        sparkRenderer.velocityScale = 0.03f;
        sparkRenderer.lengthScale = 1f;
        sparkRenderer.cameraVelocityScale = 0f;

        smoke = NewSystem("Smoke", rail.smokeMaterial, 8, 48, new Vector2(0.8f, 1.2f), -0.08f);
        main = smoke.main;
        main.startSize = new ParticleSystem.MinMaxCurve(0.32f, 0.5f);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        Drag(smoke, 0.9f);
        var size = smoke.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 1f, 0f, 2.4f), new Keyframe(1f, 2.4f, 0.4f, 0f)));
        var spin = smoke.rotationOverLifetime;
        spin.enabled = true;
        spin.z = new ParticleSystem.MinMaxCurve(-0.6f, 0.6f);

        steam.Play();
        sparks.Play();
        smoke.Play();
        return true;
    }

    private static void Drag(ParticleSystem ps, float drag)
    {
        var limit = ps.limitVelocityOverLifetime;   // 일정한 감속(Unity drag는 속도를 초당 drag만큼 깎는다)
        limit.enabled = true;
        limit.limit = 100f;
        limit.dampen = 0f;
        limit.drag = drag;
        limit.multiplyDragByParticleSize = false;
        limit.multiplyDragByParticleVelocity = false;
    }

    private ParticleSystem NewSystem(string name, Material material, int order, int maxParticles, Vector2 life, float gravity)
    {
        var obj = new GameObject(name) { hideFlags = HideFlags.DontSave };
        obj.layer = gameObject.layer;
        obj.transform.SetParent(fxRoot, false);
        var ps = obj.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.playOnAwake = false;
        main.loop = true;
        main.duration = 1f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(life.x, life.y);
        main.startSpeed = 0f;
        main.startRotation = 0f;
        main.startColor = Color.white;
        main.gravityModifier = gravity;
        main.maxParticles = maxParticles;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.scalingMode = ParticleSystemScalingMode.Local;
        var emission = ps.emission;
        emission.enabled = false;   // Emit으로만 뿜는다
        var shape = ps.shape;
        shape.enabled = false;
        var renderer = obj.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = material;
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.alignment = ParticleSystemRenderSpace.World;
        renderer.sortingLayerName = "Particle";
        renderer.sortingOrder = order;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.maxParticleSize = 10f;
        renderer.SetActiveVertexStreams(new List<ParticleSystemVertexStream>
        {
            ParticleSystemVertexStream.Position, ParticleSystemVertexStream.Color, ParticleSystemVertexStream.UV,
            ParticleSystemVertexStream.AgePercent, ParticleSystemVertexStream.StableRandomX,
        });
        return ps;
    }

    private void Hide()
    {
        if (overlay != null) overlay.enabled = false;
    }

    private void OnDisable()
    {
        pulse = echo = chargeUp = 0f;
        chargeUpDuration = -1f;
        Hide();
    }

    private void OnDestroy()
    {
        if (overlay != null) Destroy(overlay.gameObject);
        if (fxRoot != null) Destroy(fxRoot.gameObject);
    }
}
