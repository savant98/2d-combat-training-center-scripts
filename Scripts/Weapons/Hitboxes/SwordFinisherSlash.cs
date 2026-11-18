using System;
using System.Collections.Generic;
using UnityEngine;

// 일격필살(SwordFinisherWeaponSO)의 거대한 일섬 — 화면을 15도로 가르는 선·화면 가르기·참격과 화면 전체 판정(2026-11-18).
// 사용자 지시(1차): Sword 무장 Gear 3단계 '일격필살'을 "개발된 무장을 참고해서" 만든다. 돌진 베기(SwordDashSlash, 일섬)와 같은 미감.
// 2차(같은 날): "화면을 반으로 가르는 연출(normal map이나 카메라 활용)을 하고, 화면 전체에 데미지를 주도록 해줘. 화면을 반으로 가름,
//   둘로 쪼개지는게 보임. 그 후 자연스럽게 참격이 보임. 화면을 15도 대각선으로 자르면 됨."
// - 시계: 연출은 실제 시간(Time.unscaledDeltaTime)으로 잰다. 갈라지는 동안 히트스톱으로 세계가 거의 멈추므로 게임 시간으로 재면 갈라짐이
//   보이지 않는다. 일시정지(GameTimeController.IsPaused·시간 배율 0)에는 멈춘다. 꺼낸 프레임에는 시계를 더하지 않는다(그 프레임의 앞 시간이다).
// - 흐름(시계 = 꺼낸 순간 = 발도부터):
//   1) 발도(0 ~ drawTime): 보는 방향으로 올라가는 lineAngle(15도) 선의 머리가 화면 뒤쪽 끝에서 앞쪽 끝까지 단숨에 그어진다.
//      선은 Player의 칼 높이(이 개체의 자리)를 지나고, 길이는 메인 카메라 화면을 lineOverscan만큼 넘는다(벽에서 멈추지 않는다).
//   2) 화면 가르기(drawTime ~ cutDelay): 다 그어지면 갈라짐 Feedback(거의 멈춘 시간 = 히트스톱)을 한 번 재생하고 ScreenSplitRenderer로
//      화면을 그 선을 따라 가른다. splitOpenTime 동안 벌어지고(ease-out), 머물다가, 베는 순간 전 splitCloseTime 동안 다시 붙는다(ease-in).
//      베기 직전 sheatheLead에 납도 소리.
//   3) 베는 순간(cutDelay): 화면 가르기를 끝내고, 선이 굵고 밝게 번쩍이며 칼자국이 그어지고(셰이더), 충격 Feedback을 한 번 재생하고,
//      메인 카메라 화면 사각형(+ screenMargin) 안의 모든 적이 한꺼번에 한 번 맞는다. 벽 뒤도 맞고 화면 밖은 맞지 않는다.
//   4) 선·칼자국이 옅어지다 베는 순간에서 lifeAfterCut 뒤에 풀로 돌아간다.
// - 판정(규칙: RaycastMotor2D 기반): 베는 순간 한 번 Player 모터의 OverlapAttackBox로 화면 사각형을 묻는다(InvalidateAttackQuerySync로
//   LateUpdate에서도 최신 위치를 보게 한다). Player의 위치는 읽기만 하고 옮기지 않는다. 피해·쉴드·넉백·적중 Feedback은 SwordDashSlash.Strike와 같은 길,
//   막힌 적(무적·등장 잠금)은 건너뛴다. 게이지 보상(onHitEnemy)은 무기가 정한 적 수(rewardedHits)까지만 낸다.
// - 끊김: 발도가 끝나기 전에 Player의 공격이 끊기면(피격·잡기·Player가 사라짐) 가르지도 베지도 않고 바로 사라진다.
//   발도가 끝난 뒤에는 이미 그은 것이므로 끊겨도 그대로 갈라지고 베인다(돌진 베기와 같다).
// - 해제 보장: 풀 반환·끊김·OnDisable에서 반드시 ScreenSplitRenderer.End를 불러 화면이 갈린 채 남지 않게 한다.
// - 모양은 Player/SwordDashSlash 셰이더(전용 재질 PlayerSwordFinisherSlash)가 사각형 하나에 그리고, 이 스크립트는 값만 MaterialPropertyBlock으로 넘긴다.
//   _Head = 그어진 데까지, _Age = 베는 순간부터의 시간(음수 = 발도·가르기), _Clock = 꺼낸 뒤 흐른 시간, _CutClock = cutDelay, _Span·_FullSpan = 선 길이.
public class SwordFinisherSlash : WorldEntityBase
{
    private static readonly int RectId = Shader.PropertyToID("_Rect");
    private static readonly int SpanId = Shader.PropertyToID("_Span");
    private static readonly int FullSpanId = Shader.PropertyToID("_FullSpan");
    private static readonly int HeadId = Shader.PropertyToID("_Head");
    private static readonly int AgeId = Shader.PropertyToID("_Age");
    private static readonly int ClockId = Shader.PropertyToID("_Clock");
    private static readonly int CutClockId = Shader.PropertyToID("_CutClock");

