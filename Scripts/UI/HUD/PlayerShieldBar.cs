using UnityEngine;
using UnityEngine.UI;

// PlayerHUD 체력 바 바로 위의 해머 방어막 바(2026-11-21 4차, 사용자 요청 "쉴드 값은 playerHUD에 체력부분 위에 쉴드바를 넣어줘. 체력위에 쉴드바가 있는거야").
// - 값: PlayerWeaponTraits.Barrier / BarrierCapacity. 방어막이 없을 때는 충전 비율을 어두운 칸으로 보인다.
// - 모양(셰이더 UI/PlayerHudShieldBar): 체력 칸과 같은 20° 사선 칸이 체력 칸의 사선을 위로 그대로 잇는다. 체력 첫 칸 위는 비워(SkippedCells)
//   본 슬롯 조준경과 떨어지고, 칸 15개(한 칸 = 방어막 4)가 체력 칸 2~16번 위에 선다. 칸 높이는 체력 칸의 절반 아래(반높이 5)라 보조 바로 읽힌다.
//   자리는 메인 판 위 변 안쪽 점선(HUD y -67)과 체력 레일 위 테두리(-85.8) 사이(칸 -72 ~ -82). 메인 판·체력 바와 같은 HUD 단위다.
// - 사건(PlayerWeaponTraits.BarrierEvent 구독 + 놓친 변화는 값으로 맞춤):
//   전개(Raised) = 빈 칸이 왼쪽부터 raiseTime 동안 차오르며 빛 띠가 지나가고 섬광, 재충전(Refilled) = 남은 곳에서 가득까지 차오름 + 빛 띠,
//   피격(Hit) = 막은 양이 흰빛 도는 청록으로 trailDelay 동안 남았다가 trailDuration 동안 따라 줄어듦(체력 바 깎인 양과 같은 값) + 그 지점 번쩍임,
//   파괴(Broken) = 남아 있던 칸이 번쩍인 뒤 금이 가고 칸마다 다른 때에 깜빡이며 꺼짐(breakTime). 바뀐 양에 따라 바가 위아래로 살짝 튄다.
//   30% 아래에서는 켜진 칸이 숨 쉬듯 어두워졌다 돌아온다.
// - 보임: 해머가 어느 무장 자리에든 장착됐거나, 방어막·충전량이 있거나, 파괴·막은 양 연출 중일 때(빈 칸 홈만 보여도 '방어막 자리'를 알린다).
//   Player가 쓰러졌으면 바로 숨긴다(사망 연출이 Time.timeScale = 0이어도 실제 시간으로 숨김). HUD CanvasGroup(PlayerHUD.HUDVisible)에 함께 숨는다.
// - 시간은 실제 시간(Time.unscaledTime)이다 — 체력 바처럼 히트스톱 중에도 움직인다.
// PlayerHUD.Start가 Resources/UI/PlayerShieldBar를 HUD 아래(체력 바 바로 다음 형제)에 만들고 Configure(체력 바)를 부른다(MainScene 무수정).
// Prefab·재질은 tools/Unity/PlayerShieldBarSetup.cs가 만든다.
[DefaultExecutionOrder(161)]
public sealed class PlayerShieldBar : MaskableGraphic
{
    public const string ResourcePath = "UI/PlayerShieldBar";
    public const string ShaderName = "UI/PlayerHudShieldBar";
    /// <summary>체력 바 가운데에서 방어막 바 가운데까지 올린 거리(HUD 단위). 체력 레일 위 테두리(+19)와 메인 판 안쪽 점선(+37.8) 사이.</summary>
    public const float CenterRise = 27.8f;
    /// <summary>방어막 칸의 반높이(HUD 단위, 체력 칸 11의 절반 아래).</summary>
    public const float CellHalfHeight = 5f;
    /// <summary>체력 칸 줄 왼쪽에서 비우는 칸 수(본 슬롯 조준경 눈금과 겹치지 않게).</summary>
    public const int SkippedCells = 1;
    /// <summary>칸 줄 둘레 여백(가로 = 끝 사선·앞끝 선, 세로 = 앞끝 선이 칸보다 1.5 더 길다).</summary>
    public const float EdgePad = 8f, VerticalPad = 3f;

