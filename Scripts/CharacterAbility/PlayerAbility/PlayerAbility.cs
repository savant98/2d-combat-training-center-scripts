using UnityEngine;

public abstract class PlayerAbility : MonoBehaviour
{
    [Header("Ability Data")]
    [SerializeField] protected PlayerAbilityData data;

    [HideInInspector] public PlayerController controller;
    protected FeedbackPlayer feedbackPlayer;
    protected AbilityManager manager;
    public abstract PlayerActionType ActionType { get; }
    public virtual bool IsActive => false; // 현재 동작중인지 확인
    public virtual void Initialize(AbilityManager manager)
    {
        this.manager = manager;
        feedbackPlayer = FeedbackPlayer.Instance;
    }
    public virtual void Interrupt() { }
    public virtual void Tick() { }
    protected virtual bool CanDo()
    {
        return manager.StateMachine.CanPerform(ActionType);
    }
    protected virtual void Awake()
    {
        controller = GetComponent<PlayerController>();
    }
}
