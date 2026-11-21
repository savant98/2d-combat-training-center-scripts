using System.Collections.Generic;
using UnityEngine;

// Player 주변 무장 특성 아이콘(월드 공간, 2026-11-21 사용자 요청).
// 요청 원문 요지: "해머 쉴드 UI는 쉴드 모양 아이콘이 차오르게(글씨 없음), Player 바로 아래로 / 라이플 탄창 아이콘은 더 적절한 위치에 글씨 없이 직관적인 탄창 디자인 /
// 검(필살 n/3)·레일건(열 %)의 Player 주변 표시도 같은 방식(글씨 없는 아이콘)으로 교체".
// 3차 요청(2026-11-21) 요지: "재장전 중 탄창 아이콘은 빨갛게 차오르게, 다른 무장을 들어도 재장전 중이면 보여 줄 것 / 작열탄 10발 /
// 검 필살은 스택 3개 대신 일본도 모양 게이지 하나를 1·2·3단계로 / 레일건 열은 발 아래 막대 대신 탄창처럼 총 쪽에 세로 배터리(칸 10개),
// 다른 무기를 들어도 열·잠금·충전 중이면 보여 줄 것".
// - 발 아래 줄(몸 가운데 기준 y -1.05, 칸 높이 0.5칸, 가운데 정렬로 왼쪽부터 나란히):
//   방어막 칩(해머를 들었을 때 늘 — 0%면 빈 윤곽, 다른 무장이어도 방어막이 남아 있으면 표시) + 검을 들면 일본도 게이지
//   + 라이플 장전 칩(작은 빨간 탄창 — 장전 중인데 총 쪽 탄창이 보이지 않을 때: 다른 무장, 작열탄·기관총 장착 중)
//   + 레일건 열 칩(작은 배터리 5칸 — 열이 남았거나 과열 잠금·충전 중인데 레일건을 들지 않았을 때).
// - 총 쪽(보는 방향 앞, 총 아래: x = 보는 방향 × 0.88, y -0.5): 지금 든 원거리 무장의 칩 하나 — 라이플 탄창(30발, 장전 중 빨강) /
//   작열탄 장착 중이면 작열탄 10발 + 장착 남은 시간 막대 / 레일건이면 세로 열 배터리(칸 10개 = RailShotsToOverheat).
//   기관총 장착 중에는 숨긴다(MachineGunMountVisual의 남은 시간 막대가 같은 쪽 위에 있다). 방향을 바꾸면 0.14초 동안 반대쪽으로 미끄러진다(배터리 방식),
//   몸 앞을 지나는 동안에는 옅어진다. 가득 찬 탄창·식은 배터리가 2.5초 동안 변화가 없으면 조금 옅어진다(시야 방해를 줄이는 판단값).
// - 그림: 흰 사각형 Sprite + Player/TraitGauge 재질(WeaponTraitFeedbackSO.GaugeMaterial) + MaterialPropertyBlock. 모양·사건 연출은 셰이더(PlayerTraitGaugeCommon.hlsl)가
//   상태 값과 사건 시각으로 그린다. 상태 계산은 TraitGaugeModel(아래, PlayerHUD 띠 WeaponTraitHud와 공용)이다.
// - 자리: Player 루트 아래(FacingModel 밖, 좌우 반전 영향 없음) 'TraitGauge' 아래 로컬 좌표. FixedMotionBody가 렌더 때 보간 위치로 함께 옮긴다.
//   Sorting Layer 'Particle' 순서 10(스킬 배터리 9 위). 시간은 Time.deltaTime·Time.time(히트스톱 때 함께 멈춘다).
//   사망·외형 숨김 때는 페이드 없이 바로 숨긴다(사망 연출이 Time.timeScale = 0으로 멈춰 페이드가 진행되지 않는다).
// Player.prefab은 건드리지 않는다. PlayerWeaponTraitVisual.Bind가 실행 중에 이 컴포넌트를 붙이고 Bind를 부른다.
[DefaultExecutionOrder(133)]
public sealed class PlayerTraitGaugeVisual : MonoBehaviour
{
    private static readonly int KindId = Shader.PropertyToID("_Kind");
    private static readonly int StateAId = Shader.PropertyToID("_StateA");
    private static readonly int StateBId = Shader.PropertyToID("_StateB");
    private static readonly int FrameId = Shader.PropertyToID("_Frame");
    private static readonly int AlphaId = Shader.PropertyToID("_Alpha");
    private static readonly int ClockId = Shader.PropertyToID("_Clock");