    // 체력 바 재질(UI/PlayerHudHpFill)을 읽지 못했을 때 쓰는 값(PlayerHudSetup과 같다)
    public const float FallbackSpan = 484f, FallbackCells = 16f, FallbackGap = 6f;
    public static readonly float FallbackSlant = Mathf.Tan(20f * Mathf.Deg2Rad);

    /// <summary>방어막 바 배치(체력 바 가운데 기준). Configure와 생성 도구의 정적 배치 검사가 같은 계산을 쓴다.</summary>
    public readonly struct BarLayout
    {
        /// <summary>체력 바 가운데에서 방어막 바 가운데까지(HUD 단위).</summary>
        public readonly Vector2 Offset;
        public readonly Vector2 Size;
        /// <summary>칸 줄의 체력 기울임 좌표 범위(왼쪽 끝 = 체력 칸 SkippedCells개 뒤, 오른쪽 끝 = 체력 칸 끝)와 칸 간격·틈·기울기.</summary>
        public readonly float CellStart, CellEnd, Pitch, Gap, Slant, HpSpan;
        public readonly int Cells;
        /// <summary>방어막 사각형 가운데의 체력 기울임 좌표(= 칸 줄 가운데).</summary>
        public float SkewMid => (CellStart + CellEnd) * 0.5f;

        public BarLayout(float hpSpan, float hpCells, float gap, float slant)
        {
            hpCells = Mathf.Max(1f, Mathf.Round(hpCells));
            HpSpan = hpSpan;
            Pitch = hpSpan / hpCells;
            int skipped = Mathf.Clamp(SkippedCells, 0, (int)hpCells - 1);
            Cells = (int)hpCells - skipped;
            CellStart = Pitch * skipped;
            CellEnd = hpSpan;
            Gap = gap;
            Slant = slant;
            float halfHeight = CellHalfHeight + VerticalPad;
            Size = new Vector2(CellEnd - CellStart + 2f * halfHeight * Mathf.Abs(slant) + 2f * EdgePad, 2f * halfHeight);
            // 체력 기울임 좌표 u = q.x - q.y × slant + span/2(q = 체력 바 가운데 기준). 방어막 사각형 가운데의 u가 칸 줄 가운데가 되도록 옆으로 민다.
            Offset = new Vector2((CellStart + CellEnd) * 0.5f - hpSpan * 0.5f + CenterRise * slant, CenterRise);
        }

        /// <summary>체력 기울임 좌표 u, 체력 바 가운데 기준 높이 y인 점의 가로 자리(체력 바 가운데 기준).</summary>
        public float SkewToX(float u, float y) => u - HpSpan * 0.5f + y * Slant;
    }

    private static readonly int FillId = Shader.PropertyToID("_Fill");
    private static readonly int TrailId = Shader.PropertyToID("_Trail");
    private static readonly int ChargeId = Shader.PropertyToID("_Charge");
    private static readonly int ScanTimeId = Shader.PropertyToID("_ScanTime");
    private static readonly int PulseId = Shader.PropertyToID("_Pulse");
    private static readonly int PulseXId = Shader.PropertyToID("_PulseX");
    private static readonly int PulseAgeId = Shader.PropertyToID("_PulseAge");
    private static readonly int FlashId = Shader.PropertyToID("_Flash");
    private static readonly int SweepId = Shader.PropertyToID("_Sweep");
    private static readonly int BreakId = Shader.PropertyToID("_Break");
    private static readonly int BreakFromId = Shader.PropertyToID("_BreakFrom");
    private static readonly int LowId = Shader.PropertyToID("_Low");
    private static readonly int ClockId = Shader.PropertyToID("_Clock");
    private static readonly int SlantId = Shader.PropertyToID("_Slant");
    private static readonly int SkewOriginId = Shader.PropertyToID("_SkewOrigin");
    private static readonly int CellStartId = Shader.PropertyToID("_CellStart");
    private static readonly int CellEndId = Shader.PropertyToID("_CellEnd");
    private static readonly int PitchId = Shader.PropertyToID("_Pitch");
    private static readonly int CellGapId = Shader.PropertyToID("_CellGap");
    private static readonly int CellHalfHeightId = Shader.PropertyToID("_CellHalfHeight");

