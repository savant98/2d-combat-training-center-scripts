using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// PlayerHUD 상태 표시 띠(2026-11-21 개편, 사용자 요청 "PlayerHUD 기존 HUD 바로 아래에 '상태이상 표시 영역'을 만들어 같은 표시").
// 옛 판(Player를 따라다니는 글자 판, PR #159)을 대신한다. 더 이상 Player를 따라다니지 않고 HUD 메인 판 바로 밑에 고정된다.
// - 자리: 부모 HUD 왼쪽 위 기준 anchor·pivot (0,1), anchoredPosition (164,-256), 크기 520×44(HUD 단위, scale 0.8 → 1080p x 121~537·y 166~201px).
//   메인 판 아래 변(y -248) 바로 밑이고, 왼쪽 시작 x 164는 대각 판(Icons/Panels)과 겹치지 않는 자리다(조사 02 §4.1).
// - 칩(높이 44, 간격 6)을 왼쪽부터 쌓는다: 방어막 칩(해머가 어느 자리에든 장착됐거나 충전·방어막이 있을 때)
//   + 지금 든 무장의 특성 칩(라이플 탄창 / 작열탄 / 기관총 장착 남은 시간 고리 / 검 필살 일본도 / 레일건 열 배터리)
//   + 다른 무장을 든 동안의 상태 칩(3차, 2026-11-21 사용자 요청 "다른 무장을 들어도 재장전·열·잠금·충전 중이면 HUD 띠에도"):
//     라이플 장전 칩(장전 중 빨간 탄창 — 라이플 탄창 칩이 없을 때), 레일건 열 칩(열이 남았거나 과열 잠금·충전 중 — 레일건 칩이 없을 때).
//   왼쪽부터의 순서는 종류로 고정이다(방어막 → 검 필살 → 작열탄 → 기관총 → 라이플 탄창 → 레일건 열 — 칩끼리 자리를 맞바꾸지 않게).
//   검 필살 칩만 폭 88(일본도 게이지를 눕혀 그린다), 나머지는 44×44. 글자는 없다.
// - 칩 = PlayerHUD 판 언어(어두운 Sci-fi 판 #08111d·알파 0.88 + 얇은 청록 테두리 + 깎인 모서리 + 밝은 꺾쇠) + 안에 Player 주변 월드 아이콘과 같은 그림.
//   그림은 셰이더 UI/PlayerHudTraitGauge(PlayerTraitGaugeCommon.hlsl 공용)가 그리고, 칩별 값은 정점 데이터(uv1·uv2·uv3)로 넘긴다
//   (UI 그래픽은 MaterialPropertyBlock을 못 쓴다). 그래서 Canvas의 additionalShaderChannels에 TexCoord1~3을 더한다.
//   사건 연출(번쩍임·빠지는 탄창·깜빡임)은 재질 사본의 _Clock(Time.time)으로 셰이더가 그려, 값이 바뀔 때만 정점을 다시 만든다.
// - 칩끼리는 겹치지 않는다(2026-11-21 독립 검토 지적 — 교체·자리 이동 때 판이 약 0.1초 겹쳤다):
//   사라지는 칩은 제자리에서 아래로 가라앉으며 0.08초에 사라지고, 자리를 옮기는 칩은 가는 길에 아직 보이는 칩이 있으면 사라질 때까지 기다렸다가 미끄러지며,
//   새 칩은 제자리가 빈 뒤(사라지는 칩이 사라지고 옮겨 가는 칩이 자리를 비운 뒤)에 아래에서 떠오르며 0.12초에 나타난다.
//   예: 무장 교체 = 0.08초 뒤 새 칩 등장, 방어막 칩이 빠질 때 = 0.08초 뒤 무장 칩이 0.14초 동안 왼쪽으로, 방어막 칩이 생길 때 = 무장 칩이 비킨 뒤 등장.
// - 사망 시 페이드 없이 바로 숨긴다(사망 연출이 Time.timeScale = 0으로 1.5초 멈춰 페이드가 진행되지 않는다). HUD CanvasGroup(PlayerHUD.HUDVisible)에 따라 함께 숨는다.
// PlayerHUD.Start가 Resources/UI/WeaponTraitHud를 HUD 아래에 만들고 Configure를 부른다(MainScene 무수정). 배치·재질 연결은 tools/Unity/WeaponTraitHudSetup.cs.
[DefaultExecutionOrder(160)]
public sealed class WeaponTraitHud : MaskableGraphic
{
    private static readonly int ClockId = Shader.PropertyToID("_Clock");
    public const float ChipSize = 44f, ChipGap = 6f;
    /// <summary>검 필살(일본도 게이지) 칩의 폭. 칼을 눕혀 칼날 3구간이 읽히게 정사각 칩 두 개 폭으로 잡았다.</summary>
    public const float WideChipWidth = 88f;
    /// <summary>HUD 열 배터리의 칸 수(한 칸 = 2발 — 44 칩 안에서 10칸이면 칸이 2px 아래로 작아진다).</summary>
    public const int RailCells = 5;
    public static readonly Vector2 BandPosition = new(164f, -256f), BandSize = new(520f, 44f);
    /// <summary>나타남 시간, 사라짐 시간, 자리 이동 속도(HUD 단위/초), 나타나고 사라질 때 아래로 내려가 있는 거리(HUD 단위).</summary>
    private const float FadeTime = 0.12f, LeaveTime = 0.08f, SlideSpeed = 360f, DropOffset = 10f;
    private const float Hidden = 0.001f;
    private const AdditionalCanvasShaderChannels Channels =
        AdditionalCanvasShaderChannels.TexCoord1 | AdditionalCanvasShaderChannels.TexCoord2 | AdditionalCanvasShaderChannels.TexCoord3;

