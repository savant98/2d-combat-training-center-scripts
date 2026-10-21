using UnityEngine;

[CreateAssetMenu(menuName = "Enemy/Shield Data")]
[UnityEngine.Scripting.APIUpdating.MovedFrom(false, sourceNamespace: "", sourceAssembly: "Assembly-CSharp", sourceClassName: "EnemyShieldData")]
public class EnemyShieldDataSO : ScriptableObject
{
    [Header("Shild Settings")]
    public float maxShield = 50f;
    public float shieldRegenRate = 5f; // 초당 5씩 회복
    public float regenDelay = 2f; // 피해 후 2초 뒤 회복 시작
    public float breakDuration = 3f; // 브레이크 상태 유지 시간
    public float breakableDuration = 3f; // 브레이크 가능 상태 유지 시간
    public float breakFailedRecoveryValue = 0.3f; // 브레이크 실패 시 쉴드 회복 계수

    [Header("Feedbacks")]
    public FeedbackDataSO breakFeedback;
    public FeedbackDataSO shieldBreakFeedback;
    public FeedbackDataSO shieldRegenFeedback;

    [Header("Animation")]
    public string breakAnimTrigger;
}