    [Header("Trail")]
    [SerializeField] private SpriteRenderer trail;
    [Tooltip("그리는 사각형이 시작점 뒤·끝점 앞으로 더 나가는 거리와 위아래 반높이(칸). 비스듬한 칼자국·굵어진 선의 번짐이 잘리지 않을 만큼 둔다.")]
    [SerializeField, Min(0f)] private float padBack = 2f;
    [SerializeField, Min(0f)] private float padFront = 2.5f;
    [SerializeField, Min(0.1f)] private float halfHeight = 2.4f;
    [Tooltip("발도: 선의 머리가 화면 끝에서 끝까지 뻗는 시간(초, 실제 시간).")]
    [SerializeField, Min(0.01f)] private float drawTime = 0.05f;
    [Tooltip("베는 순간 뒤 사라질 때까지(초, 실제 시간). 셰이더의 궤적이 사라지는 시각·마지막 칼자국이 사라지는 시각보다 길어야 한다.")]
    [SerializeField, Min(0.1f)] private float lifeAfterCut = 1.0f;

    [Header("Line")]
    [Tooltip("가르는 선의 기울기(도). 보는 방향으로 올라간다.")]
    [SerializeField, Range(0f, 60f)] private float lineAngle = 15f;
    [Tooltip("선이 화면 가장자리 밖으로 더 나가는 거리(칸). 선 양 끝이 가늘어지는 구간이 화면 밖에 있게 한다.")]
    [SerializeField, Min(0f)] private float lineOverscan = 1.5f;
    [Tooltip("메인 카메라가 없을 때 쓰는 대체 화면의 반폭·반높이(칸, 주인 중심).")]
    [SerializeField] private Vector2 fallbackHalfSize = new Vector2(14f, 8f);

    [Header("Split")]
    [Tooltip("화면 가르기 전체 화면 재질(Hidden/2DCTC/ScreenSplit). 여기서 참조해 빌드에 셰이더가 포함된다.")]
    [SerializeField] private Material splitMaterial;
    [Tooltip("갈라짐이 다 벌어지는 시간(초, 발도가 끝난 뒤부터, 실제 시간).")]
    [SerializeField, Min(0.01f)] private float splitOpenTime = 0.15f;
    [Tooltip("베는 순간 전 다시 붙는 시간(초, 실제 시간).")]
    [SerializeField, Min(0.01f)] private float splitCloseTime = 0.06f;
    [Tooltip("다 벌어졌을 때 틈의 반폭·두 조각이 선 방향으로 어긋난 거리·자른 면이 들쭉날쭉한 정도(화면 높이 비율).")]
    [SerializeField, Min(0f)] private float splitGap = 0.012f;
    [SerializeField, Min(0f)] private float splitSlide = 0.045f;
    [SerializeField, Min(0f)] private float splitJag = 0.0025f;

    [Header("Cut")]
    [Tooltip("발도 → 베는 순간(초, 실제 시간). 화면이 갈라졌다 다시 붙는 끝이다.")]
    [SerializeField, Min(0.05f)] private float cutDelay = 0.48f;
    [Tooltip("납도 소리(칼집에 칼이 들어가는 찰칵)를 베는 순간보다 이만큼 먼저 낸다(초, 실제 시간).")]
    [SerializeField, Min(0f)] private float sheatheLead = 0.03f;

    [Header("Hit")]
    [Tooltip("판정: 베는 순간 메인 카메라 화면 사각형의 네 변 밖으로 더 닿는 거리(칸). 화면에 걸친 적도 맞는다.")]
    [SerializeField, Min(0f)] private float screenMargin = 0.5f;

    private struct Target
    {
        public EnemyHealth health;
        public Collider2D body;
    }

