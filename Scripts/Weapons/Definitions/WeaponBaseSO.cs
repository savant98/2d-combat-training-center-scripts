using System;
using System.Collections;
using UnityEngine;
[UnityEngine.Scripting.APIUpdating.MovedFrom(false, sourceNamespace: "", sourceAssembly: "Assembly-CSharp", sourceClassName: "WeaponBase")]
public abstract class WeaponBaseSO : ScriptableObject
{
    [Header("Weapon Information")]
    public string weaponName;
    public Sprite weaponSprite;
    public UpgradeNodeInfo unlockInfo;
    public UpgradeNodeInfo leftNodeInfo;
    public UpgradeNodeInfo rightNodeInfo;

    [Header("Attack Timing")]
    public float windupTime = 0.1f; // 선딜
    public float executeTime = 0.2f; // 공격 유지시간
    public float recoverTime = 0.25f; // 후딜
    public float cooldown = 0.3f; // 무기별 쿨타임

    [Header("Recoil")]
    public float recoilPower = 2f;

    [Header("DamageInfo Settings")]
    public DamageInfo damageInfo;
    public float skillGauge;
    public float skillGainPerHit;

    [Header("Animation")]
    public string weaponAnim = "WeaponPhase";
    public AnimationSetSO animationSet;

    [Header("Feedbacks")]
    public FeedbackDataSO windupFeedback;
    public FeedbackDataSO executeFeedback;
    public FeedbackDataSO recoverFeedback;

    public Action<CharacterController2D, Vector2> OnHitEnemy;
    public abstract IEnumerator WeaponUseSequence(CharacterController2D controller, Action forceRecover = null);

    [Serializable]
    public struct DamageInfo
    {
        public float damage;
        public DamageType damageType;

        // 발사 시 복사된 출처를 유지하여 무기 교체 후 적중도 구분한다.
        [NonSerialized] public WeaponBaseSO sourceWeapon;
    }

    protected DamageInfo WithSource(DamageInfo info)
    {
        info.sourceWeapon = this;
        return info;
    }

    [Serializable]
    public struct UpgradeNodeInfo
    {
        public string nodeName;
        [TextArea(2, 4)]
        public string description;
        public int upgradeCost;
    }

    public virtual bool ShouldEnterRecover()
    {
        return true;
    }

    public virtual bool CanForceRecoverOnSuccess()
    {
        return false;
    }
}