    // ===== 배치(몸 가운데 기준 월드 칸) — 사용자 검수 전 판단값 =====
    /// <summary>발 아래 줄 가운데(캡슐 반높이 0.75 + 틈 0.05 + 칸 반높이 0.25).</summary>
    public const float FootRowY = -1.05f;
    /// <summary>발 아래 칩의 아이콘 1단위(세로 반높이) 크기. 칸 높이 = 0.5칸.</summary>
    public const float FootIconScale = 0.25f;
    /// <summary>일본도 게이지의 아이콘 1단위. 칼 길이 3.36단위 → 약 0.81칸, 칼날 굵기 약 0.065칸.</summary>
    public const float KatanaIconScale = 0.24f;
    /// <summary>발 아래 작은 상태 칩(장전 탄창·열 배터리)의 아이콘 1단위. 높이 약 0.37~0.38칸.</summary>
    public const float FootMiniIconScale = 0.2f;
    public const float FootGap = 0.1f;
    /// <summary>총 쪽 칩 자리(x는 보는 방향 쪽). 저장 기술 슬롯(x 1.1~)과 겹치지 않게 0.88로 잡았다.</summary>
    public const float GunSideX = 0.88f, GunSideY = -0.5f;
    /// <summary>총 쪽 칩 아이콘 1단위 크기. 탄창 실루엣 세로 1.86단위 → 약 0.66칸, 배터리 세로 1.85단위 → 약 0.66칸.</summary>
    public const float GunSideIconScale = 0.355f;
    /// <summary>열 배터리 칸 수: 총 쪽은 과열까지의 발수(10), 발 아래 작은 칩은 5칸(한 칸 = 2발 — 칸이 2px보다 작아지지 않게).</summary>
    public const int RailCellsGunSide = PlayerWeaponTraits.RailShotsToOverheat, RailCellsMini = 5;
    public const int SortingOrder = 10;
    private const string SortingLayer = "Particle";
    private const float FadeTime = 0.12f, SideSwitchTime = 0.14f, RowSlideSpeed = 5f;
    private const float RestAlpha = 0.6f, RestDelay = 2.5f;

    private sealed class Chip
    {
        public string name;
        public TraitGaugeKind kind;
        public SpriteRenderer renderer;
        public Vector4 frame;        // 사각형이 덮는 아이콘 좌표(가운데 xy, 반너비 zw)
        public float iconScale;      // 아이콘 1단위의 월드 크기
        public float layoutWidth;    // 발 아래 줄에서 차지하는 폭(칸)
        public bool foot;
        public float alpha, x;
        public bool want, placed;
        public TraitGaugeState state;
    }

    private static Sprite quadSprite;
    private readonly TraitGaugeModel model = new();
    private readonly List<TraitGaugeKind> footKinds = new();
    private PlayerController player;
    private PlayerWeaponAbility attack;
    private PlayerHealth health;
    private MaterialPropertyBlock block;
    private Transform root;
    private Chip shield, resolve, reload, railMini, magazine, incendiary, rail;
    private Chip[] chips;
    private float side = 1f;

    // ===== 검증·디버그가 읽기만 하는 값 =====
    public TraitGaugeModel Model => model;
    public bool IsShieldShown => shield != null && shield.alpha > 0.01f;
    public float ShieldFill => model.Shield.A.x;
    public bool IsShieldDeployed => model.BarrierUp;
    /// <summary>발 아래 줄의 무장 칩(검을 들면 Resolve — 일본도 게이지, 없으면 None). 3차부터 레일건 열은 총 쪽이다.</summary>
    public TraitGaugeKind FootArmamentKind => resolve != null && resolve.want ? TraitGaugeKind.Resolve : TraitGaugeKind.None;
    /// <summary>지금 보여야 하는 발 아래 칩(왼쪽부터): Shield · Resolve(일본도) · Magazine(장전 칩) · Rail(열 배터리 칩).</summary>
    public IReadOnlyList<TraitGaugeKind> FootChipKinds => footKinds;
    /// <summary>총 쪽 칩(Magazine·Incendiary·Rail, 기관총 장착 중이거나 근접 무장이면 None).</summary>
    public TraitGaugeKind GunSideKind => magazine != null && magazine.want ? TraitGaugeKind.Magazine
        : incendiary != null && incendiary.want ? TraitGaugeKind.Incendiary
        : rail != null && rail.want ? TraitGaugeKind.Rail : TraitGaugeKind.None;
    public bool IsGunSideShown => (magazine != null && magazine.alpha > 0.01f) || (incendiary != null && incendiary.alpha > 0.01f)
                                  || (rail != null && rail.alpha > 0.01f);
    /// <summary>발 아래 라이플 장전 칩(작은 빨간 탄창)이 보인다.</summary>
    public bool IsReloadChipShown => reload != null && reload.alpha > 0.01f;
    /// <summary>발 아래 레일건 열 칩(작은 배터리)이 보인다.</summary>
    public bool IsRailChipShown => railMini != null && railMini.alpha > 0.01f;
    /// <summary>장전 중이고 그 진행이 빨간 탄창(총 쪽 또는 발 아래 칩)으로 보이고 있다.</summary>
    public bool IsReloadRed => model.Reloading && ((magazine != null && magazine.want) || (reload != null && reload.want));
    /// <summary>총 쪽 칩의 가운데(Player 루트 로컬, 미끄러지는 중이면 그 순간 자리).</summary>
    public Vector3 GunSideLocalPosition => new(side * GunSideX, GunSideY, 0f);
    public float Side => side;
    /// <summary>칩 하나의 가운데(Player 루트 로컬). 같은 종류가 총 쪽과 발 아래에 둘 다 있으면(Magazine·Rail) 보이는 총 쪽을 먼저 본다. 보이지 않으면 false.</summary>
    public bool TryGetChipCenter(TraitGaugeKind kind, out Vector3 center)
    {
        center = Vector3.zero;
        var chip = Find(kind, false);
        if (chip == null || chip.alpha <= 0.01f) chip = Find(kind, true);
        if (chip == null || chip.alpha <= 0.01f) return false;
        center = chip.foot ? new Vector3(chip.x, FootRowY, 0f) : GunSideLocalPosition;
        return true;
    }
    /// <summary>발 아래 칩 하나의 가운데(Player 루트 로컬). 보이지 않으면 false.</summary>
    public bool TryGetFootChipCenter(TraitGaugeKind kind, out Vector3 center)
    {
        center = Vector3.zero;
        var chip = Find(kind, true);
        if (chip == null || chip.alpha <= 0.01f) return false;
        center = new Vector3(chip.x, FootRowY, 0f);
        return true;
    }