    private readonly List<Target> targets = new();
    private readonly HashSet<HealthBase> seen = new();
    private readonly Collider2D[] overlapResults = new Collider2D[256];
    private MaterialPropertyBlock block;
    private FeedbackDataSO hitFeedback, splitFeedback, impactFeedback, sheatheFeedback;
    private Action<CharacterController2D, Vector2> onHitEnemy;
    private LayerMask damageMask;
    private Vector4 rect;
    private Vector2 lineDirection = Vector2.right;
    private Rect hitRect;
    private int facing = 1, beginFrame, rewardedHits = int.MaxValue, rewarded, struck;
    private float lineStart, length, head, clock;
    private bool running, drawn, splitStarted, splitEnded, sheathed, cut, cancelled;

    public bool IsRunning => running && !isReturning;
    public bool IsDrawn => drawn;
    public bool IsCut => cut;
    public bool WasCancelled => cancelled;
    public bool IsSplitting => splitStarted && !splitEnded;

    // 갈라짐 히트스톱은 실제 시간으로 끝나므로(GameTimeController), 갈라진 동안 일시정지했다가 돌아오면 이미 끝나 있다.
    // 연출 시계는 일시정지 동안 멈추므로, 남은 갈라짐 동안 세계가 정상 속도로 흐르지 않게 남은 시간만큼 다시 건다(2026-11-18 독립 검토).
    private void KeepSplitHitStop()
    {
        var time = GameTimeController.Instance;
        if (splitFeedback == null || !splitFeedback.useHitStop || time == null || time.IsPaused || Time.timeScale <= 0f || time.IsHitStopActive) return;
        float remaining = cutDelay - clock;
        if (remaining > 0.01f) time.PlayHitStop(splitFeedback.hitStopScale, remaining);
    }
    public int Facing => facing;
    public float LineAngle => lineAngle;
    public Vector2 LineDirection => lineDirection;    // 선 방향(월드, 길이 1)
    public float LineStart => lineStart;              // 이 개체 자리에서 선 시작점까지(칸, 선 방향 기준, 보통 음수 = 뒤쪽 화면 끝)
    public float Length => length;                    // 선 길이(칸)
    public float Head => head;                        // 선이 그어진 데까지(칸, 시작점 기준)
    public float Clock => clock;                      // 꺼낸 뒤 흐른 실제 시간(초, 일시정지에서 멈춤). 풀로 돌아간 뒤에도 마지막 값을 남긴다
    public float Age => running ? clock : -1f;
    public float DrawTime => drawTime;
    public float SplitOpenEnd => drawTime + splitOpenTime;     // 다 벌어지는 시각
    public float SplitCloseStart => cutDelay - splitCloseTime; // 다시 붙기 시작하는 시각
    public float SplitGap => splitGap;
    public float SplitSlide => splitSlide;
    public Material SplitMaterial => splitMaterial;
    public float CutDelay => cutDelay;
    public float SheatheLead => sheatheLead;
    public float LifeAfterCut => lifeAfterCut;
    public float ScreenMargin => screenMargin;
    public bool Sheathed => sheathed;
    public Rect HitRect => hitRect;                   // 베는 순간 물은 판정 사각형(월드)
    public int TargetCount => targets.Count;          // 판정에 걸린 적 수
    public int StruckCount => struck;                 // 실제로 벤 적 수(막힌 적·쓰러진 적 제외)
    public int RewardedCount => rewarded;             // 게이지 보상을 낸 적 수
    public int RewardedHitLimit => rewardedHits;

    protected override void Awake()
    {
        base.Awake();
        block = new MaterialPropertyBlock();   // MonoBehaviour 필드 초기화 중에는 만들 수 없다
        if (trail == null) trail = GetComponentInChildren<SpriteRenderer>(true);
    }

    public override void OnSpawnedFromPool()
    {
        base.OnSpawnedFromPool();
        ResetState();
    }

    private void ResetState()
    {
        EndSplit();
        targets.Clear();
        seen.Clear();
        hitFeedback = splitFeedback = impactFeedback = sheatheFeedback = null;
        onHitEnemy = null;
        running = drawn = splitStarted = splitEnded = sheathed = cut = cancelled = false;
        lineStart = length = head = clock = 0f;
        rewardedHits = int.MaxValue;
        rewarded = struck = 0;
        hitRect = default;
        if (trail != null) trail.enabled = false;
    }