    [Header("Show / Hide")]
    [SerializeField, Min(0.01f)] private float fadeInTime = 0.15f;
    [SerializeField, Min(0.01f)] private float fadeOutTime = 0.25f;

    [Header("Events (실제 시간)")]
    [Tooltip("전개: 빈 칸이 왼쪽부터 다 차는 시간")]
    [SerializeField, Min(0.01f)] private float raiseTime = 0.3f;
    [Tooltip("재충전(반격 성공): 남은 곳에서 가득까지 차는 시간")]
    [SerializeField, Min(0.01f)] private float refillTime = 0.22f;
    [Tooltip("전개·재충전 때 칸 줄을 지나가는 빛 띠의 시간")]
    [SerializeField, Min(0.01f)] private float sweepTime = 0.38f;
    [Tooltip("막은 양이 남아 있는 시간과 따라 줄어드는 시간(체력 바 깎인 양과 같은 값)")]
    [SerializeField, Min(0f)] private float trailDelay = 0.5f;
    [SerializeField, Min(0.01f)] private float trailDuration = 0.5f;
    [Tooltip("파괴: 번쩍임 → 금 → 칸마다 깜빡이며 꺼짐까지")]
    [SerializeField, Min(0.01f)] private float breakTime = 0.55f;
    [SerializeField, Min(0.01f)] private float flashTime = 0.3f;
    [SerializeField, Min(0.01f)] private float pulseTime = 0.6f;
    [Tooltip("충전 표시가 실제 충전량을 따라가는 빠르기(초당 비율)")]
    [SerializeField, Min(0.01f)] private float chargeFollowSpeed = 3f;
    [Tooltip("이 비율 아래에서 켜진 칸이 숨 쉬듯 어두워졌다 돌아온다")]
    [SerializeField, Range(0.05f, 0.9f)] private float lowThreshold = 0.3f;

    [Header("Punch (바가 위아래로 튐)")]
    [Tooltip("체력 바(0.26초, 0.08 ~ 0.3)보다 조금 작게 튄다 — 가장 크게 튀어도 칸 아래 끝이 체력 레일 위 테두리에 닿지 않는다")]
    [SerializeField, Min(0.01f)] private float punchTime = 0.26f;
    [SerializeField] private float punchMin = 0.06f;
    [SerializeField] private float punchMax = 0.22f;
    [Tooltip("바뀐 양이 이 비율 이상이면 가장 크게 튄다")]
    [SerializeField, Min(0.01f)] private float punchFullChange = 0.25f;

    private Material instance;
    private bool configured, warned;
    private PlayerWeaponTraits observed;
    private uint observedGeneration;
    private bool hammerEquipped;

    // 채움(차오름 연출 중이면 fillFrom → fillTo)
    private float shownFill, fillFrom, fillTo, fillStart = -100f, fillDuration;
    // 막은 양(trailFrom에서 trailStart부터 shownFill로 줄어든다)
    private float trail, trailFrom, trailStart = -100f;
    private bool trailActive;
    private float shownCharge;
    private float flash, pulse, pulseX = 1f, pulseAge = 9f, scanTime;
    private float sweepStart = -100f, breakStart = -100f, breakFrom;
    private float alpha;
    private float punchStart = -100f, punchAmount, punch;
    private PlayerWeaponTraits.BarrierEventType lastEvent;
    private float lastEventTime = -100f;