    public void Bind(PlayerController owner, PlayerWeaponAbility ability, PlayerStarShotAim aim)
    {
        player = owner;
        attack = ability;
        health = owner != null ? owner.Health : null;
        block ??= new MaterialPropertyBlock();
        side = owner != null && owner.Facing < 0 ? -1f : 1f;
        BuildChips();
        // 새 렌더러를 렌더 보간 대상에 넣는다(Collect는 처음 한 번만 하므로 실행 중에 붙인 자식은 다시 모아야 한다).
        var body = GetComponentInParent<FixedMotionBody>();
        if (body != null) body.RefreshGraphics();
    }

    private void BuildChips()
    {
        if (chips != null) return;
        var material = WeaponTraitFeedbackSO.GaugeMaterial;
        if (material == null)
        {
            Debug.LogWarning("[PlayerTraitGaugeVisual] WeaponTraitFeedback 표의 gaugeMaterial이 비어 있어 Player 주변 특성 아이콘을 그리지 않습니다(tools/Unity/WeaponTraitHudSetup.Run).");
            return;
        }
        var existing = transform.Find("TraitGauge");
        root = existing != null ? existing : new GameObject("TraitGauge").transform;
        root.SetParent(transform, false);
        root.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
        root.localScale = Vector3.one;

        // 사각형(frame)은 연출 전체(튀어오름·퍼지는 고리·바깥 빛·빠지는 탄창)가 잘리지 않게 덮는다. 셰이더 계산을 옮긴 오프라인 측정에서
        // 배경을 1단계(8비트) 이상 바꾸는 범위에 AA 여유를 더한 값이다(방어막 고리 ±1.17×±1.47, 2026-11-21 독립 검토 지적 반영 /
        // 3차 측정: 일본도 금빛 맥동 ±2.21×-0.81~0.83, 열 배터리 충전 고리·과열 튀어오름 ±0.98×±1.56, 작열탄 10발 ±0.99×±1.46,
        // 장전 탄창 붉은 빛 ±0.72×-1.26~1.24, 총 쪽 탄창은 빠지는 빈 탄창 포함 y -2.07~1.24).
        // 사각형 밖은 투명이고 줄 배치는 layoutWidth로 하므로 넓혀도 배치는 그대로다.
        shield = MakeChip("Shield", TraitGaugeKind.Shield, material, new Vector4(0f, 0f, 1.2f, 1.5f), FootIconScale, 0.34f, true);
        resolve = MakeChip("Resolve", TraitGaugeKind.Resolve, material, new Vector4(0f, 0f, 2.25f, 0.87f), KatanaIconScale, 3.36f * KatanaIconScale, true);
        reload = MakeChip("Reload", TraitGaugeKind.Magazine, material, new Vector4(0f, 0f, 0.75f, 1.28f), FootMiniIconScale, 0.2f, true);
        railMini = MakeChip("RailHeat", TraitGaugeKind.Rail, material, new Vector4(0f, 0f, 1.0f, 1.58f), FootMiniIconScale, 0.2f, true);
        // 총 쪽 탄창은 빠지는 빈 탄창과 아래에서 올라오는 새 탄창이 보이도록 아래로 더 넉넉하게 덮는다.
        magazine = MakeChip("Magazine", TraitGaugeKind.Magazine, material, new Vector4(0f, -0.52f, 0.75f, 1.8f), GunSideIconScale, 0f, false);
        incendiary = MakeChip("Incendiary", TraitGaugeKind.Incendiary, material, new Vector4(0f, 0f, 1.02f, 1.48f), GunSideIconScale, 0f, false);
        rail = MakeChip("Rail", TraitGaugeKind.Rail, material, new Vector4(0f, 0f, 1.0f, 1.58f), GunSideIconScale, 0f, false);
        // 발 아래 줄은 이 순서대로 왼쪽부터 놓인다.
        chips = new[] { shield, resolve, reload, railMini, magazine, incendiary, rail };
    }