    // hitFeedback = 베인 적마다, splitFeedback = 화면이 갈라지기 시작할 때 한 번, impactFeedback = 베는 순간 한 번,
    // sheatheFeedback = 베기 직전의 납도 소리, rewardedHits = 게이지 보상(onHitEnemy)을 낼 적의 수
    public void SetHit(FeedbackDataSO hitFeedback, FeedbackDataSO splitFeedback, FeedbackDataSO impactFeedback, FeedbackDataSO sheatheFeedback,
        int rewardedHits, Action<CharacterController2D, Vector2> onHitEnemy)
    {
        this.hitFeedback = hitFeedback;
        this.splitFeedback = splitFeedback;
        this.impactFeedback = impactFeedback;
        this.sheatheFeedback = sheatheFeedback;
        this.rewardedHits = Mathf.Max(0, rewardedHits);
        this.onHitEnemy = onHitEnemy;
    }

    // 발도하는 순간. 이 개체는 선이 지나는 자리(칼 높이)에 놓여 있다(Initialize 뒤에 부른다).
    // 보는 방향으로 올라가는 선이 메인 카메라 화면을 끝에서 끝까지 지나도록 시작점·길이를 정한다.
    public void Begin(int facing, LayerMask damageMask)
    {
        this.facing = facing >= 0 ? 1 : -1;
        this.damageMask = damageMask;
        float radians = lineAngle * Mathf.Deg2Rad;
        lineDirection = new Vector2(this.facing * Mathf.Cos(radians), Mathf.Sin(radians));
        // 왼쪽을 보면 그림을 좌우로 뒤집고 반대로 돌린다: 로컬 +x가 lineDirection과 같아진다(왼쪽 위).
        transform.rotation = Quaternion.Euler(0f, 0f, this.facing * lineAngle);
        transform.localScale = new Vector3(this.facing, 1f, 1f);

        Vector2 origin = transform.position;
        Rect view = ScreenRect(lineOverscan);
        if (ClipLine(origin, lineDirection, view, out float near, out float far))
        {
            lineStart = near;
            length = Mathf.Max(0.1f, far - near);
        }
        else
        {
            float half = fallbackHalfSize.x / Mathf.Max(0.1f, Mathf.Cos(radians)) + lineOverscan;
            lineStart = -half;
            length = half * 2f;
        }

        head = 0f;
        clock = 0f;
        beginFrame = Time.frameCount;
        running = true;
        drawn = splitStarted = splitEnded = sheathed = cut = cancelled = false;

        // 셰이더의 x(0 = 시작점)는 로컬 x - lineStart다. 사각형의 왼쪽 끝을 시작점 뒤 padBack에 둔다.
        float width = length + padBack + padFront;
        rect = new Vector4(-padBack, -halfHeight, width, halfHeight * 2f);
        if (trail != null && trail.sprite != null)
        {
            Vector2 size = trail.sprite.bounds.size;
            trail.transform.localPosition = new Vector3(lineStart - padBack + width * 0.5f, 0f, 0f);
            trail.transform.localRotation = Quaternion.identity;
            trail.transform.localScale = new Vector3(width / size.x, halfHeight * 2f / size.y, 1f);
            trail.enabled = true;
        }
        Apply();
    }

    // Player의 공격이 아직 이어지는지(PlayerLaserBeam.IsAttackAlive와 같은 기준). Player가 아닌 주인은 끊김을 보지 않는다.
    private bool IsAttackAlive()
    {
        if (owner == null || !owner.gameObject.activeInHierarchy) return false;
        if (owner is not PlayerController player) return true;
        var abilities = player.AbilityManager;
        if (abilities == null || abilities.AttackAbility == null || abilities.AttackAbility.AttackPhase == AbilityState.None) return false;
        return abilities.StateMachine.CurrentState == PlayerState.Attack;
    }

    // 일시정지(메뉴·시간 배율 0)에는 멈추고, 히트스톱 동안에도 실제 시간으로 흐른다.
    private static float RealDelta()
    {
        var time = GameTimeController.Instance;
        if ((time != null && time.IsPaused) || Time.timeScale <= 0f) return 0f;
        return Time.unscaledDeltaTime;
    }