    // 종류별 아이콘 1단위(세로 반높이)의 Canvas 크기 — 칩 안쪽 창(높이 ±20, 일본도 칩 폭 ±42)에 들어가게 맞췄다.
    // 0 방어막 · 1 탄창 · 2 작열탄 · 3 일본도(길이 ±1.68 → ±40) · 4 열 배터리(충전 고리 포함 ±1.03 → ±19) · 5 기관총 · 6(화상, HUD에서는 쓰지 않음)
    private static readonly float[] IconScale = { 17f, 17f, 17f, 24f, 18.5f, 18f, 17f };
    // 칩 자리(종류마다 하나)이자 왼쪽부터의 고정 순서: 방어막 → 검 필살 → 작열탄 → 기관총 → 라이플 탄창(든 무장이든 장전 칩이든) → 레일건 열.
    // 순서를 종류로 고정해 두 칩이 서로 자리를 맞바꾸는 일이 없게 한다(예: 레일건을 들면 열 칩이 탄창 칩 앞으로 가야 하는 순서라면
    // 옮기는 칩이 서로의 길을 막아 멈춘다 — 3차 배치 시뮬레이션에서 확인).
    private static readonly TraitGaugeKind[] Kinds =
        { TraitGaugeKind.Shield, TraitGaugeKind.Resolve, TraitGaugeKind.Incendiary, TraitGaugeKind.MachineGun, TraitGaugeKind.Magazine, TraitGaugeKind.Rail };

    private sealed class Slot
    {
        public TraitGaugeKind kind;
        public TraitGaugeState state;
        public float alpha, x;
        public bool want;
        public Vector4 drawnA, drawnB;
        public float drawnAlpha = -1f, drawnX;
    }

    private readonly TraitGaugeModel model = new();
    private readonly List<TraitGaugeKind> order = new();
    private Slot[] slots;
    private CanvasGroup visibility;
    private Material instance;
    private bool configured, warned;
    private PlayerController player;
    private PlayerWeaponAbility attack;
    // 회귀 도구 호환(ArmamentWeaponsRegression이 비공개 필드 "mode"를 읽는다): 지금 든 무장 0 해머·1 라이플·2 검·3 레일건, 없으면 -1
    private int mode = -1;

