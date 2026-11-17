using UnityEngine;

[System.Serializable]
public class MeleeComboStep
{
    public AnimationSetSO animationSet;

    public float windupTime;
    public float executeTime;
    public float recoverTime;
    public float comboQueueWindow;
    [Tooltip("회수 Clip이 대기 자세에서 끝나는 단계(해머 3타처럼 무기를 제자리에 내려놓거나, 칼처럼 검집에 넣고 끝남). 켜면 이 단계에서 공격이 끝날 때 회수 → 대기를 섞지 않고 바로 대기로 넘어간다.")]
    public bool recoverEndsAtIdle;

    [Tooltip("이 단계의 실행이 시작될 때 재생할 Feedback(휘두름 소리 등). 비우면 무기의 executeFeedback을 쓴다.")]
    public FeedbackDataSO executeFeedback;
    public FeedbackDataSO hitFeedbackData;
    public WeaponBaseSO.DamageInfo damageInfo;
}
