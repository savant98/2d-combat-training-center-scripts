using UnityEngine;
using UnityEngine.XR;

public abstract class AiAbility : MonoBehaviour
{
    protected AiBrain brain;
    protected EnemyController controller;
    protected FeedbackPlayer feedbackPlayer;
    protected Animator animator;

    [Header("Pattern")]
    [SerializeField] private bool allowRandomSelection = true;
    // 기존 Prefab 직렬화 이름은 유지하며 이제 일반 가중치 선택의 참가 여부를 뜻한다.
    public bool AllowRandomSelection => allowRandomSelection;
    public bool AllowWeightedSelection => allowRandomSelection;
    public EnemyAbilityDataSO Data => data;
    public string AbilityKey => data != null ? data.abilityKey : string.Empty;

    [Header("EnemyAbilityData")]
    [SerializeField] protected EnemyAbilityDataSO data;

    [Header("Feedback Points")]
    [SerializeField] protected Transform windupPoint;
    [SerializeField] protected Transform executePoint;
    [SerializeField] protected Transform recoverPoint;

    private int abilityAnimHash;
    // 기존 검증 도구의 필드 연결을 보존한다. 값은 이제 마지막 종료 시각이다.
    private float lastUseTime = float.NegativeInfinity;
    private float storedWeight;
    private float weightTimestamp;
    public long LastSelectionOrder { get; private set; }
    public float CurrentWeight => storedWeight + (data != null && !IsRunning
        ? Mathf.Max(0f, WeightRecoveryPerSecond) * Mathf.Max(0f, Time.time - weightTimestamp) : 0f);
    public float CooldownRemaining => data == null ? 0f
        : Mathf.Max(0f, lastUseTime + Mathf.Max(0f, Cooldown) - Time.time);

    // 페이즈별 선택 수치. 적용 중에는 SO의 공통 수치 대신 쓴다. 런타임 값이며 SO 원본은 바꾸지 않는다.
    private bool hasSelectionTuning;
    private float tunedBaseWeight, tunedWeightChangeOnUse, tunedWeightRecovery, tunedCooldown;
    public bool HasSelectionTuning => hasSelectionTuning;
    public float BaseWeight => hasSelectionTuning ? tunedBaseWeight : data != null ? data.baseWeight : 0f;
    public float WeightChangeOnUse => hasSelectionTuning ? tunedWeightChangeOnUse : data != null ? data.weightChangeOnUse : 0f;
    public float WeightRecoveryPerSecond => hasSelectionTuning ? tunedWeightRecovery : data != null ? data.weightRecoveryPerSecond : 0f;
    public float Cooldown => hasSelectionTuning ? tunedCooldown : data != null ? data.cooldown : 0f;

    /// <summary>페이즈에 들어올 때 그 페이즈의 선택 수치를 적용하고 점수를 새 기본값에서 시작한다. 지난 종료 시각은 유지해 쿨타임이 이어진다.</summary>
    public void ApplySelectionTuning(float baseWeight, float weightChangeOnUse, float weightRecoveryPerSecond, float cooldown)
    {
        hasSelectionTuning = true;
        tunedBaseWeight = baseWeight;
        tunedWeightChangeOnUse = weightChangeOnUse;
        tunedWeightRecovery = weightRecoveryPerSecond;
        tunedCooldown = cooldown;
        storedWeight = baseWeight;
        weightTimestamp = Time.time;
    }

    /// <summary>현재 페이즈에 등록하지 않은 패턴은 SO의 공통 수치로 돌아간다.</summary>
    public void ClearSelectionTuning()
    {
        if (!hasSelectionTuning) return;
        hasSelectionTuning = false;
        storedWeight = BaseWeight;
        weightTimestamp = Time.time;
    }
    private float stateStartTime;

    public AbilityState State { get; private set; } = AbilityState.None;
    public bool IsRunning => State != AbilityState.None;

    // 순찰처럼 시간으로 끝나지 않는 패턴은 true를 반환해 실행 중에도 AiPatternProfileSO의 전환 판단을 받는다.
    public virtual bool CanYieldToPattern => false;

