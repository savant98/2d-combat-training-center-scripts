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
    public bool AllowRandomSelection => allowRandomSelection;
    public string AbilityKey => data != null ? data.abilityKey : string.Empty;

    [Header("EnemyAbilityData")]
    [SerializeField] protected EnemyAbilityDataSO data;

    [Header("Feedback Points")]
    [SerializeField] protected Transform windupPoint;
    [SerializeField] protected Transform executePoint;
    [SerializeField] protected Transform recoverPoint;

    private int abilityAnimHash;
    private float lastUseTime = -999f;
    private float stateStartTime;

    public AbilityState State { get; private set; } = AbilityState.None;
    public bool IsRunning => State != AbilityState.None;

    public void Initialize(AiBrain brain, EnemyController controller)
    {
        this.brain = brain;
        this.controller = controller;
        this.feedbackPlayer = FeedbackPlayer.Instance;

        animator = controller.animator;

        abilityAnimHash = Animator.StringToHash("Attack0");

        if (data.forceRecoverOnLand)
        {
            controller.OnLanded += ForceRecover;
        } 
    }

    public bool IsReady()
    {
        return Time.time >= lastUseTime + data.cooldown;
    }

    public void StartAbility()
    {
        controller.ApplyAttackAnimation(data.animationSet);
        lastUseTime = Time.time;
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
                if (CanFinishWindup(elapsed))
                    ChangeState(AbilityState.Execute);
                break;

            case AbilityState.Execute:
                OnExecute(elapsed);
                if (CanFinishExecute(elapsed))
                    ChangeState(AbilityState.Recover);
                break;

            case AbilityState.Recover:
                OnRecover(elapsed);
                if (CanFinishRecover(elapsed))
                    StopAbility();
                break;
        }
    }

    public void StopAbility()
    {
        State = AbilityState.None;
        animator.SetInteger(abilityAnimHash, 0);
        OnStop();
    }
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