    // ===== 검증·디버그가 읽기만 하는 값 =====
    /// <summary>바가 보이는가(페이드 중 포함).</summary>
    public bool IsShown => alpha > 0.001f;
    public float Alpha => alpha;
    /// <summary>지금 그리는 남은 방어막(0~1, 차오름 연출 중이면 연출 값).</summary>
    public float DisplayedFill => shownFill;
    /// <summary>지금 그리는 막은 양의 끝(0~1, 없으면 DisplayedFill 이하).</summary>
    public float DisplayedTrail => trail;
    public float DisplayedCharge => shownCharge;
    public bool IsBreaking => BreakProgress(Time.unscaledTime) >= 0f;
    public bool IsTrailing => trailActive;
    public bool HammerEquipped => hammerEquipped;
    public PlayerWeaponTraits.BarrierEventType LastEvent => lastEvent;
    /// <summary>마지막 사건을 받은 실제 시각(Time.unscaledTime, 없으면 -100).</summary>
    public float LastEventTime => lastEventTime;
    public bool IsMaterialReady => instance != null;
    /// <summary>칸 수(체력 칸 수 - SkippedCells, Configure 뒤 값).</summary>
    public int CellCount { get; private set; }
    /// <summary>Configure가 정한 배치(체력 바 가운데 기준 자리·크기·칸 범위).</summary>
    public BarLayout Layout { get; private set; }

    /// <summary>PlayerHUD.Start가 부른다. 체력 바 사각형·재질(칸 배치)을 읽어 그 바로 위에 자리를 잡는다.</summary>
    public void Configure(RectTransform hpBar)
    {
        raycastTarget = false;
        maskable = false;
        float span = FallbackSpan, cells = FallbackCells, gap = FallbackGap, slant = FallbackSlant;
        var hpMaterial = hpBar != null && hpBar.TryGetComponent<Graphic>(out var hpGraphic) ? hpGraphic.material : null;
        if (hpMaterial != null && hpMaterial.HasProperty("_CellSpan") && hpMaterial.HasProperty("_Cells") && hpMaterial.HasProperty("_Slant"))
        {
            span = hpMaterial.GetFloat("_CellSpan");
            cells = Mathf.Max(1f, Mathf.Round(hpMaterial.GetFloat("_Cells")));
            slant = hpMaterial.GetFloat("_Slant");
            if (hpMaterial.HasProperty("_CellGap")) gap = hpMaterial.GetFloat("_CellGap");
        }
        var layout = new BarLayout(span, cells, gap, slant);
        CellCount = layout.Cells;
        Layout = layout;
        var offset = layout.Offset;

        var rt = rectTransform;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = layout.Size;
        rt.localRotation = Quaternion.identity;
        rt.localScale = Vector3.one;
        if (hpBar != null && hpBar.parent == rt.parent && hpBar.anchorMin == hpBar.anchorMax)
        {
            // 체력 바와 같은 한 점 앵커를 쓰면 HUD 크기가 바뀌어도 함께 움직인다.
            rt.anchorMin = rt.anchorMax = hpBar.anchorMin;
            Vector2 hpCenter = hpBar.anchoredPosition + Vector2.Scale(new Vector2(0.5f, 0.5f) - hpBar.pivot, hpBar.sizeDelta);
            rt.anchoredPosition = hpCenter + offset;
            rt.SetSiblingIndex(hpBar.GetSiblingIndex() + 1);   // 체력 바 바로 다음(아이콘·무기 칸보다 아래)에 그린다
        }
        else if (hpBar != null)
        {
            Vector3 hpCenter = hpBar.localPosition + (Vector3)hpBar.rect.center;
            rt.localPosition = (rt.parent != null && hpBar.parent != null
                ? rt.parent.InverseTransformPoint(hpBar.parent.TransformPoint(hpCenter))
                : hpCenter) + (Vector3)offset;
        }

        EnsureMaterial();
        if (instance != null)
        {
            instance.SetFloat(SlantId, layout.Slant);
            instance.SetFloat(SkewOriginId, layout.SkewMid);
            instance.SetFloat(CellStartId, layout.CellStart);
            instance.SetFloat(CellEndId, layout.CellEnd);
            instance.SetFloat(PitchId, layout.Pitch);
            instance.SetFloat(CellGapId, layout.Gap);
            instance.SetFloat(CellHalfHeightId, CellHalfHeight);
        }
        configured = true;
        alpha = 0f;
        Snap(null);
        Apply(Time.unscaledTime);
        SetVerticesDirty();
    }

