using System.Collections.Generic;
using UnityEngine;

// 작열탄 장착 중 총열 가열 표시(2026-11-21 무장 특성 피드백 개편 시안, 사용자 요청 "총열에 불 들어오는 것을 자연스럽게" — 사용자 검수 전).
// 옛 표시(총 위아래에 뜬 주황 LineRenderer 두 줄, Sprites/Default)를 대신한다. Player.prefab은 고치지 않고 실행 중에만 만든다.
// - 덧그림: 별 총(Model/WeaponModel/StarModel)의 자식으로 같은 Sprite를 쓰는 SpriteRenderer를 하나 두고 가열 셰이더(barrelHeatMaterial,
//   Player/IncendiaryBarrelHeat _Mode 0)로 총 실루엣 안의 총열 부분만 달군 금속처럼 빛낸다(총구 쪽부터 뒤로: 노랑-흰 → 주황 → 검붉은).
//   총 Sprite 바로 위(같은 정렬 레이어, 순서 +1)에 그린다. 위치·회전·반동·좌우 반전은 부모를 따라가므로 따로 맞추지 않는다.
// - 열의 시간 변화: 장착 직후 WarmUpTime 동안 서서히 달아오르고, 발사마다 순간 밝아졌다 식고(남은 탄이 줄면 감지),
//   쏜 비율만큼 열이 뒤로 번지며 조금 더 붉어지고, 장착이 끝나면 CoolDownTime 동안 식으며 꺼진다.
// - 총구의 가는 열기·연기 가닥과 불씨: 같은 재질의 복사본(_Mode 1·2)을 쓰는 작은 파티클 두 개(월드 공간, Emit으로만 뿜음).
// 표시 조건은 옛 DrawIncendiaryMount와 같다: 장착한 자리를 들고 있고 PrimaryOverrideWeapon이 IncendiaryRifleWeaponSO이며 총 모형이 보일 때.
// 다른 자리를 들거나 총 모형이 숨으면 식는 연출 없이 바로 끈다. 시간은 Time.deltaTime·Time.time이라 히트스톱 때 함께 멈춘다.
[DefaultExecutionOrder(132)]
public sealed class IncendiaryBarrelVisual : MonoBehaviour
{
    public const string OverlayName = "IncendiaryBarrelHeat";
    const string GunPath = "WeaponModel/StarModel";
    // 별 총(PlayerStarGun.png 1536×576, PPU 640) Sprite 로컬 x(칸): 총열 덮개 뒤쪽 끝. 열은 여기서 0이 된다.
    const float HeatBackLocal = 0.3f;
    const float WarmUpTime = 0.35f, CoolDownTime = 0.4f, PulseTime = 0.14f;
    const float WispInterval = 0.09f, EmberInterval = 0.2f;
    const int ShotEmbers = 3, IgniteEmbers = 6;

    private PlayerController player;
    private PlayerWeaponAbility attack;
    private PlayerStarShotAim rifle;
    private Transform gun;
    private SpriteRenderer gunRenderer, overlay;
    private MaterialPropertyBlock block;
    private Transform fxRoot;
    private ParticleSystem wisps, embers;
    private Material wispMaterial, emberMaterial;
    private float heat, pulse, spent, wispClock, emberClock;
    private int lastAmmo = -1;
    private bool wasMounted;

    public SpriteRenderer Overlay => overlay;
    public float Heat => heat;
    public bool IsShowing => overlay != null && overlay.enabled;

    public void Bind(PlayerController owner, PlayerWeaponAbility ability, PlayerStarShotAim aim)
    {
        player = owner;
        attack = ability;
        rifle = aim;
        gun = owner != null && owner.model != null ? owner.model.transform.Find(GunPath) : null;
        gunRenderer = gun != null ? gun.GetComponent<SpriteRenderer>() : null;
    }

