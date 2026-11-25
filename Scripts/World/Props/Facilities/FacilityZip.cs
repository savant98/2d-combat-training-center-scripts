using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 붙기(zip, DESIGN_V4 §2.2 · 5차 DESIGN_V5 §1 크레인) — 상호작용 키(↑)로 반경 안의 크레인(IZipAnchor)에 빠르게 날아가 매달린다. 정적 관리자 + 숨은 실행 개체 하나.
/// - 입력: PlayerController.OnInteractInput을 구독한다(상태 기계를 거치지 않아 점프·대시 중에도 들어온다). 받은 입력은 선입력 0.2초 동안 남는다.
///   무시 조건(사용자 결정 2026-11-25 '오작동 방지'): 무장 공격 중·공격이 끝난 뒤 0.15초 안·위 조준 유지 중(↑가 이미 0.1초 넘게 눌려 있던 채 들어온 입력)·
///   보통 상호작용 지점의 키 안내가 떠 있을 때(FacilityInteractPoint.Live의 HintVisible — 레버·수리대·텔레포트·출구가 우선)·사망·피격 경직·입력 차단·
///   Wave 정지/준비·관 통과·던전 조건 충족 뒤(FacilityAccess.CombatAllows false)·크레인이 아닌 다른 구속 중·붙는 중·도착 뒤 0.1초.
///   4차 통합(붙기 오작동 보강): 받은 ↑는 0.08초(AttackCancelWindow) 동안 확정하지 않고 기다린다. 그 안에 무장 공격 입력(기본 무기·보조 무기·기어)이
///   들어오면 위 조준 공격으로 보고 아직 이동을 시작하지 않은 붙기를 취소한다. 확정 대기는 선입력 0.2초 안에서 일어나 선입력 규칙과 겹치지 않는다.
/// - 대상 선택(5차, 매 프레임 표시용): 반경 안 && 쓸 수 있음 && 시야 열림. 매달린 크레인은 뺀다.
///   방향 입력 없음 → 순수 가장 가까움(바라보는 쪽 가산 없음). 좌우·↓ 입력 → 그 방향 반각 100° 콘(내적 ≥ −0.17) 안에서 거리 × (1 + 0.6 × (1 − 내적)) 최소,
///   콘 안에 후보가 없으면 '대상 없음'(되돌아가는 붙기 방지). ↑는 방향으로 쓰지 않는다(키보드 기본 배치에서 ↑ = 상호작용 — 입력 설정 무수정).
///   방향은 기존 이동 입력만 읽는다(PlayerController.MoveInput.x, InputManager.MoveDownHeld).
///   5차 검토 c6(연속 이동 때 원래 크레인으로 돌아오지 않게): 매달린 대상이 바뀌면(점프·대시·조건 충족 하차, 연쇄 붙기) 직전 대상을 1.5초(LeftMemory) 기억하고,
///   방향 입력이 없을 때만 그 대상의 점수에 × 1.5(1 + LeftPenalty)를 곱한다(후보에서 빼지는 않는다 — 그것뿐이면 그대로 고른다. 방향 입력이 있으면 벌점 없음).
///   점수가 TieEpsilon(0.001칸) 안으로 같으면 매달림 발이 높은 대상을 고른다(등록 순서·부동소수 오차로 정하지 않는다 — 세로 사슬은 위로 오르는 쪽).
/// - 누른 순간 고정(5차): ↑를 받은 순간 표시 중인 대상(지난 프레임 선택)과 방향을 저장한다. 0.08초 확정 대기 동안 표시는 그 대상에 고정하고,
///   확정 때 그 대상이 여전히 유효하면(쓸 수 있음·반경·시야를 새로 물음) 그 대상으로 붙는다. 무효면 저장한 방향으로 다시 고른다.
/// - 호출(5차 B안): 확정한 대상이 IZipCallTarget(경로 크레인의 빈 도크)이면 붙지 않고 크레인을 부른다. 크레인이 도착했을 때 그 도크가 여전히
///   선택돼 있으면 TryAutoZip으로 붙기를 자동으로 시작한다(무시 조건·반경·새 시야 질의를 그대로 본다 — 아니면 도크에서 기다린다).
/// - 시야(물리 질의는 모터 공개 질의만): 몸통 중심과 진행 방향의 옆으로 몸 반폭 0.8배씩 띄운 세 점에서 RaycastMotor2D.RaycastEnvironment 3줄
///   (가로로 날면 몸 위·가운데·아래) + 도착 자리 IsBodyBlockedAt. 위쪽으로 쏘는 광선은 통과 발판(OneWay)을 무시한다(모터 규칙).
///   fix4zip: IsBodyBlockedAt는 Physics2D.SyncTransforms를 강제하므로 표시용 선택은 후보별 결과를 0.1초(SightReuseTime) 동안,
///   Player 몸 중심 칸·대상 매달림 발 칸이 그대로일 때만 재사용한다. 실제 붙기 확정(Begin 직전)과 거절음 양보 판단(WouldTakePress)은 늘 새로 묻는다.
/// - 상호작용 지점 양보(fix4zip): 키 안내가 숨은(쓸 수 없는) 지점이 같은 ↑를 거절할 때, 이 입력을 붙기가 받을 수 있으면(WouldTakePress)
///   지점은 거절음·Rejections 대신 붙기에 양보한다. 키 안내가 보이는 지점이 있으면 지금처럼 붙기가 양보한다.
/// - 실행(물리 틱 코루틴): 대시를 끊고 점프·대시 선입력을 지운다 → 자체 보유자 WavePlayerStart.HoldForPassage(true)(보호 = 피해 무시·잡기 차단) →
///   매달려 있었으면 이전 크레인 탑승을 끝낸다(구속은 보유자 여럿 규칙으로 유지) → ShiftConfinement로 직선 60칸/초(시간 clamp(거리/60, 0.06, 0.25)) →
///   도착하면 크레인에 인계(IZipAnchor.AcceptZip → FacilityRide.BeginHandoff) → 0.15초 동안 보호 유지(그 사이 크레인에서 내리면 바로 푼다) → 붙기 보유자를 놓는다.
///   위치를 직접 바꾸지 않는다(AGENTS 물리 규칙). 실패·중단(대상 사라짐·사망·정지)이면 보유자를 놓고, 몸이 지형과 겹치면 출발 자리로 FacilityRide.RelocateFeet.
/// - 던전에서는 붙는 동안 IDungeonFacilityHost.SetFacilityBusy(this, true)(매달린 동안은 크레인 탑승이 알린다).
/// 실행 개체는 첫 대상이 등록될 때(Play 중에만) 만들고 장면이 바뀌어도 남는다(DontDestroyOnLoad, 계층 창에서 숨김).
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(150)]   // 크레인(100)이 하차를 처리한 뒤 도착 보호를 푼다.
public sealed class FacilityZip : MonoBehaviour
{
    public const float BufferTime = .2f;          // 선입력(점프 선입력과 같은 값)
    public const float AttackGrace = .15f;        // 공격이 끝난 뒤 붙기를 받지 않는 시간
    public const float UpHoldLimit = .1f;         // ↑가 이만큼 이미 눌려 있던 채 들어온 상호작용 입력은 위 조준으로 본다(판단값)
    public const float Speed = 60f;               // 칸/초(대시 30의 2배)
    public const float MinTime = .06f, MaxTime = .25f;
    public const float ArrivalGuard = .15f;       // 도착 뒤 보호 유지
    public const float RegrabDelay = .1f;         // 도착 뒤 다시 붙지 못하는 시간
    public const float AimDeadZone = .3f;         // 5차: 좌우 이동 입력을 방향으로 보는 최소 크기
    public const float ConeCos = -.17364818f;     // 5차: 방향 콘 반각 100°(cos 100°, DESIGN_V5 판단값)
    public const float ForwardWeight = .6f;       // 5차: 앞쪽 가중 — 점수 = 거리 × (1 + 0.6 × (1 − 내적))
    public const float SightInset = .8f;          // 옆 광선을 몸 반폭의 몇 배에 둘지
    public const float AttackCancelWindow = .08f; // ↑ 뒤 이 시간 안에 무장 공격 입력이 오면 붙기 취소(4차 통합 판단값, 선입력 BufferTime보다 짧아야 한다)
    public const float SightReuseTime = .1f;      // 표시용 시야 결과를 재사용하는 시간(fix4zip 판단값 — 확정 직전에는 늘 새로 묻는다)
    public const float LeftPenalty = .5f;         // c6: 방향 입력이 없을 때 직전에 떠난 대상의 점수 배율 1 + 0.5(판단값)
    public const float LeftMemory = 1.5f;         // c6: 직전에 떠난 대상을 기억하는 시간(초, 판단값)
    public const float TieEpsilon = 1e-3f;        // c6: 이 폭(칸) 안으로 점수가 같으면 높은 대상 우선