    private void EnsureMaterial()
    {
        if (instance != null) return;
        var source = m_Material != null && m_Material.shader != null && m_Material.shader.name == ShaderName ? m_Material : null;
        if (source == null)
        {
            var shader = Shader.Find(ShaderName);
            if (shader != null) source = new Material(shader);
        }
        if (source == null)
        {
            if (!warned) Debug.LogWarning("[PlayerShieldBar] UI/PlayerHudShieldBar 재질이 없어 방어막 바를 그리지 않습니다(tools/Unity/PlayerShieldBarSetup.Run).");
            warned = true;
            return;
        }
        // 값을 매 프레임 넣으므로 이 바만 쓰는 사본을 만든다.
        instance = new Material(source) { name = source.name + " (HUD)", hideFlags = HideFlags.DontSave };
        if (source != m_Material) Destroy(source);
        material = instance;
    }

    // 다른 Traits로 바뀌었거나 초기화(사망·Reset)됐으면 연출 없이 지금 값에 맞춘다.
    private void Snap(PlayerWeaponTraits traits)
    {
        if (observed != traits)
        {
            if (observed != null) observed.BarrierEvent -= OnBarrierEvent;
            observed = traits;
            if (observed != null) observed.BarrierEvent += OnBarrierEvent;
        }
        observedGeneration = traits != null ? traits.Generation : 0u;
        float barrier = Barrier01(traits);
        shownFill = fillFrom = fillTo = barrier;
        fillStart = -100f;
        fillDuration = 0f;
        trail = trailFrom = barrier;
        trailStart = -100f;
        trailActive = false;
        shownCharge = barrier > 0f ? 0f : Charge01(traits);
        flash = pulse = 0f;
        pulseAge = 9f;
        sweepStart = breakStart = -100f;
        punchStart = -100f;
        punch = 0f;
    }

    private static float Barrier01(PlayerWeaponTraits traits) =>
        traits != null ? Mathf.Clamp01(traits.Barrier / PlayerWeaponTraits.BarrierCapacity) : 0f;

    private static float Charge01(PlayerWeaponTraits traits) =>
        traits != null ? Mathf.Clamp01(traits.BarrierCharge / PlayerWeaponTraits.BarrierCapacity) : 0f;

    // PlayerWeaponTraits.Absorb·AddBarrierCharge·FillBarrier 안에서 바로 불린다(이때 Barrier는 이미 새 값).
    private void OnBarrierEvent(PlayerWeaponTraits.BarrierEventType type)
    {
        if (!configured || observed == null) return;
        float now = Time.unscaledTime;
        float barrier = Barrier01(observed);
        lastEvent = type;
        lastEventTime = now;
        switch (type)
        {
            case PlayerWeaponTraits.BarrierEventType.Raised:
                breakStart = -100f;
                ClearTrail();
                shownCharge = 0f;
                StartRise(0f, barrier, raiseTime, now);
                sweepStart = now;
                flash = 1f;
                Punch(1f, now);
                break;
            case PlayerWeaponTraits.BarrierEventType.Refilled:
            {
                float from = CurrentFill(now);
                ClearTrail();
                StartRise(from, barrier, refillTime, now);
                sweepStart = now;
                flash = Mathf.Max(flash, 0.6f);
                pulse = 1f;
                pulseX = barrier;
                Punch(barrier - from, now);
                break;
            }
            case PlayerWeaponTraits.BarrierEventType.Hit:
                Drop(barrier, now, true);
                break;
            case PlayerWeaponTraits.BarrierEventType.Broken:
                breakFrom = Mathf.Max(CurrentFill(now), trailActive ? CurrentTrail(now) : 0f);
                breakStart = now;
                ClearTrail();
                StartRise(0f, 0f, 0f, now);
                flash = 1f;
                pulse = 0f;
                Punch(1f, now);
                break;
        }
    }

