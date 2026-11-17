using UnityEngine;

[CreateAssetMenu(menuName = "Enemy/Ability Data/Aegis Reposition")]
public sealed class AegisRepositionDataSO : FlyingAbilityDataSO
{
    [Header("순간이동 재배치")]
    public Vector2 playerOffset = new(9f, 4f);
    [Min(0f)] public float wallMargin = 2.4f;
    [Min(0f)] public float minimumDistance = 6f;
    [Min(0f)] public float disappearDelay = .06f;
    [Min(0f)] public float appearDelay = .06f;
    public FeedbackDataSO teleportFeedback;
}
