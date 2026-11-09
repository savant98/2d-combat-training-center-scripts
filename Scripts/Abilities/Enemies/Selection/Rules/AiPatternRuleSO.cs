using UnityEngine;

[UnityEngine.Scripting.APIUpdating.MovedFrom(false, sourceNamespace: "", sourceAssembly: "Assembly-CSharp", sourceClassName: "AiPatternRule")]

public abstract class AiPatternRuleSO : ScriptableObject
{
    [SerializeField] private bool isEnabled = true;
    public bool IsEnabled => isEnabled;

    /// <summary>후보 평가 전 필요한 준비 패턴의 사용 가능 여부를 부작용 없이 검사한다.</summary>
    public virtual bool IsCandidateAvailable(AiPatternContext context, AiAbility candidate) => true;

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

    // 선택 평가와 분리된 순수 방향 조회다. 기존 전환 규칙은 순찰 방향에 관여하지 않는다.
    public virtual bool TryResolvePatrolDirection(AiPatternContext context, AiAbility ability, int proposedDirection, out int direction)
    {
        direction = proposedDirection;
        return false;
    }
}