    // 방어막이 줄었다: 막은 양을 남겼다가 줄이고, 그 지점이 번쩍이며 바가 튄다.
    private void Drop(float barrier, float now, bool fromEvent)
    {
        float before = Mathf.Max(CurrentFill(now), fillTo);
        float lingering = trailActive ? CurrentTrail(now) : 0f;
        StartRise(barrier, barrier, 0f, now);
        trailFrom = Mathf.Max(before, lingering);
        trailStart = now + trailDelay;
        trailActive = trailFrom > barrier + 0.0005f;
        trail = trailFrom;
        pulse = 1f;
        pulseX = barrier;
        pulseAge = 0f;
        if (fromEvent) flash = Mathf.Max(flash, 0.25f);
        Punch(before - barrier, now);
    }

    private void StartRise(float from, float to, float duration, float now)
    {
        fillFrom = from;
        fillTo = to;
        fillStart = now;
        fillDuration = duration;
        shownFill = duration > 0f ? from : to;
    }

    private float CurrentFill(float now)
    {
        if (fillDuration <= 0f) return fillTo;
        float t = Mathf.Clamp01((now - fillStart) / fillDuration);
        float eased = 1f - (1f - t) * (1f - t) * (1f - t);      // 끝으로 갈수록 느려진다
        return Mathf.Lerp(fillFrom, fillTo, eased);
    }

    private float CurrentTrail(float now)
    {
        if (!trailActive) return shownFill;
        float t = Mathf.Clamp01((now - trailStart) / trailDuration);
        float eased = 1f - (1f - t) * (1f - t);
        return Mathf.Lerp(trailFrom, shownFill, eased);
    }

    private void ClearTrail()
    {
        trailActive = false;
        trailStart = -100f;
    }

    // 파괴 진행(0~1), 끝났거나 없으면 -1
    private float BreakProgress(float now)
    {
        float t = (now - breakStart) / breakTime;
        return t >= 0f && t < 1f ? t : -1f;
    }

    private void Punch(float change, float now)
    {
        punchAmount = Mathf.Lerp(punchMin, punchMax, Mathf.Clamp01(Mathf.Abs(change) / punchFullChange));
        punchStart = now;
    }

    private void LateUpdate()
    {
        if (!configured) return;
        float now = Time.unscaledTime, dt = Time.unscaledDeltaTime;
        var game = GameManager.Instance;
        var player = game != null ? game.Player : null;
        var abilities = player != null ? player.AbilityManager : null;
        var attack = abilities != null ? abilities.AttackAbility : null;
        var traits = attack != null ? attack.Traits : null;
        if (traits != observed || (traits != null && traits.Generation != observedGeneration)) Snap(traits);

        bool alive = player != null && (player.Health == null || !player.Health.IsDead);
        if (traits == null || !alive || instance == null)
        {
            // 쓰러졌거나 그릴 수 없으면 페이드 없이 바로 숨긴다(사망 연출이 시간을 멈춰도 숨김이 늦지 않게).
            alpha = 0f;
            Apply(now);
            return;
        }

        hammerEquipped = false;
        for (int i = 0; i < attack.WeaponSetCount; i++)
        {
            var armament = attack.GetEquippedArmament(i);
            if (armament != null && armament.armamentId == "Hammer") { hammerEquipped = true; break; }
        }

        float barrier = Barrier01(traits);
        float charge = Charge01(traits);

        // 사건 없이 바뀐 값(같은 프레임에 사건이 겹침·늦은 연결 등)도 실제 값에 맞춘다.
        if (fillDuration <= 0f || now - fillStart >= fillDuration)
        {
            if (barrier < fillTo - 0.0005f) Drop(barrier, now, false);
            else if (barrier > fillTo + 0.0005f) StartRise(CurrentFill(now), barrier, refillTime, now);
        }
        shownFill = CurrentFill(now);

        if (trailActive)
        {
            trail = CurrentTrail(now);
            if (now >= trailStart + trailDuration || trail <= shownFill + 0.0005f) ClearTrail();
        }
        if (!trailActive) trail = shownFill;

        // 충전량: 방어막이 있으면 0. 오를 때는 따라 오르며 그 앞끝이 잠깐 밝아지고, 줄면(초기화 등) 바로 맞춘다.
        if (barrier > 0f || fillTo > 0f) shownCharge = 0f;
        else if (charge > shownCharge + 0.0005f)
        {
            if (charge > shownCharge + 0.01f)
            {
                pulse = Mathf.Max(pulse, 0.6f);
                pulseX = charge;
            }
            shownCharge = Mathf.MoveTowards(shownCharge, charge, chargeFollowSpeed * dt);
        }
        else shownCharge = charge;

        bool breaking = BreakProgress(now) >= 0f;
        bool want = hammerEquipped || barrier > 0f || charge > 0f || breaking || trailActive;
        alpha = Mathf.MoveTowards(alpha, want ? 1f : 0f, dt / (want ? fadeInTime : fadeOutTime));

        flash = Mathf.MoveTowards(flash, 0f, dt / flashTime);
        pulse = Mathf.MoveTowards(pulse, 0f, dt / pulseTime);
        pulseAge += dt;
        scanTime += dt;

        float punchAge = now - punchStart;
        punch = punchAge >= 0f && punchAge < punchTime
            ? punchAmount * Mathf.Sin(punchAge / punchTime * Mathf.PI * 3f) * (1f - punchAge / punchTime)
            : 0f;
        Apply(now);
    }

