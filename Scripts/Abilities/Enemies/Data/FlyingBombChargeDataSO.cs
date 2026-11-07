using UnityEngine;

[CreateAssetMenu(menuName = "Enemy/Ability Data/Flying Bomb Charge")]
public class FlyingBombChargeDataSO : FlyingAbilityDataSO
{
    [Header("Bomb Charge")]
    [Min(0.1f)] public float detectionDistance = 6f;
    [Header("Retreat")]
    [Min(0.1f)] public float retreatDistance = 4.5f;
    [Min(0.01f)] public float retreatDuration = 0.5f;
    [Min(0f)] public float minimumVerticalSeparation = 3.5f;
    [Min(0.1f)] public float recoverySpeed = 4f;
    public FeedbackDataSO preparationFeedback;
    [Tooltip("Drone 탄속 15보다 15% 느린 고정 돌진 속도입니다.")]
    [Min(0.1f)] public float chargeSpeed = 12.75f;
    [Tooltip("폭발 지름입니다. Player 본체 지름 1.5의 2.5배입니다.")]
    [Min(0.1f)] public float explosionDiameter = 3.75f;
    [Min(0f)] public float explosionDamage = 20f;
    public FeedbackDataSO explosionFeedback;
}