    private void LateUpdate()
    {
        if (!running || isReturning) return;
        if (Time.frameCount != beginFrame) clock += RealDelta();

        if (!drawn)
        {
            if (!IsAttackAlive())
            {
                // 발도 도중 끊겼다. 가르지도 베지도 않고 사라진다.
                cancelled = true;
                running = false;
                ForceReturnToPool();
                return;
            }
            float u = Mathf.Clamp01(clock / drawTime);
            head = length * (1f - (1f - u) * (1f - u) * (1f - u));   // 처음이 가장 빠르게 뻗는다
            if (u >= 1f)
            {
                head = length;
                drawn = true;
            }
        }

        // 화면 가르기: 다 그어진 순간 시작해 베는 순간에 끝난다.
        if (drawn && !splitStarted)
        {
            splitStarted = true;
            PlayFeedback(splitFeedback, OwnerPosition());
            if (clock < cutDelay && splitMaterial != null) ScreenSplitRenderer.Begin(this, splitMaterial, SplitState());
        }
        if (splitStarted && !splitEnded)
        {
            if (clock >= cutDelay) EndSplit();
            else
            {
                ScreenSplitRenderer.UpdateState(this, SplitState());
                KeepSplitHitStop();
            }
        }

        // 납도: 베는 순간보다 sheatheLead 먼저 칼집에 칼이 들어가는 소리를 한 번 낸다(화면 안에 적이 없어도).
        if (!sheathed && clock >= cutDelay - sheatheLead)
        {
            sheathed = true;
            PlayFeedback(sheatheFeedback, OwnerPosition());
        }
        if (!cut && clock >= cutDelay)
        {
            cut = true;
            head = length;
            EndSplit();
            Collect();
            PlayFeedback(impactFeedback, OwnerPosition());
            for (int i = 0; i < targets.Count; i++) Strike(targets[i]);
        }
        if (cut && clock >= cutDelay + lifeAfterCut)
        {
            running = false;
            ForceReturnToPool();
            return;
        }
        Apply();
    }

    // 지금의 갈라진 정도: 벌어짐(ease-out) → 머묾 → 다시 붙음(ease-in). 0 = 붙음, 1 = 다 벌어짐.
    private float SplitAmount()
    {
        float t = clock - drawTime;
        if (t <= 0f) return 0f;
        if (t < splitOpenTime)
        {
            float u = t / splitOpenTime;
            return 1f - (1f - u) * (1f - u) * (1f - u);
        }
        float closeStart = cutDelay - splitCloseTime;
        if (clock < closeStart) return 1f;
        float v = Mathf.Clamp01((clock - closeStart) / splitCloseTime);
        return 1f - v * v;
    }

    private ScreenSplitState SplitState()
    {
        float k = SplitAmount();
        return new ScreenSplitState
        {
            point = transform.position,
            direction = lineDirection,
            gap = splitGap * k,
            slide = splitSlide * k,
            jag = splitJag * k,
            strength = k,
        };
    }

    private void EndSplit()
    {
        if (splitStarted) splitEnded = true;
        ScreenSplitRenderer.End(this);
    }

    // 메인 카메라가 보는 월드 사각형을 사방으로 margin만큼 넓힌 것. 카메라가 없으면 주인 둘레의 대체 상자.
    private Rect ScreenRect(float margin)
    {
        var cam = Camera.main;
        if (cam != null)
        {
            if (cam.orthographic)
            {
                float halfH = cam.orthographicSize, halfW = halfH * cam.aspect;
                Vector2 c = cam.transform.position;
                return new Rect(c.x - halfW - margin, c.y - halfH - margin, 2f * (halfW + margin), 2f * (halfH + margin));
            }
            float depth = Mathf.Abs(transform.position.z - cam.transform.position.z);
            Vector3 a = cam.ViewportToWorldPoint(new Vector3(0f, 0f, depth));
            Vector3 b = cam.ViewportToWorldPoint(new Vector3(1f, 1f, depth));
            return Rect.MinMaxRect(Mathf.Min(a.x, b.x) - margin, Mathf.Min(a.y, b.y) - margin, Mathf.Max(a.x, b.x) + margin, Mathf.Max(a.y, b.y) + margin);
        }
        Vector2 center = OwnerPosition();
        Vector2 half = fallbackHalfSize + Vector2.one * margin;
        return new Rect(center - half, half * 2f);
    }

    // 점 origin에서 방향 direction으로 뻗은 직선이 사각형 안에 있는 구간 [near, far](direction 기준 거리)
    private static bool ClipLine(Vector2 origin, Vector2 direction, Rect area, out float near, out float far)
    {
        near = float.NegativeInfinity;
        far = float.PositiveInfinity;
        for (int axis = 0; axis < 2; axis++)
        {
            float o = origin[axis], d = direction[axis];
            float min = axis == 0 ? area.xMin : area.yMin, max = axis == 0 ? area.xMax : area.yMax;
            if (Mathf.Abs(d) < 1e-6f)
            {
                if (o < min || o > max) return false;
                continue;
            }
            float t1 = (min - o) / d, t2 = (max - o) / d;
            if (t1 > t2) (t1, t2) = (t2, t1);
            near = Mathf.Max(near, t1);
            far = Mathf.Min(far, t2);
        }
        return far > near;
    }