    private static readonly List<IZipAnchor> anchors = new List<IZipAnchor>();
    private static FacilityZip runner;

    /// <summary>살아 있는 붙기 대상(읽기 전용).</summary>
    public static IReadOnlyList<IZipAnchor> Anchors => anchors;
    /// <summary>실행 개체(없으면 null — 첫 대상이 등록될 때 만든다).</summary>
    public static FacilityZip Runner => runner;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        anchors.Clear();
        runner = null;
    }

    public static void Register(IZipAnchor anchor)
    {
        if (anchor == null || !Application.isPlaying) return;
        if (!anchors.Contains(anchor)) anchors.Add(anchor);
        EnsureRunner();
    }

    public static void Unregister(IZipAnchor anchor)
    {
        if (anchor == null) return;
        anchors.Remove(anchor);
        if (runner != null) runner.OnAnchorRemoved(anchor);
    }

    /// <summary>실행 개체를 만든다(이미 있으면 그대로). Play 중에만.</summary>
    public static FacilityZip EnsureRunner()
    {
        if (runner != null || !Application.isPlaying) return runner;
        var go = new GameObject("FacilityZip") { hideFlags = HideFlags.HideInHierarchy };
        DontDestroyOnLoad(go);
        runner = go.AddComponent<FacilityZip>();
        return runner;
    }

    /// <summary>player가 지금 어떤 붙기 대상에 매달려 있는지(없으면 null).</summary>
    public static IZipAnchor HangingOn(PlayerController player)
    {
        if (player == null) return null;
        for (int i = 0; i < anchors.Count; i++)
            if (anchors[i] != null && anchors[i].HasRider(player)) return anchors[i];
        return null;
    }

    private PlayerController player;
    private WavePlayerStart hold;
    private readonly InputBuffer buffer = new InputBuffer(BufferTime);
    private float lastAttackTime = -999f;
    private float pressTime = -999f;          // 받아들인 마지막 ↑ 시각(확정 대기 기준)
    private float attackInputTime = -999f;    // 마지막 무장 공격 입력 시각(공격 실행 여부와 무관)
    private float upHeld;
    private IZipAnchor focused;
    private IZipAnchor target;
    private IZipAnchor guardAnchor;
    private IZipAnchor pressTarget;           // 5차: 누른 순간 표시 중이던 대상(확정 대기 동안 고정)
    private Vector2 pressAim;                 // 5차: 누른 순간의 방향 입력(고정 대상이 무효일 때 다시 고르는 기준)
    private Vector2? aimOverride;             // 검사용 방향 입력(입력 장치 없이 시험)
    private IZipAnchor hangingLast;           // c6: 지난 프레임에 매달려 있던 대상
    private IZipAnchor lastLeft;              // c6: 직전에 떠난 대상(내림·연쇄 붙기)
    private float lastLeftTime = -999f;
    private float guardUntil, regrabAt = -999f;
    private bool zipping;
    private Coroutine routine;
    private IDungeonFacilityHost busyHost;
    private Vector2 startFeet;

    // 후보별 시야 판정 기억(표시용 선택이 매 프레임 모터 질의 — SyncTransforms 강제 — 를 하지 않게). 열쇠는 대상 개체(참조 비교, 할당 없음).
    private struct SightMemo
    {
        public float At;
        public Vector2Int PlayerCell, AnchorCell;
        public bool Clear;
    }
    private readonly Dictionary<IZipAnchor, SightMemo> sightMemo = new Dictionary<IZipAnchor, SightMemo>();

    /// <summary>지금 고른 붙기 대상(검사용·크레인 직선 표시).</summary>
    public IZipAnchor Focused => focused;
    /// <summary>5차: 누른 순간 고정한 대상(확정 대기 중에만, 없으면 null). 크레인은 이때 직선을 실선으로 밝힌다.</summary>
    public IZipAnchor Locked => buffer.IsValid && !zipping ? pressTarget : null;
    /// <summary>5차: 이번 프레임에 후보 표시(직선·흐린 표적 괄호)를 보일 수 있는 상태인지(키 안내 우선·입력 차단·붙는 중이면 false).</summary>
    public bool CandidatesShown { get; private set; }
    /// <summary>5차: 이번 프레임 선택에 쓴 방향 입력(x ∈ {−1, 0, 1}, y ∈ {−1, 0} — 검사용).</summary>
    public Vector2 Aim { get; private set; }
    /// <summary>붙는 중인지(검사용).</summary>
    public bool IsZipping => zipping;
    /// <summary>도착 뒤 보호를 유지하는 중인지(검사용).</summary>
    public bool GuardActive => guardAnchor != null;
    /// <summary>받은 상호작용 입력 수·무시한 수·시작한 붙기 수·도착 수·중단 수(검사용).</summary>
    public int Presses { get; private set; }
    public int Ignored { get; private set; }
    public int Zips { get; private set; }
    public int Arrivals { get; private set; }
    public int Failures { get; private set; }
    /// <summary>5차: 빈 도크 호출 수·호출 도착 때 자동으로 시작한 붙기 수(검사용).</summary>
    public int Calls { get; private set; }
    public int AutoZips { get; private set; }
    /// <summary>마지막으로 입력을 무시한 이유(검사용).</summary>
    public string LastIgnoreReason { get; private set; } = string.Empty;
    /// <summary>마지막 붙기의 이동 시간(초, 검사용).</summary>
    public float LastDuration { get; private set; }
    /// <summary>마지막 붙기 대상(검사용 — 누른 순간 고정 확인).</summary>
    public IZipAnchor LastTarget { get; private set; }
    /// <summary>선입력이 남아 있는지(검사용).</summary>
    public bool HasBufferedInput => buffer.IsValid;
    /// <summary>시야를 모터에 실제로 물은 수·기억을 재사용한 수(검사용, fix4zip).</summary>
    public int SightQueries { get; private set; }
    public int SightReuses { get; private set; }
    /// <summary>c6: 직전에 떠난 대상(기억 시간 LeftMemory 안일 때만, 없으면 null — 검사용).</summary>
    public IZipAnchor RecentlyLeft => lastLeft != null && Time.time - lastLeftTime < LeftMemory ? lastLeft : null;

    /// <summary>검사용: 방향 입력을 대신 넣는다(null이면 실제 입력). 입력 장치 없이 방향 선택 규칙을 시험한다.</summary>
    public void SetAimOverride(Vector2? aim) => aimOverride = aim;

    /// <summary>
    /// 방금 들어온 상호작용 입력(↑)을 붙기가 받을지(fix4zip). 키 안내가 숨은 상호작용 지점이 같은 입력을 거절할 때 거절음을 낼지 정하는 데 쓴다.
    /// 입력 무시 조건(Blocked, 누른 순간 기준)에 걸리지 않고, 같은 프레임에 무장 공격 입력이 먼저 오지 않았고, 지금 시야를 새로 물어 고를 대상이 있으면 true.
    /// 5차: 누른 순간 고정 규칙과 같게 — 표시 중인 대상이 유효하면 그것, 아니면 지금 방향으로 다시 고른다. 상태를 바꾸지 않는다.
    /// </summary>
    public static bool WouldTakePress(PlayerController p)
    {
        var r = runner;
        if (r == null || p == null || !r.isActiveAndEnabled || r.player != p) return false;
        if (r.Blocked(p, true, out _)) return false;
        // 같은 프레임에 무장 공격 입력이 먼저 왔으면 확정 대기에서 취소된다(Update의 AttackCancelWindow 규칙).
        if (r.attackInputTime >= Time.time) return false;
        if (r.focused != null && r.Valid(p, r.focused, true)) return true;
        return r.Pick(p, true, r.ReadAim(p)) != null;
    }

    /// <summary>
    /// 5차 호출 B안: 불린 크레인(anchor)이 도크(dock — 호출 지점)에 도착했을 때 부른다. caller가 지금도 그 도크를 선택 중이고(지난 프레임 표시 대상),
    /// 무시 조건에 걸리지 않고, 반경·새 시야 질의를 통과하면 붙기를 시작한다. 아니면 false(크레인은 그 도크에서 기다린다).
    /// </summary>
    public static bool TryAutoZip(IZipAnchor anchor, IZipAnchor dock, PlayerController caller)
    {
        var r = runner;
        if (r == null || anchor == null || caller == null || !r.isActiveAndEnabled || r.player != caller) return false;
        if (!ReferenceEquals(r.focused, dock) && !ReferenceEquals(r.focused, anchor)) return false;
        if (r.zipping || r.Blocked(caller, false, out _)) return false;
        if (!r.Valid(caller, anchor, true)) return false;
        r.buffer.Reset();
        r.pressTarget = null;
        r.SetFocus(anchor);
        r.AutoZips++;
        r.Begin(anchor);
        return true;
    }

    private void OnDisable()
    {
        Cleanup(false);
        Bind(null);
        SetFocus(null);
    }

    private void OnDestroy()
    {
        if (runner == this) runner = null;
    }

    // ── 입력 ─────────────────────────────

    private void Bind(PlayerController next)
    {
        if (player == next) return;
        if (player != null)
        {
            player.OnInteractInput -= OnInteract;
            player.OnNormalWeapon -= OnAttackInput;
            player.OnSecondaryWeapon -= OnAttackInput;
            player.OnGearPressed -= OnAttackInput;
            if (zipping || guardAnchor != null) Cleanup(false);
        }
        player = next;
        sightMemo.Clear();
        buffer.Reset();
        pressTarget = null;
        hangingLast = lastLeft = null;
        lastLeftTime = -999f;
        upHeld = 0f;
        pressTime = attackInputTime = -999f;
        if (player != null)
        {
            player.OnInteractInput += OnInteract;
            player.OnNormalWeapon += OnAttackInput;
            player.OnSecondaryWeapon += OnAttackInput;
            player.OnGearPressed += OnAttackInput;
        }
    }

    private void OnInteract()
    {
        Presses++;
        if (Blocked(player, true, out string reason))
        {
            Ignored++;
            LastIgnoreReason = reason;
            buffer.Reset();
            pressTarget = null;
            return;
        }
        pressTime = Time.time;
        buffer.RegisterInput();
        // 5차: 누른 순간 표시 중이던 대상과 방향을 고정한다(확정 때 ↑가 아직 눌려 있어도 대상이 바뀌지 않게).
        pressTarget = focused;
        pressAim = ReadAim(player);
    }

    /// <summary>상호작용 키를 누른 것과 같은 경로(검사용 — 입력 장치 없이 부른다).</summary>
    public void Press() => OnInteract();

    // 무장 공격 입력(PlayerController가 공격 키를 누른 프레임에 알린다 — 같은 프레임이면 상호작용보다 먼저 온다). 공격이 실제로 시작됐는지는 보지 않는다.
    private void OnAttackInput() => attackInputTime = Time.time;

    /// <summary>무장 공격 키를 누른 것과 같은 경로(검사용).</summary>
    public void NoteAttackInput() => OnAttackInput();

    // 5차: 방향 입력(기존 이동 입력만 읽는다 — 입력 설정 무수정). x = 좌우(데드존 0.3), y = ↓만(−1). ↑는 쓰지 않는다.
    private Vector2 ReadAim(PlayerController p)
    {
        if (aimOverride.HasValue)
        {
            Vector2 o = aimOverride.Value;
            return new Vector2(Mathf.Abs(o.x) > AimDeadZone ? Mathf.Sign(o.x) : 0f, o.y < -AimDeadZone ? -1f : 0f);
        }
        if (p == null) return Vector2.zero;
        Vector2 move = p.MoveInput;
        float x = Mathf.Abs(move.x) > AimDeadZone ? Mathf.Sign(move.x) : 0f;
        float y = InputManager.Instance != null && InputManager.Instance.MoveDownHeld ? -1f : 0f;
        return new Vector2(x, y);
    }

    // ── 매 프레임 ─────────────────────────────

    private void Update()
    {
        Bind(GameManager.Instance != null ? GameManager.Instance.Player : null);
        if (player == null) { CandidatesShown = false; SetFocus(null); return; }
        TrackHanging();
        var attack = player.AbilityManager != null ? player.AbilityManager.AttackAbility : null;
        if (attack != null && attack.IsActive) lastAttackTime = Time.time;
        // 입력 처리(PlayerController.Update, 순서 0)는 이보다 먼저 돌아 지난 프레임까지의 값을 본다.
        upHeld = player.MoveInput.y > .5f ? upHeld + Time.deltaTime : 0f;
        UpdateGuard();

        bool allowed = !zipping && FocusAllowed(player);
        CandidatesShown = allowed;
        Aim = ReadAim(player);
        IZipAnchor best = null;
        if (allowed)
        {
            // 확정 대기 중에는 누른 순간의 대상을 그대로 표시한다(유효할 때만 — 무효가 되면 다시 고른다).
            if (buffer.IsValid && pressTarget != null && Valid(player, pressTarget, false)) best = pressTarget;
            else best = Pick(player, false, Aim);
        }
        SetFocus(best);
        if (!buffer.IsValid) { pressTarget = null; return; }
        if (Blocked(player, false, out _)) { buffer.Reset(); pressTarget = null; return; }
        // ↑ 뒤 0.08초 안에 무장 공격 입력이 왔으면 위 조준 공격으로 보고 버린다(아직 Begin 전 — 구속·이동 없음).
        if (attackInputTime >= pressTime && attackInputTime - pressTime <= AttackCancelWindow)
        {
            buffer.Reset();
            pressTarget = null;
            Ignored++;
            LastIgnoreReason = "↑ 뒤 공격 입력";
            return;
        }
        // 확정 대기: 누른 뒤 0.08초가 지나야 붙는다(그 사이 공격 입력을 기다린다). 부동소수 눈금(0.01초 Step) 여유 1e-4.
        if (Time.time - pressTime < AttackCancelWindow - 1e-4f) return;
        if (zipping) return;
        // 실제 붙기 직전에는 시야를 새로 묻는다(표시용 선택은 최대 0.1초 지난 기억을 쓴다 — fix4zip).
        // 5차: 누른 순간 고정한 대상이 여전히 유효하면 그 대상, 아니면 누른 순간의 방향으로 다시 고른다. 없으면 선입력이 남는 동안 다음 프레임에 다시 본다.
        var chosen = pressTarget != null && Valid(player, pressTarget, true) ? pressTarget : Pick(player, true, pressAim);
        if (chosen == null) return;
        buffer.Reset();
        pressTarget = null;
        SetFocus(chosen);
        if (chosen is IZipCallTarget call)
        {
            // 빈 도크: 붙지 않고 크레인을 부른다(구속·이동 없음). 도착 때 이 도크가 계속 선택돼 있으면 크레인이 TryAutoZip을 부른다.
            if (call.Call(player)) Calls++;
            return;
        }
        Begin(chosen);
    }

    // c6: 매달린 대상이 바뀌면(점프·대시·조건 충족 하차, 연쇄 붙기의 ReleaseForZip — 크레인(100)이 하차를 처리한 뒤 돈다) 직전 대상과 시각을 기억한다.
    private void TrackHanging()
    {
        var hang = HangingOn(player);
        if (hangingLast != null && !ReferenceEquals(hang, hangingLast))
        {
            lastLeft = hangingLast;
            lastLeftTime = Time.time;
        }
        hangingLast = hang;
    }

    // 도착 보호: 시간이 지나거나 그 크레인에서 내렸으면(점프·대시 하차) 붙기 보유자를 놓는다. 크레인 보유자가 남아 있으면 구속은 이어진다.
    private void UpdateGuard()
    {
        if (guardAnchor == null) return;
        if (Time.time < guardUntil && player != null && guardAnchor.HasRider(player) && anchors.Contains(guardAnchor)) return;
        guardAnchor = null;
        if (hold != null && hold.IsHoldingPlayer) hold.Release();
    }

    private void SetFocus(IZipAnchor next)
    {
        if (ReferenceEquals(focused, next)) return;
        var previous = focused;
        focused = next;
        if (previous != null && anchors.Contains(previous)) previous.SetZipFocus(false);
        next?.SetZipFocus(true);
    }

    private void OnAnchorRemoved(IZipAnchor anchor)
    {
        if (ReferenceEquals(focused, anchor)) focused = null;
        if (ReferenceEquals(pressTarget, anchor)) pressTarget = null;
        // c6: 풀로 돌아간 개체가 다른 크레인으로 다시 나와도 벌점을 이어받지 않게 기억을 지운다.
        if (ReferenceEquals(hangingLast, anchor)) hangingLast = null;
        if (ReferenceEquals(lastLeft, anchor)) lastLeft = null;
        sightMemo.Remove(anchor);
        if (ReferenceEquals(guardAnchor, anchor)) UpdateGuard();
    }

    // ── 조건 ─────────────────────────────

    // 키 안내·대상 표시를 보일 수 있는 상태(공격 타이밍 조건은 빼서 쏘는 동안 안내가 깜박이지 않게 한다).
    private bool FocusAllowed(PlayerController p)
    {
        if (p == null || !p.isActiveAndEnabled) return false;
        if (p.Health != null && (p.Health.IsDead || p.Health.IsDeathPending)) return false;
        if (!p.CanReceiveInput) return false;
        var abilities = p.AbilityManager;
        if (abilities != null && abilities.HitAbility != null && abilities.HitAbility.IsActive) return false;
        if (OtherHintVisible()) return false;
        if (!FacilityAccess.CombatAllows) return false;
        return !OtherHold(p);
    }

    /// <summary>입력을 받지 않을 이유(없으면 false). press = 방금 누른 입력(위 조준 유지 검사는 누른 순간에만).</summary>
    private bool Blocked(PlayerController p, bool press, out string reason)
    {
        reason = string.Empty;
        if (p == null || !p.isActiveAndEnabled) { reason = "Player 없음"; return true; }
        if (p.Health != null && (p.Health.IsDead || p.Health.IsDeathPending)) { reason = "사망"; return true; }
        if (!p.CanReceiveInput) { reason = "입력 차단"; return true; }
        var abilities = p.AbilityManager;
        if (abilities != null && abilities.HitAbility != null && abilities.HitAbility.IsActive) { reason = "피격 경직"; return true; }
        var attack = abilities != null ? abilities.AttackAbility : null;
        if (attack != null && attack.IsActive) { reason = "공격 중"; return true; }
        if (Time.time - lastAttackTime < AttackGrace) { reason = "공격 직후"; return true; }
        if (press && upHeld >= UpHoldLimit) { reason = "위 조준 유지"; return true; }
        if (OtherHintVisible()) { reason = "상호작용 안내 우선"; return true; }
        if (!FacilityAccess.CombatAllows) { reason = "진행 정지·조건 충족"; return true; }
        if (OtherHold(p)) { reason = "다른 구속"; return true; }
        if (zipping) { reason = "붙는 중"; return true; }
        if (Time.time < regrabAt) { reason = "재붙기 대기"; return true; }
        return false;
    }

    private static bool OtherHintVisible()
    {
        var points = FacilityInteractPoint.Live;
        for (int i = 0; i < points.Count; i++)
            if (points[i] != null && points[i].HintVisible) return true;
        return false;
    }

    // 크레인 탑승·붙기 보호가 아닌 다른 구속(관 통과·Wave 준비 상자 등)에 걸려 있는지.
    private bool OtherHold(PlayerController p)
    {
        bool confined = p.Motor != null && p.Motor.IsConfined;
        bool held = WavePlayerStart.IsHolding(p);
        if (!confined && !held) return false;
        if (guardAnchor != null || zipping) return false;
        return HangingOn(p) == null;
    }

    // ── 대상 고르기 ─────────────────────────────

    // 대상 하나가 지금 붙기(또는 호출) 대상이 될 수 있는지(살아 있음·쓸 수 있음·매달린 크레인 아님·반경 안·시야). fresh면 시야를 새로 묻는다.
    private bool Valid(PlayerController p, IZipAnchor anchor, bool fresh)
    {
        if (anchor == null || !anchors.Contains(anchor) || !anchor.ZipAvailable || anchor.HasRider(p)) return false;
        Rect body = FacilityAccess.BodyRect(p);
        Vector2 goal = anchor.ZipFeet + Vector2.up * (body.height * .5f);
        float distance = Vector2.Distance(goal, body.center);
        if (distance > anchor.ZipRadius || distance < .05f) return false;
        return Sight(p, body, anchor, fresh);
    }

    // fresh = 시야를 기억 없이 새로 묻는다(붙기 확정·거절음 양보 판단). false면 표시용 — 기억을 재사용한다.
    // aim = (0, 0)이면 가장 가까움(직전에 떠난 대상은 1.5초 동안 × 1.5 — c6), 아니면 반각 100° 콘 안에서 앞쪽 가중 점수 최소(콘 밖 후보는 버린다 — 후보가 없으면 null).
    // 점수가 TieEpsilon 안으로 같으면 매달림 발이 높은 대상(c6 — 같은 높이면 먼저 등록된 대상).
    private IZipAnchor Pick(PlayerController p, bool fresh, Vector2 aim)
    {
        if (anchors.Count == 0) return null;
        Rect body = FacilityAccess.BodyRect(p);
        Vector2 center = body.center;
        float halfHeight = body.height * .5f;
        bool directed = aim.sqrMagnitude > .01f;
        Vector2 direction = directed ? aim.normalized : Vector2.zero;
        var left = !directed ? RecentlyLeft : null;
        IZipAnchor best = null;
        float bestScore = float.MaxValue;
        for (int i = 0; i < anchors.Count; i++)
        {
            var anchor = anchors[i];
            if (anchor == null || !anchor.ZipAvailable || anchor.HasRider(p)) continue;
            Vector2 goal = anchor.ZipFeet + Vector2.up * halfHeight;
            Vector2 delta = goal - center;
            float distance = delta.magnitude;
            if (distance > anchor.ZipRadius || distance < .05f) continue;
            float score = distance;
            if (directed)
            {
                float dot = Vector2.Dot(delta / distance, direction);
                if (dot < ConeCos) continue;
                score = distance * (1f + ForwardWeight * (1f - dot));
            }
            else if (ReferenceEquals(anchor, left)) score *= 1f + LeftPenalty;
            // 동점(TieEpsilon 안)은 높은 대상 우선 — 시야 질의 전에 거른다.
            if (score > bestScore + TieEpsilon) continue;
            if (best != null && score > bestScore - TieEpsilon && anchor.ZipFeet.y <= best.ZipFeet.y) continue;
            if (!Sight(p, body, anchor, fresh)) continue;
            best = anchor;
            bestScore = score;
        }
        return best;
    }

    // 후보 하나의 시야. 기억이 SightReuseTime 안이고 Player 몸 중심 칸·대상 매달림 발 칸이 그대로면 재사용하고, 아니면 모터에 묻고 기억한다.
    private bool Sight(PlayerController p, Rect body, IZipAnchor anchor, bool fresh)
    {
        Vector2 goal = anchor.ZipFeet;
        var playerCell = new Vector2Int(Mathf.FloorToInt(body.center.x), Mathf.FloorToInt(body.center.y));
        var anchorCell = new Vector2Int(Mathf.FloorToInt(goal.x), Mathf.FloorToInt(goal.y));
        if (!fresh && sightMemo.TryGetValue(anchor, out var memo) && Time.time - memo.At < SightReuseTime
            && memo.PlayerCell == playerCell && memo.AnchorCell == anchorCell)
        {
            SightReuses++;
            return memo.Clear;
        }
        bool clear = SightClear(p, body, goal - new Vector2(body.center.x, body.yMin));
        SightQueries++;
        sightMemo[anchor] = new SightMemo { At = Time.time, PlayerCell = playerCell, AnchorCell = anchorCell, Clear = clear };
        return clear;
    }

    /// <summary>
    /// 몸을 feetDelta만큼 직선으로 옮길 때 지형에 막히는지(모터 공개 질의만). 옆으로 띄운 광선 3줄 + 도착 자리 겹침.
    /// </summary>
    public static bool SightClear(PlayerController p, Rect body, Vector2 feetDelta)
    {
        var motor = p != null ? p.Motor : null;
        if (motor == null) return false;
        float distance = feetDelta.magnitude;
        if (distance < 1e-4f) return !motor.IsBodyBlockedAt(Vector2.zero);
        Vector2 dir = feetDelta / distance;
        Vector2 side = new Vector2(-dir.y, dir.x);
        float half = (Mathf.Abs(side.x) * body.width + Mathf.Abs(side.y) * body.height) * .5f * SightInset;
        for (int k = -1; k <= 1; k++)
        {
            Vector2 origin = body.center + side * (half * k);
            if (motor.RaycastEnvironment(origin, dir, distance).collider != null) return false;
        }
        return !motor.IsBodyBlockedAt(feetDelta);
    }

    // ── 실행 ─────────────────────────────

    private void Begin(IZipAnchor anchor)
    {
        if (zipping || anchor == null || player == null) return;
        zipping = true;
        target = anchor;
        LastTarget = anchor;
        Zips++;
        var started = StartCoroutine(ZipRoutine(player, anchor));
        // 첫 yield 전에 끝났으면(구속 실패) 이미 정리됐다 — 끝난 코루틴을 들고 있지 않는다.
        if (zipping) routine = started;
        if (started == null) Cleanup(false);
    }

    private IEnumerator ZipRoutine(PlayerController p, IZipAnchor anchor)
    {
        bool arrived = false;
        try
        {
            var abilities = p.AbilityManager;
            if (abilities != null)
            {
                if (abilities.InteractAbility != null) abilities.InteractAbility.Interrupt();
                if (abilities.DashAbility != null) { abilities.DashAbility.Interrupt(); abilities.DashAbility.ClearPendingInput(); }
                if (abilities.JumpAbility != null) abilities.JumpAbility.ClearPendingInput();
            }
            startFeet = FacilityAccess.Feet(p);
            var previous = HangingOn(p);
            guardAnchor = null;   // 도착 보호 중 다시 붙으면 그 보유자를 이번 붙기가 그대로 쓴다
            if (hold == null && !TryGetComponent(out hold)) hold = gameObject.AddComponent<WavePlayerStart>();
            hold.HoldForPassage(true);
            if (!hold.IsHoldingPlayer || p.Motor == null || !p.Motor.IsConfined) yield break;
            if (previous != null && !ReferenceEquals(previous, anchor)) previous.ReleaseForZip(p);
            busyHost = DungeonFacilityHost.Current;
            busyHost?.SetFacilityBusy(this, true);
            p.Velocity = Vector2.zero;
            p.externalForce = Vector2.zero;
            anchor.BeginZipIn(p);

            var tick = new WaitForFixedUpdate();
            Vector2 from = FacilityAccess.Feet(p), last = from;
            float duration = Mathf.Clamp(Vector2.Distance(from, anchor.ZipFeet) / Speed, MinTime, MaxTime);
            LastDuration = duration;
            for (float time = 0f; time < duration;)
            {
                yield return tick;
                if (Aborted(p, anchor)) yield break;
                time = Mathf.Min(duration, time + Time.fixedDeltaTime);
                Vector2 point = Vector2.Lerp(from, anchor.ZipFeet, time / duration);
                p.Motor.ShiftConfinement(point - last);
                last = point;
            }
            // 마지막으로 넘긴 이동량이 반영된 뒤 남은 차이(대상이 조금 움직였을 때)를 한 번 더 맞춘다.
            yield return tick;
            if (Aborted(p, anchor)) yield break;
            Vector2 residual = anchor.ZipFeet - FacilityAccess.Feet(p);
            if (residual.sqrMagnitude > 1e-4f)
            {
                p.Motor.ShiftConfinement(residual);
                yield return tick;
                if (Aborted(p, anchor)) yield break;
            }
            if (!anchor.AcceptZip(p)) yield break;
            arrived = true;
            Arrivals++;
            anchor.EndZipIn(true);
            guardAnchor = anchor;
            guardUntil = Time.time + ArrivalGuard;
            regrabAt = Time.time + RegrabDelay;
        }
        finally
        {
            routine = null;
            Cleanup(arrived);
        }
    }

    // 붙는 동안 끊어야 하는지(대상 사라짐·사망·Wave 정지·구속이 다른 경로로 풀림·Player 꺼짐).
    private bool Aborted(PlayerController p, IZipAnchor anchor)
    {
        if (p == null || !p.isActiveAndEnabled || p != player) return true;
        if (p.Health != null && p.Health.IsDead) return true;
        if (!anchors.Contains(anchor)) return true;
        if (hold == null || !hold.IsHoldingPlayer || p.Motor == null || !p.Motor.IsConfined) return true;
        return FacilityAccess.WaveHalted;
    }

    // 붙기 상태 정리(여러 번 불러도 한 번만). arrived가 아니면 와이어를 거두고, 몸이 지형과 겹치면 출발 자리로 옮긴 뒤 보유자를 놓는다.
    // StopCoroutine·개체 꺼짐은 finally를 돌리지 않으므로 OnDisable에서도 부른다.
    private void Cleanup(bool arrived)
    {
        if (routine != null) { StopCoroutine(routine); routine = null; }
        var anchor = target;
        target = null;
        bool wasZipping = zipping;
        zipping = false;
        try
        {
            if (wasZipping && !arrived)
            {
                Failures++;
                if (anchor != null && anchors.Contains(anchor)) anchor.EndZipIn(false);
                var p = player;
                if (p != null && p.Motor != null && p.Motor.IsConfined && HangingOn(p) == null && p.Motor.IsBodyBlockedAt(Vector2.zero))
                    FacilityRide.RelocateFeet(p, startFeet);
            }
        }
        catch (System.Exception e) { Debug.LogException(e, this); }
        finally
        {
            try
            {
                if (!arrived)
                {
                    guardAnchor = null;
                    if (hold != null && hold.IsHoldingPlayer) hold.Release();
                }
            }
            finally
            {
                var host = busyHost;
                busyHost = null;
                host?.SetFacilityBusy(this, false);
            }
        }
    }
}
