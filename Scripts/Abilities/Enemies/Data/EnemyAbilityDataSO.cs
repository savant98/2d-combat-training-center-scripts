using UnityEngine;
[UnityEngine.Scripting.APIUpdating.MovedFrom(false, sourceNamespace: "", sourceAssembly: "Assembly-CSharp", sourceClassName: "EnemyAbilityData")]
public abstract class EnemyAbilityDataSO : ScriptableObject
{
    [Header("Ability Key")]
    public string abilityKey;

    [Header("Animation")]
    public AnimationSetSO animationSet;

    [Header("가중치 선택")]
    [Tooltip("스폰 시의 초기 점수입니다. 확률이 아니라 최고 점수를 선택합니다.")]
    public float baseWeight = 100f;
    [Tooltip("패턴 종료·중단 시 자신의 점수에 더합니다. 음수는 반복 사용을 억제합니다.")]
    public float weightChangeOnUse = -100f;
    [Tooltip("실행하지 않는 동안 초당 회복하는 점수입니다. 0이면 종료 시 증감만 사용합니다.")]
    [Min(0f)] public float weightRecoveryPerSecond = 0f;

    [Header("패턴 접촉 피해")]
    [Tooltip("이 패턴이 실행 중일 때 몸통·검·돌진의 접촉 피해를 아래 값으로 덮어씁니다. 끄면 Prefab 기본값을 사용합니다.")]
    public bool overrideContactDamage;
    [Min(0f)] public float contactDamage = 20f;

    [Header("Times")]
    [Tooltip("Recover까지 종료하거나 중단한 시점부터 재사용까지의 대기 시간입니다.")]
    public float cooldown = 1f;
    public float windupTime = 0.3f;
    public float executeTime = 0.5f;
    public float recoverTime = 0.3f;

    [Header("Feedbacks")]
    public FeedbackDataSO windupFeedback;
    public FeedbackDataSO executeFeedback;
    public FeedbackDataSO recoverFeedback;

    [Header("ForceRecover")]
    public bool forceRecoverOnLand = false;
}
