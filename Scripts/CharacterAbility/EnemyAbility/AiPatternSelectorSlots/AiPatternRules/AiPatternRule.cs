using UnityEngine;

public abstract class AiPatternRule : ScriptableObject
{
    [SerializeField] private bool isEnabled = true;
    public bool IsEnabled => isEnabled;

    /// <summary>
    /// candidate를 이 룰이 처리할 대상인지 먼저 판단.
    /// 관련 없으면 그대로 false 반환.
    /// </summary>
    public abstract bool CanHandle(AiPatternContext context, AiAbility candidate);

    /// <summary>
    /// candidate를 유지하거나 다른 ability로 교체해서 반환.
    /// null 반환은 하지 않는 걸 기본 원칙으로 권장.
    /// </summary>
    public abstract AiAbility Resolve(AiPatternContext context, AiAbility candidate);
}