    private void LateUpdate()
    {
        if (attack == null || rifle == null || gun == null || gunRenderer == null || attack.Traits == null) return;
        bool mounted = attack.PrimaryOverrideSetIndex == attack.CurrentSetIndex
            && attack.PrimaryOverrideWeapon is IncendiaryRifleWeaponSO;
        bool visible = rifle.Muzzle != null && rifle.IsModelVisible && gunRenderer.enabled;
        if (!visible)
        {
            heat = pulse = 0f;
            lastAmmo = -1;
            wasMounted = false;
            Hide();
            return;
        }

        float dt = Time.deltaTime;
        heat = Mathf.MoveTowards(heat, mounted ? 1f : 0f, dt / (mounted ? WarmUpTime : CoolDownTime));
        int ammo = attack.Traits.IncendiaryAmmo;
        bool ignited = mounted && !wasMounted;
        if (mounted)
        {
            if (!ignited && lastAmmo >= 0 && ammo < lastAmmo) pulse = 1f;   // 발사(PlayerStarShotAim이 같은 프레임 앞서 탄을 줄였다)
            spent = 1f - Mathf.Clamp01(ammo / (float)PlayerWeaponTraits.IncendiaryCapacity);
            lastAmmo = ammo;
        }
        else lastAmmo = -1;
        wasMounted = mounted;
        if (heat <= 0f && !mounted)
        {
            Hide();
            return;
        }

        if (!EnsureOverlay()) return;
        bool fired = pulse >= 1f;
        pulse = Mathf.Max(0f, pulse - dt / PulseTime);
        float shown = heat * heat * (3f - 2f * heat);
        overlay.enabled = true;
        overlay.sprite = gunRenderer.sprite;
        overlay.flipX = gunRenderer.flipX;
        overlay.flipY = gunRenderer.flipY;
        overlay.sortingLayerID = gunRenderer.sortingLayerID;
        overlay.sortingOrder = gunRenderer.sortingOrder + 1;
        overlay.GetPropertyBlock(block);
        block.SetFloat("_Mode", 0f);
        block.SetFloat("_Tip", gun.InverseTransformPoint(rifle.BarrelTip).x);
        block.SetFloat("_Back", HeatBackLocal);
        block.SetFloat("_Heat", shown);
        block.SetFloat("_Pulse", pulse * pulse);
        block.SetFloat("_Spent", spent);
        block.SetFloat("_Clock", Time.time);
        overlay.SetPropertyBlock(block);

        EmitMuzzleFx(shown, mounted, ignited, fired, dt);
    }

    // 총구 열기 가닥·불씨: 달아오른 만큼 일정 간격으로, 발사·점화 순간엔 불씨를 한 번에 더.
    private void EmitMuzzleFx(float shown, bool mounted, bool ignited, bool fired, float dt)
    {
        if (!EnsureParticles()) return;
        Vector3 tip = PresentedTip();
        Vector2 heading = rifle.BarrelDirection;
        if (ignited || fired) EmitEmbers(tip, heading, ignited ? IgniteEmbers : ShotEmbers, ignited ? 1.4f : 1f);
        if (shown < 0.3f) return;
        // 프레임이 크게 끊겨도 밀린 만큼 한꺼번에 뿜지 않게 한 간격까지만 쌓는다.
        wispClock = Mathf.Max(wispClock - dt * shown, -WispInterval);
        if (wispClock <= 0f)
        {
            wispClock += WispInterval;
            var p = new ParticleSystem.EmitParams
            {
                position = tip + (Vector3)(Random.insideUnitCircle * 0.04f),
                velocity = Vector2.up * Random.Range(0.8f, 1.3f) + heading * Random.Range(0.1f, 0.35f) + Vector2.right * Random.Range(-0.12f, 0.12f),
                startColor = new Color(1f, 1f, 1f, mounted ? 1f : shown),
            };
            wisps.Emit(p, 1);
        }
        emberClock = Mathf.Max(emberClock - dt * shown, -EmberInterval);
        if (emberClock <= 0f)
        {
            emberClock += EmberInterval * Random.Range(0.7f, 1.3f);
            EmitEmbers(tip, heading, 1, 0.7f);
        }
    }