    // ===== 검증·디버그가 읽기만 하는 값 =====
    public TraitGaugeModel Model => model;
    public bool IsShown => visibility != null && visibility.alpha > 0f;
    /// <summary>지금 보여야 하는 칩 수(나타나는·사라지는 중인 칩은 포함·제외 기준이 want). 순서는 방어막 → 검 필살 → 작열탄 → 기관총 → 라이플 탄창 → 레일건 열(고정).</summary>
    public int ChipCount => order.Count;
    public TraitGaugeKind GetChipKind(int index) => index >= 0 && index < order.Count ? order[index] : TraitGaugeKind.None;
    public float GetChipAlpha(TraitGaugeKind kind) => FindSlot(kind)?.alpha ?? 0f;
    public TraitGaugeState GetChipState(TraitGaugeKind kind) => FindSlot(kind)?.state ?? default;
    public int Mode => mode;
    /// <summary>칩 하나의 폭(HUD 단위). 검 필살은 WideChipWidth, 나머지는 ChipSize.</summary>
    public static float ChipWidth(TraitGaugeKind kind) => kind == TraitGaugeKind.Resolve ? WideChipWidth : ChipSize;
    /// <summary>장전 중이고 띠에 라이플 탄창 칩(든 무장 칩 또는 장전 칩)이 있어 빨갛게 차오르는 진행이 보인다.</summary>
    public bool IsReloadRed => model.Reloading && order.Contains(TraitGaugeKind.Magazine);