    public void Initialize(AiBrain brain, EnemyController controller)
    {
        this.brain = brain;
        this.controller = controller;
        this.feedbackPlayer = FeedbackPlayer.Instance;

        animator = controller.animator;

        abilityAnimHash = Animator.StringToHash("Attack0");
        ResetForSpawn();

        if (data != null && data.forceRecoverOnLand)
        {
            controller.OnLanded += ForceRecover;
        } 
    }

    public bool IsReady()
    {
        return isActiveAndEnabled && data != null && !IsRunning && CooldownRemaining <= 0f;
    }

    public void StartAbility()
    {
        if (IsRunning || data == null || !isActiveAndEnabled)
            return;
        storedWeight = CurrentWeight;
        weightTimestamp = Time.time;
        brain.NotifyAbilityStarted(this);
        controller.ApplyAttackAnimation(data.animationSet);
        ChangeState(AbilityState.Windup);
    }
    public void ForceRecover()
    {
        if (State != AbilityState.Execute)
            return;

        // 강제로 Recover 상태로 변경
        ChangeState(AbilityState.Recover);
    }

    // AiBrain이 매 프레임 실행
    public void UpdateAbility()
    {
        if (State == AbilityState.None) return;

        float elapsed = Time.time - stateStartTime;

        switch (State)
        {
            case AbilityState.Windup:
                OnWindup(elapsed);
                if (State == AbilityState.Windup && CanFinishWindup(elapsed))
                    ChangeState(AbilityState.Execute);
                break;

            case AbilityState.Execute:
                OnExecute(elapsed);
                if (State == AbilityState.Execute && CanFinishExecute(elapsed))
                    ChangeState(AbilityState.Recover);
                break;

            case AbilityState.Recover:
                OnRecover(elapsed);
                if (State == AbilityState.Recover && CanFinishRecover(elapsed))
                    StopAbility();
                break;
        }
    }

    public void StopAbility()
    {
        if (!IsRunning)
            return;
        // OnStop에서 후속 패턴을 예약해도 이번 실행의 종료는 한 번만 기록한다.
        storedWeight += WeightChangeOnUse;
        lastUseTime = Time.time;
        weightTimestamp = Time.time;
        State = AbilityState.None;
        if (animator != null) animator.SetInteger(abilityAnimHash, 0);
        OnStop();
    }
    public void ResetForSpawn()
    {
        State = AbilityState.None;
        lastUseTime = float.NegativeInfinity;
        hasSelectionTuning = false;
        storedWeight = BaseWeight;
        weightTimestamp = Time.time;
        LastSelectionOrder = 0;
    }

    internal void RecordSelection(long order) => LastSelectionOrder = order;

    private void ChangeState(AbilityState newState)
    {
        State = newState;
        stateStartTime = Time.time;

        switch (newState)
        {
            case AbilityState.Windup: 
                animator.SetInteger(abilityAnimHash, 1);
                feedbackPlayer.Play(data.windupFeedback, windupPoint ? windupPoint : transform);
                OnWindupStart();
                break;
            case AbilityState.Execute:
                animator.SetInteger(abilityAnimHash, 2);
                feedbackPlayer.Play(data.executeFeedback, executePoint ? executePoint : transform);
                OnExecuteStart(); 
                break;
            case AbilityState.Recover:
                animator.SetInteger(abilityAnimHash, 3);
                feedbackPlayer.Play(data.recoverFeedback, recoverPoint ? recoverPoint : transform);
                OnRecoverStart();
                break;
        }
    }

    // ====== Override this ======
    protected virtual bool CanFinishWindup(float elapsed) => elapsed >= data.windupTime;
    protected virtual bool CanFinishExecute(float elapsed) => elapsed >= data.executeTime;
    protected virtual bool CanFinishRecover(float elapsed) => elapsed >= data.recoverTime;

    protected virtual void OnWindupStart() { }
    protected virtual void OnWindup(float t) { }

    protected virtual void OnExecuteStart() { }
    protected virtual void OnExecute(float t) { }

    protected virtual void OnRecoverStart() { }
    protected virtual void OnRecover(float t) { }
    protected virtual void OnStop() { }
}