    private Chip MakeChip(string name, TraitGaugeKind kind, Material material, Vector4 frame, float iconScale, float layoutWidth, bool foot)
    {
        var existing = root.Find(name);
        var go = existing != null ? existing.gameObject : new GameObject(name);
        go.transform.SetParent(root, false);
        go.layer = gameObject.layer;
        var renderer = go.GetComponent<SpriteRenderer>();
        if (renderer == null) renderer = go.AddComponent<SpriteRenderer>();
        renderer.sprite = QuadSprite;
        renderer.sharedMaterial = material;
        renderer.sortingLayerName = SortingLayer;
        renderer.sortingOrder = SortingOrder;
        renderer.color = Color.white;
        renderer.enabled = false;
        return new Chip
        {
            name = name, kind = kind, renderer = renderer, frame = frame, iconScale = iconScale, layoutWidth = layoutWidth, foot = foot,
            state = new TraitGaugeState { Kind = kind, A = Vector4.zero, B = new Vector4(-1f, -10f, -10f, 1f) },
        };
    }

    // 흰 사각형 1×1칸(셰이더가 모양을 그리므로 텍스처는 쓰지 않는다)
    private static Sprite QuadSprite
    {
        get
        {
            if (quadSprite != null) return quadSprite;
            var texture = Texture2D.whiteTexture;
            quadSprite = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0.5f),
                texture.width, 0, SpriteMeshType.FullRect);
            quadSprite.name = "TraitGaugeQuad";
            quadSprite.hideFlags = HideFlags.DontSave;
            return quadSprite;
        }
    }

    private Chip Find(TraitGaugeKind kind, bool foot)
    {
        if (chips == null) return null;
        foreach (var chip in chips) if (chip.kind == kind && chip.foot == foot) return chip;
        return null;
    }

    private void LateUpdate()
    {
        if (chips == null || player == null) return;
        float dt = Time.deltaTime, now = Time.time;
        float facing = player.Facing >= 0 ? 1f : -1f;
        side = Mathf.MoveTowards(side, facing, dt * 2f / SideSwitchTime);
        model.Update(attack, now, side >= 0f ? 1f : -1f);

        bool shown = model.Valid && (health == null || !health.IsDead)
                     && (player.model == null || player.model.activeInHierarchy);
        var held = model.Armament.Kind;
        shield.want = shown && (model.HammerHeld || model.BarrierUp || model.ShieldBreaking);
        resolve.want = shown && held == TraitGaugeKind.Resolve;
        magazine.want = shown && held == TraitGaugeKind.Magazine;
        incendiary.want = shown && held == TraitGaugeKind.Incendiary;
        rail.want = shown && held == TraitGaugeKind.Rail;
        // 지금 든 무장이 총 쪽에 보여 주지 않는 상태(다른 무장을 든 동안의 장전·열·잠금·충전)는 발 아래 작은 칩으로 보인다.
        reload.want = shown && held != TraitGaugeKind.Magazine && model.ReloadShowing(now);
        railMini.want = shown && held != TraitGaugeKind.Rail && model.RailActive;

        shield.state = model.Shield;
        magazine.state = model.Magazine;
        reload.state = MiniMagazine(model.Magazine);
        rail.state = WithCells(model.Rail, RailCellsGunSide);
        railMini.state = WithCells(model.Rail, RailCellsMini);
        // 작열탄·필살은 그 무장을 든 동안만 값이 있다(사라지는 동안은 마지막 값을 그대로 그린다).
        if (held == TraitGaugeKind.Incendiary) incendiary.state = model.Armament;
        if (held == TraitGaugeKind.Resolve) resolve.state = model.Armament;

        LayoutFootRow(dt);
        foreach (var chip in chips)
        {
            // 쓰러졌거나 외형이 숨었으면 페이드 없이 바로 숨긴다. 사망 직후 PlayerDeathTransitionSO가 Time.timeScale = 0으로 1.5초 멈춰
            // Time.deltaTime이 0이라 페이드로는 아이콘이 그동안 남는다(HammerBarrierVisual과 같은 이유, 2026-11-21 독립 검토 지적).
            if (!shown)
            {
                chip.alpha = 0f;
                chip.placed = false;
                continue;
            }
            float target = chip.want ? 1f : 0f;
            if (chip == magazine && chip.want && model.MagazineResting(now, RestDelay)) target = RestAlpha;
            if (chip == rail && chip.want && model.RailResting(now, RestDelay)) target = RestAlpha;
            chip.alpha = Mathf.MoveTowards(chip.alpha, target, dt / FadeTime);
        }
        Apply(now);
    }

    // 발 아래 장전 칩: 빠지는 탄창 연출 없이(칩이 작고 줄 아래로 떨어지므로) 늘 오른쪽을 본다.
    private static TraitGaugeState MiniMagazine(TraitGaugeState state)
    {
        state.B.x = -10f;
        state.B.w = 1f;
        return state;
    }

    private static TraitGaugeState WithCells(TraitGaugeState state, int cells)
    {
        state.B.y = cells;
        return state;
    }

    // 발 아래 줄: 보이는 칩을 가운데 정렬로 나란히 놓는다. 새로 나타나는 칩은 바로 제자리에, 이미 보이던 칩은 미끄러져 간다.
    private void LayoutFootRow(float dt)
    {
        float total = 0f;
        int count = 0;
        footKinds.Clear();
        foreach (var chip in chips)
        {
            if (!chip.foot || !chip.want) continue;
            total += chip.layoutWidth;
            count++;
            footKinds.Add(chip.kind);
        }
        total += FootGap * Mathf.Max(0, count - 1);
        float cursor = -total * 0.5f;
        foreach (var chip in chips)
        {
            if (!chip.foot || !chip.want) continue;
            float target = cursor + chip.layoutWidth * 0.5f;
            cursor += chip.layoutWidth + FootGap;
            if (!chip.placed || chip.alpha <= 0.001f) chip.x = target;
            else chip.x = Mathf.MoveTowards(chip.x, target, RowSlideSpeed * dt);
            chip.placed = true;
        }
    }

    private void Apply(float now)
    {
        foreach (var chip in chips)
        {
            float alpha = chip.alpha;
            Vector2 center;
            if (chip.foot) center = new Vector2(chip.x, FootRowY);
            else
            {
                center = new Vector2(side * GunSideX, GunSideY);
                alpha *= Mathf.SmoothStep(0f, 1f, Mathf.Abs(side) / 0.6f);   // 몸 앞을 지나는 동안 옅어진다
            }
            var renderer = chip.renderer;
            renderer.enabled = alpha > 0.001f;
            if (!renderer.enabled) continue;
            float scale = chip.iconScale;
            var t = renderer.transform;
            t.localPosition = new Vector3(center.x + chip.frame.x * scale, center.y + chip.frame.y * scale, 0f);
            t.localRotation = Quaternion.identity;
            t.localScale = new Vector3(2f * chip.frame.z * scale, 2f * chip.frame.w * scale, 1f);
            renderer.GetPropertyBlock(block);
            block.SetFloat(KindId, (float)chip.kind);
            block.SetVector(StateAId, chip.state.A);
            block.SetVector(StateBId, chip.state.B);
            block.SetVector(FrameId, chip.frame);
            block.SetFloat(AlphaId, alpha);
            block.SetFloat(ClockId, now);
            renderer.SetPropertyBlock(block);
        }
    }

    private void OnDisable()
    {
        if (chips == null) return;
        foreach (var chip in chips)
        {
            chip.alpha = 0f;
            chip.placed = false;
            if (chip.renderer != null) chip.renderer.enabled = false;
        }
    }
}

