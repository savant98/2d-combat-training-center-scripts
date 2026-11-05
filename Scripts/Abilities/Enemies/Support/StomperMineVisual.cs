using UnityEngine;

// Pool 반환 때 분리되는 Trail의 표시 상태만 복구한다.
// Animator·LandMine의 상태와 시간·발사 위치·이동 속도는 변경하지 않는다.
[DefaultExecutionOrder(200)]
[DisallowMultipleComponent]
public sealed class StomperMineVisual : MonoBehaviour
{
    [SerializeField] private Animator animator;
    [SerializeField] private TrailRenderer trail;
    [SerializeField] private SpriteRenderer sourceRange;
    [SerializeField] private SpriteRenderer warningOverlay;

    static readonly int TriggeredState = Animator.StringToHash("LandMine_Triggered");
    static readonly int FlyState = Animator.StringToHash("LandMine_Fly");
    bool pendingSpawnReset;
    bool wasFlying;

    public TrailRenderer Trail => trail;
    public SpriteRenderer SourceRange => sourceRange;
    public SpriteRenderer WarningOverlay => warningOverlay;
    public bool IsTriggeredVisual { get; private set; }
    public bool IsFlyingVisual { get; private set; }
    public int ActivationCount { get; private set; }
    public int TrailResetCount { get; private set; }

    public void Configure(Animator animator, TrailRenderer trail)
    {
        this.animator = animator;
        this.trail = trail;
    }

    public void Configure(Animator animator, TrailRenderer trail, SpriteRenderer sourceRange, SpriteRenderer warningOverlay)
    {
        Configure(animator, trail);
        this.sourceRange = sourceRange;
        this.warningOverlay = warningOverlay;
    }

    void Awake() => ResolveReferences();

    void OnEnable()
    {
        // 재대여 시 아직 원래 자식이 아니어도 직렬화된 참조를 버리거나 다시 찾지 않는다.
        ResolveReferences();
        pendingSpawnReset = true;
        wasFlying = false;
        IsFlyingVisual = false;
        ActivationCount++;
        if (trail != null) trail.emitting = false;
        UpdateWarningOverlay(false);
    }

    void LateUpdate()
    {
        if (animator == null || trail == null) return;
        // ProjectileBase.OnSpawnedFromPool이 재부착하고 Initialize가 활성화한 다음에만 처리한다.
        // Spawn만 하고 Initialize하지 않은 대기 개체의 Trail을 임의로 켜지 않는다.
        bool ready = trail.transform.IsChildOf(transform) && trail.gameObject.activeInHierarchy
            && animator.isActiveAndEnabled && animator.isInitialized;
        if (!ready)
        {
            IsFlyingVisual = false;
            wasFlying = false;
            trail.emitting = false;
            UpdateWarningOverlay(false);
            return;
        }

        var state = animator.IsInTransition(0) ? animator.GetNextAnimatorStateInfo(0) : animator.GetCurrentAnimatorStateInfo(0);
        bool flying = state.shortNameHash == FlyState;
        UpdateWarningOverlay(state.shortNameHash == TriggeredState);
        if (pendingSpawnReset || (flying && !wasFlying))
        {
            // 이전 위치의 선분을 잇지 않도록 복구된 부모·발사 위치에서 한 번 비운다.
            trail.Clear();
            pendingSpawnReset = false;
            TrailResetCount++;
        }
        trail.enabled = true;
        // Animator 평가 이후에 적용해, 재활성화 시 누락된 Trail 경로 바인딩과 이전 false값을 보완한다.
        trail.emitting = flying;
        IsFlyingVisual = flying;
        wasFlying = flying;
    }

    void OnDisable()
    {
        IsFlyingVisual = false;
        wasFlying = false;
        pendingSpawnReset = true;
        UpdateWarningOverlay(false);
        // ProjectileBase가 분리한 잔상은 기존 time 동안 자연히 사라지게 둔다.
        if (trail != null) trail.emitting = false;
    }

    void OnDestroy()
    {
        // 개체 자체가 제거될 때만 분리된 표시 객체도 정리한다. Pool 반환에서는 제거하지 않는다.
        if (Application.isPlaying && trail != null && !trail.transform.IsChildOf(transform)) Destroy(trail.gameObject);
    }

    void UpdateWarningOverlay(bool triggered)
    {
        IsTriggeredVisual = triggered;
        if (warningOverlay == null) return;
        // 원본 경고 원은 그대로 두고 새 무늬만 같은 색·알파로 겹친다. Transform/Animator는 변경하지 않는다.
        warningOverlay.enabled = triggered && sourceRange != null && sourceRange.enabled;
        if (sourceRange != null) warningOverlay.color = sourceRange.color;
    }

    void ResolveReferences()
    {
        if (animator == null) animator = GetComponent<Animator>();
        if (trail == null) trail = GetComponentInChildren<TrailRenderer>(true);
        if (sourceRange == null)
        {
            var range = transform.Find("DetectinModule");
            if (range != null) sourceRange = range.GetComponent<SpriteRenderer>();
        }
        if (warningOverlay == null && sourceRange != null)
        {
            var overlay = sourceRange.transform.Find("WarningOverlay");
            if (overlay != null) warningOverlay = overlay.GetComponent<SpriteRenderer>();
        }
    }
}