    /// <summary>PlayerHUD.Start가 부른다. 글자가 없고 자리가 고정이라 font·avoid는 쓰지 않는다(시그니처는 PlayerHUD 호환으로 유지).</summary>
    public void Configure(TMP_FontAsset font, RectTransform avoid)
    {
        raycastTarget = false;
        maskable = false;
        var rt = rectTransform;
        // 생성 도구를 아직 돌리지 않은 옛 Prefab(가운데 pivot의 따라다니는 판)이면 새 자리로 맞춘다.
        if (rt.pivot != new Vector2(0f, 1f))
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = BandPosition;
            rt.sizeDelta = BandSize;
        }
        visibility = GetComponent<CanvasGroup>();
        if (visibility == null) visibility = gameObject.AddComponent<CanvasGroup>();
        visibility.alpha = 0f;
        visibility.blocksRaycasts = false;
        visibility.interactable = false;
        EnsureSlots();
        EnsureChannels();
        EnsureMaterial();
        configured = true;
        SetVerticesDirty();
    }

    private void EnsureSlots()
    {
        if (slots != null) return;
        slots = new Slot[Kinds.Length];
        for (int i = 0; i < Kinds.Length; i++) slots[i] = new Slot { kind = Kinds[i] };
    }

    private Slot FindSlot(TraitGaugeKind kind)
    {
        if (slots == null) return null;
        foreach (var slot in slots) if (slot.kind == kind) return slot;
        return null;
    }

    // 칩별 값을 uv1~3으로 넘기므로 이 그래픽을 그리는 Canvas가 그 채널을 실어야 한다(실행 중 설정, MainScene 무수정).
    private void EnsureChannels()
    {
        var current = canvas;
        if (current == null) return;
        if ((current.additionalShaderChannels & Channels) != Channels) current.additionalShaderChannels |= Channels;
        var root = current.rootCanvas;
        if (root != null && (root.additionalShaderChannels & Channels) != Channels) root.additionalShaderChannels |= Channels;
    }

    private void EnsureMaterial()
    {
        if (instance != null) return;
        var source = WeaponTraitFeedbackSO.GaugeUiMaterial;
        if (source == null && m_Material != null && m_Material.shader != null && m_Material.shader.name == "UI/PlayerHudTraitGauge") source = m_Material;
        if (source == null)
        {
            if (!warned) Debug.LogWarning("[WeaponTraitHud] WeaponTraitFeedback 표의 gaugeUiMaterial이 비어 있어 상태 표시 띠를 그리지 않습니다(tools/Unity/WeaponTraitHudSetup.Run).");
            warned = true;
            return;
        }
        // _Clock을 매 프레임 넣으므로 이 띠만 쓰는 사본을 만든다.
        instance = new Material(source) { name = source.name + " (HUD)", hideFlags = HideFlags.DontSave };
        material = instance;
    }

    private void LateUpdate()
    {
        if (!configured) return;
        EnsureChannels();
        float now = Time.time, dt = Time.deltaTime;
        var game = GameManager.Instance;
        player = game != null ? game.Player : null;
        var abilities = player != null ? player.AbilityManager : null;
        attack = abilities != null ? abilities.AttackAbility : null;
        model.Update(attack, now, 1f);
        mode = model.ArmamentId switch { "Hammer" => 0, "Rifle" => 1, "Sword" => 2, "Railgun" => 3, _ => -1 };

        bool alive = player != null && (player.Health == null || !player.Health.IsDead);
        bool active = alive && model.Valid && instance != null;
        order.Clear();
        var held = model.Armament.Kind;
        if (active)
        {
            // 방어막, 지금 든 무장의 칩, 그리고 다른 무장을 든 동안에도 진행되는 라이플 장전과 레일건 열·잠금·충전
            // (그 무장을 들었으면 그 무장의 칩이 이미 보여 준다). 보이는 순서는 Kinds의 고정 순서다.
            bool shieldChip = model.HammerEquipped || model.ShieldBusy;
            bool reloadChip = held != TraitGaugeKind.Magazine && model.ReloadShowing(now);
            bool railChip = held != TraitGaugeKind.Rail && model.RailActive;
            foreach (var kind in Kinds)
            {
                bool want = kind == held
                    || (kind == TraitGaugeKind.Shield && shieldChip)
                    || (kind == TraitGaugeKind.Magazine && reloadChip)
                    || (kind == TraitGaugeKind.Rail && railChip);
                if (want) order.Add(kind);
            }
        }

        foreach (var slot in slots)
        {
            slot.want = order.Contains(slot.kind);
            switch (slot.kind)
            {
                case TraitGaugeKind.Shield: slot.state = model.Shield; break;
                case TraitGaugeKind.Magazine: slot.state = model.Magazine; break;
                case TraitGaugeKind.Rail:
                    slot.state = model.Rail;
                    slot.state.B.y = RailCells;
                    break;
                // 작열탄·기관총·필살은 그 무장을 든 동안만 값이 있다(사라지는 동안은 마지막 값을 그대로 그린다).
                default:
                    if (slot.kind == held) slot.state = model.Armament;
                    break;
            }
        }
        if (active) Layout(dt);
        else
        {
            // 쓰러졌거나 그릴 수 없으면 페이드 없이 바로 숨긴다. 사망 직후 PlayerDeathTransitionSO가 Time.timeScale = 0으로 1.5초 멈춰
            // Time.deltaTime이 0이라 페이드로는 칩이 그동안 남는다(HammerBarrierVisual과 같은 이유).
            foreach (var slot in slots) slot.alpha = 0f;
        }

        bool dirty = false, any = false;
        foreach (var slot in slots)
        {
            if (slot.alpha > Hidden) any = true;
            if (Changed(slot)) dirty = true;
        }

        if (instance != null) instance.SetFloat(ClockId, now);
        if (visibility != null) visibility.alpha = any ? 1f : 0f;
        if (dirty) SetVerticesDirty();
    }

    // 칩 배치: 화면에 보이는 칩(alpha > Hidden)끼리는 겹치지 않게 순서대로 처리한다(사라짐 → 자리 이동 → 나타남).
    private void Layout(float dt)
    {
        // 1) 사라지는 칩: 제자리에서 아래로 가라앉으며 사라진다(가라앉는 거리는 OnPopulateMesh가 alpha로 계산).
        foreach (var slot in slots)
            if (!slot.want) slot.alpha = Mathf.MoveTowards(slot.alpha, 0f, dt / LeaveTime);

        // 2) 이미 보이는 칩: 가는 길에 다른 칩이 보이는 동안 기다렸다가 제자리로 미끄러진다(다시 원하게 된 사라지던 칩은 다시 떠오른다).
        foreach (var slot in slots)
        {
            if (!slot.want || slot.alpha <= Hidden) continue;
            float target = TargetX(order.IndexOf(slot.kind));
            if (!Occupied(slot, Mathf.Min(slot.x, target), Mathf.Max(slot.x, target)))
                slot.x = Mathf.MoveTowards(slot.x, target, SlideSpeed * dt);
            slot.alpha = Mathf.MoveTowards(slot.alpha, 1f, dt / FadeTime);
        }

        // 3) 새 칩: 제자리가 비면 그 자리 아래에서 떠오르며 나타난다(옆 칩 쪽으로 밀려 들어오지 않는다).
        foreach (var slot in slots)
        {
            if (!slot.want || slot.alpha > Hidden) continue;
            float target = TargetX(order.IndexOf(slot.kind));
            if (Occupied(slot, target, target)) continue;
            slot.x = target;
            slot.alpha = Mathf.MoveTowards(slot.alpha, 1f, dt / FadeTime);
        }
    }

    // order의 index번째 칩 가운데(띠 왼쪽 끝 기준, 앞 칩들의 폭 + 간격)
    private float TargetX(int index)
    {
        float x = 0f;
        for (int i = 0; i < index; i++) x += ChipWidth(order[i]) + ChipGap;
        return x + ChipWidth(order[index]) * 0.5f;
    }

    // 가운데가 lo~hi 사이를 지나는 칩과 겹치는 다른 칩이 화면에 보이는가(칩마다 폭이 다르다, 맞닿는 것은 겹침이 아니다)
    private bool Occupied(Slot self, float lo, float hi)
    {
        float selfHalf = ChipWidth(self.kind) * 0.5f;
        foreach (var other in slots)
        {
            if (other == self || other.alpha <= Hidden) continue;
            float reach = selfHalf + ChipWidth(other.kind) * 0.5f - 0.01f;
            if (other.x > lo - reach && other.x < hi + reach) return true;
        }
        return false;
    }

    // 정점을 다시 만들 만큼 바뀌었는가(시각 값은 프레임마다 미세하게 흔들려 여유를 둔다)
    private static bool Changed(Slot slot)
    {
        bool visible = slot.alpha > Hidden, drawn = slot.drawnAlpha > Hidden;
        if (!visible && !drawn) return false;
        return Mathf.Abs(slot.alpha - slot.drawnAlpha) > 0.002f || Mathf.Abs(slot.x - slot.drawnX) > 0.01f
            || Far(slot.state.A, slot.drawnA) || Far(slot.state.B, slot.drawnB);
    }

    private static bool Far(Vector4 a, Vector4 b) =>
        Mathf.Abs(a.x - b.x) > 0.002f || Mathf.Abs(a.y - b.y) > 0.002f || Mathf.Abs(a.z - b.z) > 0.002f || Mathf.Abs(a.w - b.w) > 0.002f;

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        if (slots == null) return;
        Rect r = rectTransform.rect;
        float half = ChipSize * 0.5f;
        float cy = r.yMax - half;
        Color32 tint = color;
        foreach (var slot in slots)
        {
            slot.drawnA = slot.state.A;
            slot.drawnB = slot.state.B;
            slot.drawnAlpha = slot.alpha;
            slot.drawnX = slot.x;
            if (slot.alpha <= Hidden) continue;
            var info = new Vector4((float)slot.kind, IconScale[(int)slot.kind], slot.alpha, 0f);
            // 나타나고 사라지는 동안 아래로 내려가 있다(나타날 때는 감속하며 떠오르고, 사라질 때는 가속하며 가라앉는다).
            float fade = 1f - slot.alpha;
            float drop = DropOffset * fade * fade;
            AddChip(vh, new Vector2(r.xMin + slot.x, cy - drop), ChipWidth(slot.kind) * 0.5f, half, slot.state, info, tint);
        }
    }

    // 칩 사각형 하나. uv0 = (칩 가운데 기준 x, y, 0, 반폭이 반높이보다 넓은 만큼 — 셰이더가 판 모양을 그 폭으로 그린다)
    private static void AddChip(VertexHelper vh, Vector2 center, float halfWidth, float half, TraitGaugeState state, Vector4 info, Color32 tint)
    {
        int start = vh.currentVertCount;
        var vertex = UIVertex.simpleVert;
        vertex.color = tint;
        vertex.uv1 = state.A;
        vertex.uv2 = state.B;
        vertex.uv3 = info;
        for (int i = 0; i < 4; i++)
        {
            float dx = i < 2 ? -halfWidth : halfWidth;
            float dy = i == 0 || i == 3 ? -half : half;
            vertex.position = new Vector3(center.x + dx, center.y + dy, 0f);
            vertex.uv0 = new Vector4(dx, dy, 0f, halfWidth - half);
            vh.AddVert(vertex);
        }
        vh.AddTriangle(start, start + 1, start + 2);
        vh.AddTriangle(start, start + 2, start + 3);
    }

    protected override void OnDestroy()
    {
        if (instance != null)
        {
            if (Application.isPlaying) Destroy(instance);
            else DestroyImmediate(instance);
            instance = null;
        }
        base.OnDestroy();
    }
}