    // 베는 순간 한 번: 화면 사각형 안의 적을 모은다(벽 뒤 포함, 한 적의 여러 Collider는 한 번만).
    private void Collect()
    {
        hitRect = ScreenRect(screenMargin);
        if (owner == null || owner.Motor == null) return;   // 물을 모터가 없다
        RaycastMotor2D.InvalidateAttackQuerySync();         // LateUpdate에서도 이번 프레임의 위치를 보게 한다
        int count = owner.Motor.OverlapAttackBox(hitRect.center, hitRect.size, 0f, overlapResults, damageMask);
        for (int i = 0; i < count; i++)
        {
            Collider2D hit = overlapResults[i];
            overlapResults[i] = null;
            if (hit == null) continue;
            if (hit.GetComponentInParent<HealthBase>() is not EnemyHealth enemy || !seen.Add(enemy)) continue;
            targets.Add(new Target { health = enemy, body = hit });
        }
    }

    private Vector3 OwnerPosition()
    {
        return owner != null && owner.gameObject.activeInHierarchy ? owner.transform.position : transform.position;
    }

    private static void PlayFeedback(FeedbackDataSO feedback, Vector3 position)
    {
        if (feedback != null && FeedbackPlayer.Instance != null) FeedbackPlayer.Instance.Play(feedback, position);
    }

    private void Strike(Target target)
    {
        EnemyHealth enemy = target.health;
        if (enemy == null || !enemy.isActiveAndEnabled || enemy.currentHP <= 0f) return;   // 그사이 쓰러졌다
        // 피해가 막힌 적(무적·등장 잠금)은 피해·게이지 보상·적중 효과를 모두 건너뛴다. 무적 적으로 게이지를 되찾지 않게 한다.
        if (enemy.IsDamageBlocked || (enemy.enemyShieldSystem != null && enemy.enemyShieldSystem.IsSpawnLocked)) return;
        Vector2 point = HitPoint(target);
        if (enemy.enemyShieldSystem == null) enemy.TakeDamage(damageInfo, point);
        else enemy.enemyShieldSystem.TakeDamage(damageInfo, point);
        struck++;
        // 게이지 보상은 한 번 쓸 때 rewardedHits명까지만(적 수만큼 쌓여 비용이 다 돌아오지 않게)
        if (rewarded < rewardedHits)
        {
            rewarded++;
            onHitEnemy?.Invoke(owner, point);
        }
        PlayFeedback(hitFeedback, point);
    }

    // 맞은 자리: 적의 몸 가운데를 판정 사각형(화면) 안으로 당긴 점(화면에 걸친 큰 적도 화면 안에서 이펙트가 난다)
    private Vector2 HitPoint(Target target)
    {
        Vector2 center = target.body != null && target.body.gameObject.activeInHierarchy
            ? (Vector2)target.body.bounds.center : (Vector2)target.health.transform.position;
        return new Vector2(Mathf.Clamp(center.x, hitRect.xMin, hitRect.xMax), Mathf.Clamp(center.y, hitRect.yMin, hitRect.yMax));
    }

    private void Apply()
    {
        if (trail == null) return;
        float age = running ? clock : 0f;
        trail.GetPropertyBlock(block);
        block.SetVector(RectId, rect);
        block.SetFloat(SpanId, Mathf.Max(length, 0.01f));
        block.SetFloat(FullSpanId, Mathf.Max(length, 0.01f));   // 칼자국을 선 전체에 고르게 나눈다
        block.SetFloat(HeadId, head);
        block.SetFloat(AgeId, age - cutDelay);   // 베는 순간부터의 시간(음수 = 발도·가르기)
        block.SetFloat(ClockId, age);
        block.SetFloat(CutClockId, cutDelay);    // 베는 순간의 _Clock
        trail.SetPropertyBlock(block);
    }

    public override void ForceReturnToPool()
    {
        EndSplit();
        base.ForceReturnToPool();
    }

    private void OnDisable()
    {
        running = false;
        EndSplit();
        targets.Clear();
        seen.Clear();
    }
}
