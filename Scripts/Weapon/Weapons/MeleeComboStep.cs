using UnityEngine;

[System.Serializable]
public class MeleeComboStep
{
    public AnimationSet animationSet;

    public float windupTime;
    public float executeTime;
    public float recoverTime;
    public float comboQueueWindow;

    public FeedbackData hitFeedbackData;
    public WeaponBase.DamageInfo damageInfo;
}