/// <summary>무장 특성 아이콘의 종류(셰이더 _Kind 값과 같다. 6은 적 화상 EnemyBurn이 쓴다).</summary>
public enum TraitGaugeKind { None = -1, Shield = 0, Magazine = 1, Incendiary = 2, Resolve = 3, Rail = 4, MachineGun = 5 }

/// <summary>아이콘 하나의 상태. A·B의 뜻은 종류마다 다르다(Assets/Art/Shaders/PlayerTraitGaugeCommon.hlsl 머리말 표와 같은 약속).</summary>
public struct TraitGaugeState
{
    public TraitGaugeKind Kind;
    public Vector4 A, B;
}

/// <summary>
/// 무장 특성 표시의 상태 계산(Player 주변 월드 아이콘 PlayerTraitGaugeVisual과 PlayerHUD 띠 WeaponTraitHud가 각자 하나씩 두고 매 프레임 부른다).
/// PlayerWeaponTraits·PlayerWeaponAbility를 읽기만 한다. 값이 바뀐 시각(쏨·장전 끝·필살 증감·채움 변화·열 오름 등)을 기억해 셰이더가 사건 연출을 그리게 한다.
/// 라이플 탄창(Magazine)과 레일건 열(Rail)은 어느 무장을 들었든 계산한다(다른 무장을 든 동안의 장전·열 표시용, 3차).
/// 시간은 Time.time 기준이다.
/// </summary>
public sealed class TraitGaugeModel
{
    /// <summary>방어막 채움이 오를 때 따라가는 시간(셰이더 TG_DrawShield와 같은 값).</summary>
    public const float ShieldRiseTime = 0.22f;
    /// <summary>방어막이 부서진 뒤 균열 연출이 끝날 때까지 칩을 남기는 시간.</summary>
    public const float BreakHoldTime = 0.7f;
    /// <summary>다른 무장을 든 동안 장전이 끝나면 '다 참' 번쩍임이 보이도록 장전 칩을 더 남기는 시간.</summary>
    public const float ReloadDoneHoldTime = 0.45f;