    private void Apply(float now)
    {
        if (canvasRenderer != null) canvasRenderer.SetAlpha(alpha);
        var rt = rectTransform;
        var scale = new Vector3(1f, 1f + punch, 1f);
        if (rt.localScale != scale) rt.localScale = scale;
        if (instance == null) return;
        float sweep = (now - sweepStart) / sweepTime;
        float breakProgress = BreakProgress(now);
        bool deployed = shownFill > 0.0005f && breakProgress < 0f;
        instance.SetFloat(FillId, shownFill);
        instance.SetFloat(TrailId, trailActive ? trail : shownFill);
        instance.SetFloat(ChargeId, shownCharge);
        instance.SetFloat(ScanTimeId, scanTime);
        instance.SetFloat(PulseId, pulse);
        instance.SetFloat(PulseXId, pulseX);
        instance.SetFloat(PulseAgeId, pulseAge);
        instance.SetFloat(FlashId, flash);
        instance.SetFloat(SweepId, sweep >= 0f && sweep < 1f ? sweep : -1f);
        instance.SetFloat(BreakId, breakProgress);
        instance.SetFloat(BreakFromId, breakFrom);
        instance.SetFloat(LowId, deployed && fillTo < lowThreshold ? 1f : 0f);
        instance.SetFloat(ClockId, Mathf.Repeat(now, 600f));
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        Rect r = rectTransform.rect;
        Vector2 c = r.center;
        var vertex = UIVertex.simpleVert;
        vertex.color = color;
        for (int i = 0; i < 4; i++)
        {
            float x = i < 2 ? r.xMin : r.xMax;
            float y = i == 0 || i == 3 ? r.yMin : r.yMax;
            vertex.position = new Vector3(x, y, 0f);
            vertex.uv0 = new Vector4(x - c.x, y - c.y, 0f, 0f);    // 사각형 가운데 기준 Canvas 단위(셰이더가 칸을 그린다)
            vh.AddVert(vertex);
        }
        vh.AddTriangle(0, 1, 2);
        vh.AddTriangle(0, 2, 3);
    }

    protected override void OnDestroy()
    {
        if (observed != null) observed.BarrierEvent -= OnBarrierEvent;
        observed = null;
        if (instance != null)
        {
            if (Application.isPlaying) Destroy(instance);
            else DestroyImmediate(instance);
            instance = null;
        }
        base.OnDestroy();
    }
}
