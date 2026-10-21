using UnityEngine;

[System.Serializable]
public class MeleeComboStep
{
    public AnimationSetSO animationSet;

    public float windupTime;
    public float executeTime;
    public float recoverTime;
    public float comboQueueWindow;

    public FeedbackDataSO hitFeedbackData;
    public WeaponBaseSO.DamageInfo damageInfo;
}