    public TraitGaugeState Shield;
    /// <summary>지금 든 무장의 칩(Magazine·Incendiary·MachineGun·Resolve·Rail, 없으면 Kind = None).</summary>
    public TraitGaugeState Armament;
    /// <summary>라이플 탄창(어느 무장을 들었든 계산). B.w = 넘겨받은 보는 방향.</summary>
    public TraitGaugeState Magazine;
    /// <summary>레일건 열 배터리(어느 무장을 들었든 계산). B.y(칸 수)는 0 — 그리는 쪽이 정한다(0이면 셰이더가 10칸).</summary>
    public TraitGaugeState Rail;
    public bool Valid { get; private set; }
    public string ArmamentId { get; private set; }
    public bool HammerHeld { get; private set; }
    /// <summary>해머가 어느 무장 자리에든 장착되어 있다.</summary>
    public bool HammerEquipped { get; private set; }
    /// <summary>라이플이 어느 무장 자리에든 장착되어 있다(장착하지 않았으면 장전 칩을 보이지 않는다).</summary>
    public bool RifleEquipped { get; private set; }
    /// <summary>레일건이 어느 무장 자리에든 장착되어 있다(장착하지 않았으면 열 칩을 보이지 않는다).</summary>
    public bool RailgunEquipped { get; private set; }
    public bool BarrierUp { get; private set; }
    /// <summary>방어막이 남았거나 충전 중이거나 막 부서진 참이다.</summary>
    public bool ShieldBusy { get; private set; }
    public bool ShieldBreaking { get; private set; }
    public int Ammo { get; private set; }
    public bool Reloading { get; private set; }
    /// <summary>레일건 열이 남았거나 과열 잠금·충전 중이다(레일건을 들지 않았어도 표시할 상태).</summary>
    public bool RailActive { get; private set; }
    public float RailHeat { get; private set; }
    public bool RailLocked { get; private set; }
    public bool RailCharged { get; private set; }

    private PlayerWeaponTraits observed;
    private uint observedGeneration;
    private string lastArmament;
    private float shieldTarget = -1f, shieldFrom, shieldChange = -10f;
    private bool shieldDeployed;
    private int lastAmmo = -1;
    private float lastShot = -10f, loadedAt = -10f, magazineActivity = -10f;
    private bool wasReloading, loadedAway;
    private int lastIncendiary = -1;
    private float incendiaryShot = -10f, incendiaryMount = -10f;
    private bool wasIncendiary;
    private int lastResolve = -1, resolveFrom;
    private float resolveChange = -10f;
    private float chargeTotal = 5f, lastChargeRemaining;
    private float lastHeat = -1f, heatFrom, heatRise = -10f, railActivity = -10f;
    private float machineGunMount = -10f;
    private bool wasMachineGun;