    private void EmitEmbers(Vector3 tip, Vector2 heading, int count, float speed)
    {
        for (int i = 0; i < count; i++)
        {
            var p = new ParticleSystem.EmitParams
            {
                position = tip + (Vector3)(Random.insideUnitCircle * 0.05f),
                velocity = (heading * Random.Range(0.6f, 2.2f) + Vector2.up * Random.Range(0.4f, 1.4f) + Random.insideUnitCircle * 0.6f) * speed,
            };
            embers.Emit(p, 1);
        }
    }

    // 총구 끝(물리 자세)에 Player의 화면 보간 위치 차이를 더한다(파티클은 월드 공간에 바로 남으므로).
    private Vector3 PresentedTip()
    {
        Vector3 tip = rifle.BarrelTip;
        if (player == null) return tip;
        FixedMotionBody.GetPresentationPose(player.transform, out var presented, out _);
        return tip + (presented - player.transform.position);
    }

    private bool EnsureOverlay()
    {
        if (overlay != null) return true;
        var material = WeaponTraitFeedbackSO.BarrelHeatMaterial;
        if (material == null) return false;
        var obj = new GameObject(OverlayName) { hideFlags = HideFlags.DontSave };
        obj.layer = gun.gameObject.layer;
        obj.transform.SetParent(gun, false);
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
        if (wisps != null && embers != null) return true;
        var material = WeaponTraitFeedbackSO.BarrelHeatMaterial;
        if (material == null) return false;
        fxRoot = new GameObject("IncendiaryBarrelFx") { hideFlags = HideFlags.DontSave }.transform;
        fxRoot.SetParent(transform, false);
        wispMaterial = new Material(material) { name = "IncendiaryBarrelWisp (Instance)", hideFlags = HideFlags.DontSave };
        wispMaterial.SetFloat("_Mode", 1f);
        emberMaterial = new Material(material) { name = "IncendiaryBarrelEmber (Instance)", hideFlags = HideFlags.DontSave };
        emberMaterial.SetFloat("_Mode", 2f);
        wisps = NewSystem("Wisps", wispMaterial, 6, 24, new Vector2(0.55f, 0.85f), -0.05f);
        var main = wisps.main;
        main.startSize3D = true;
        main.startSizeX = new ParticleSystem.MinMaxCurve(0.2f, 0.28f);
        main.startSizeY = new ParticleSystem.MinMaxCurve(0.5f, 0.75f);
        main.startSizeZ = 1f;
        wisps.GetComponent<ParticleSystemRenderer>().pivot = new Vector3(0f, 0.5f, 0f);   // 가닥 아래 끝이 총구에서 시작하게
        embers = NewSystem("Embers", emberMaterial, 7, 32, new Vector2(0.3f, 0.6f), -0.12f);
        main = embers.main;
        main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.09f);
        var limit = embers.limitVelocityOverLifetime;
        limit.enabled = true;
        limit.limit = 100f;
        limit.dampen = 0f;
        limit.drag = 3f;
        limit.multiplyDragByParticleSize = false;
        limit.multiplyDragByParticleVelocity = false;
        wisps.Play();
        embers.Play();
        return true;
    }

    private ParticleSystem NewSystem(string name, Material material, int order, int maxParticles, Vector2 life, float gravity)
    {
        var obj = new GameObject(name) { hideFlags = HideFlags.DontSave };
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
        heat = pulse = 0f;
        lastAmmo = -1;
        wasMounted = false;
        Hide();
    }

    private void OnDestroy()
    {
        if (overlay != null) Destroy(overlay.gameObject);
        if (fxRoot != null) Destroy(fxRoot.gameObject);
        if (wispMaterial != null) Destroy(wispMaterial);
        if (emberMaterial != null) Destroy(emberMaterial);
    }
}