    /// <param name="facing">탄창·작열탄 그림의 앞쪽(월드 = 보는 방향, HUD = +1)</param>
    public void Update(PlayerWeaponAbility attack, float now, float facing)
    {
        var traits = attack != null ? attack.Traits : null;
        if (traits != observed || (traits != null && traits.Generation != observedGeneration))
        {
            ResetTracking();
            observed = traits;
            observedGeneration = traits != null ? traits.Generation : 0u;
        }
        Shield.Kind = TraitGaugeKind.Shield;
        Magazine.Kind = TraitGaugeKind.Magazine;
        Rail.Kind = TraitGaugeKind.Rail;
        Armament.Kind = TraitGaugeKind.None;
        Valid = traits != null;
        if (!Valid)
        {
            ArmamentId = null;
            HammerHeld = HammerEquipped = RifleEquipped = RailgunEquipped = BarrierUp = ShieldBusy = ShieldBreaking = Reloading = false;
            RailActive = RailLocked = RailCharged = false;
            return;
        }

        ArmamentId = attack.ActiveArmamentId;
        if (ArmamentId != lastArmament) magazineActivity = railActivity = now;   // 무장을 바꾼 직후에는 탄창·배터리를 또렷하게 보인다
        lastArmament = ArmamentId;
        HammerHeld = ArmamentId == "Hammer";
        HammerEquipped = RifleEquipped = RailgunEquipped = false;
        for (int i = 0; i < attack.WeaponSetCount; i++)
        {
            var armament = attack.GetEquippedArmament(i);
            if (armament == null) continue;
            if (armament.armamentId == "Hammer") HammerEquipped = true;
            else if (armament.armamentId == "Rifle") RifleEquipped = true;
            else if (armament.armamentId == "Railgun") RailgunEquipped = true;
        }

        UpdateShield(traits, now);

        int setIndex = attack.PrimaryOverrideSetIndex;
        bool mounted = setIndex >= 0 && setIndex == attack.CurrentSetIndex;
        var mountedWeapon = mounted ? attack.PrimaryOverrideWeapon : null;
        bool rifle = ArmamentId == "Rifle";
        bool isIncendiary = rifle && mountedWeapon is IncendiaryRifleWeaponSO;
        bool isMachineGun = rifle && mountedWeapon is MachineGunWeaponSO;
        float overrideEnd = now + attack.PrimaryOverrideRemaining;
        float overrideDuration = attack.PrimaryOverrideDuration;

        // 라이플 탄창(어느 무장을 들었든 변화를 기억한다 — 장전은 다른 무장을 든 동안에도 진행된다)
        int ammo = traits.RifleAmmo;
        bool reloading = traits.IsReloading;
        if (lastAmmo >= 0 && ammo < lastAmmo) lastShot = magazineActivity = now;
        if (wasReloading && !reloading && ammo >= PlayerWeaponTraits.MagazineCapacity)
        {
            loadedAt = magazineActivity = now;
            // 라이플 탄창을 든 채 끝났으면 총 쪽 탄창이 이미 '다 참'을 보여 주므로 장전 칩을 더 남기지 않는다.
            loadedAway = !(rifle && !isIncendiary && !isMachineGun);
        }
        if (reloading) magazineActivity = now;
        lastAmmo = ammo;
        wasReloading = reloading;
        Ammo = ammo;
        Reloading = reloading;
        Magazine = new TraitGaugeState
        {
            Kind = TraitGaugeKind.Magazine,
            A = new Vector4(ammo, PlayerWeaponTraits.MagazineCapacity, reloading ? now + traits.ReloadRemaining : 0f, PlayerWeaponTraits.ReloadDuration),
            B = new Vector4(traits.MagazineEmptyTime, lastShot, loadedAt, facing),
        };

        // 작열탄
        int incendiaryAmmo = traits.IncendiaryAmmo;
        if (isIncendiary && !wasIncendiary) incendiaryMount = now;
        if (lastIncendiary >= 0 && incendiaryAmmo < lastIncendiary) incendiaryShot = now;
        lastIncendiary = incendiaryAmmo;
        wasIncendiary = isIncendiary;

        // 기관총 장착
        if (isMachineGun && !wasMachineGun) machineGunMount = now;
        wasMachineGun = isMachineGun;

        // 검 필살
        int resolveNow = traits.Resolve;
        if (lastResolve < 0) resolveFrom = resolveNow;
        else if (resolveNow != lastResolve)
        {
            resolveFrom = lastResolve;
            resolveChange = now;
        }
        lastResolve = resolveNow;

        // 레일건 열 배터리(어느 무장을 들었든). 충전 시간: 충전이 새로 시작되거나 늘어난 순간의 남은 시간을 전체로 본다.
        float charge = traits.ChargeRemaining;
        if (charge > lastChargeRemaining + 0.05f) chargeTotal = Mathf.Max(charge, 0.1f);
        lastChargeRemaining = charge;
        float lockout = traits.OverheatRemaining;
        float heat = traits.Heat;
        // 열이 오른 순간(한 발)만 기억한다. 식는 동안 줄어드는 값은 사건이 아니다.
        if (lastHeat >= 0f && heat > lastHeat + 0.001f)
        {
            heatFrom = lastHeat;
            heatRise = now;
        }
        lastHeat = heat;
        RailHeat = heat;
        RailLocked = lockout > 0f;
        RailCharged = charge > 0f;
        RailActive = RailgunEquipped && (heat > 0.001f || RailLocked || RailCharged);   // 장착하지 않은 레일건의 열은 보이지 않는다
        if (RailActive) railActivity = now;
        Rail = new TraitGaugeState
        {
            Kind = TraitGaugeKind.Rail,
            A = new Vector4(heat, RailLocked ? now + lockout : 0f, PlayerWeaponTraits.OverheatDuration, RailCharged ? now + charge : 0f),
            B = new Vector4(chargeTotal, 0f, heatRise, Mathf.Min(heatFrom, heat)),
        };

        switch (ArmamentId)
        {
            case "Rifle":
                if (isIncendiary)
                    Armament = new TraitGaugeState
                    {
                        Kind = TraitGaugeKind.Incendiary,
                        A = new Vector4(incendiaryAmmo, PlayerWeaponTraits.IncendiaryCapacity, overrideEnd, overrideDuration),
                        B = new Vector4(incendiaryShot, incendiaryMount, 0f, facing),
                    };
                else if (isMachineGun)
                    Armament = new TraitGaugeState
                    {
                        Kind = TraitGaugeKind.MachineGun,
                        A = new Vector4(overrideEnd, overrideDuration, machineGunMount, 0f),
                        B = Vector4.zero,
                    };
                else Armament = Magazine;
                break;
            case "Sword":
                Armament = new TraitGaugeState
                {
                    Kind = TraitGaugeKind.Resolve,
                    A = new Vector4(resolveNow, PlayerWeaponTraits.ResolveCapacity, resolveChange, resolveFrom),
                    B = Vector4.zero,
                };
                break;
            case "Railgun":
                Armament = Rail;
                break;
        }
    }

    /// <summary>탄창이 가득 찬 채 delay초 동안 변화가 없다(월드 탄창을 조금 옅게 한다).</summary>
    public bool MagazineResting(float now, float delay) =>
        !Reloading && Ammo >= PlayerWeaponTraits.MagazineCapacity && now - magazineActivity > delay;

    /// <summary>열 배터리가 식은 채(열 0, 잠금·충전 없음) delay초 동안 변화가 없다(월드 배터리를 조금 옅게 한다).</summary>
    public bool RailResting(float now, float delay) => !RailActive && now - railActivity > delay;

    /// <summary>장전 칩을 보여야 한다: 장전 중이거나, 장전이 막 끝나 '다 참' 번쩍임을 보이는 중이다.</summary>
    public bool ReloadShowing(float now) => RifleEquipped && (Reloading || (loadedAway && loadedAt > 0f && now - loadedAt < ReloadDoneHoldTime));

    private void UpdateShield(PlayerWeaponTraits traits, float now)
    {
        bool deployed = traits.Barrier > 0f;
        float target = Mathf.Clamp01((deployed ? traits.Barrier : traits.BarrierCharge) / PlayerWeaponTraits.BarrierCapacity);
        if (shieldTarget < 0f)
        {
            shieldTarget = shieldFrom = target;
            shieldChange = -10f;
            shieldDeployed = deployed;
        }
        else if (Mathf.Abs(target - shieldTarget) > 0.0001f || deployed != shieldDeployed)
        {
            shieldFrom = ShownShieldFill(now);
            shieldTarget = target;
            shieldChange = now;
            shieldDeployed = deployed;
        }
        float eventTime = traits.BarrierEventTime;
        bool hasEvent = eventTime > -5f;
        Shield.A = new Vector4(shieldTarget, shieldFrom, shieldChange, deployed ? 1f : 0f);
        Shield.B = new Vector4(hasEvent ? (int)traits.LastBarrierEvent : -1f, eventTime, 0f, 0f);
        BarrierUp = deployed;
        ShieldBreaking = hasEvent && traits.LastBarrierEvent == PlayerWeaponTraits.BarrierEventType.Broken && now - eventTime < BreakHoldTime;
        ShieldBusy = deployed || traits.BarrierCharge > 0f || ShieldBreaking;
    }

    // 셰이더와 같은 계산: 오를 때는 ShieldRiseTime 동안 따라가고, 줄 때는 바로 줄어든다.
    private float ShownShieldFill(float now)
    {
        if (shieldTarget < shieldFrom) return shieldTarget;
        float k = Mathf.Clamp01((now - shieldChange) / ShieldRiseTime);
        k = 1f - (1f - k) * (1f - k) * (1f - k);
        return Mathf.Lerp(shieldFrom, shieldTarget, k);
    }

    private void ResetTracking()
    {
        shieldTarget = -1f;
        shieldFrom = 0f;
        shieldChange = -10f;
        shieldDeployed = false;
        lastAmmo = -1;
        lastShot = loadedAt = magazineActivity = -10f;
        wasReloading = loadedAway = false;
        lastIncendiary = -1;
        incendiaryShot = incendiaryMount = -10f;
        wasIncendiary = false;
        lastResolve = -1;
        resolveChange = -10f;
        chargeTotal = 5f;
        lastChargeRemaining = 0f;
        lastHeat = -1f;
        heatFrom = 0f;
        heatRise = railActivity = -10f;
        machineGunMount = -10f;
        wasMachineGun = false;
        lastArmament = null;
    }
}
