using System;
using System.Collections;
using UnityEngine;
[UnityEngine.Scripting.APIUpdating.MovedFrom(false, sourceNamespace: "", sourceAssembly: "Assembly-CSharp", sourceClassName: "WeaponBase")]
public abstract class WeaponBaseSO : ScriptableObject
{
    [Header("Weapon Information")]
    [Tooltip("세이브·판정 키. 화면 이름을 바꾸려면 displayName을 고친다(이 값을 바꾸면 업그레이드 기록과 해머 판정이 끊긴다).")]
    public string weaponName;
    [Tooltip("화면에 보이는 표준 이름(메뉴·단말기·정산). 비우면 weaponName을 쓴다.")]
    public string displayName;
    public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? weaponName : displayName;
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

    [Header("Enemy Knockback")]
    [Tooltip("일반 피격 시 적에게 더하는 수평 외력 속도. 쉴드 적은 면역이며 0이면 밀리지 않는다.")]
    [Min(0f)] public float knockbackPower;

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

    // 준비 단계가 시작될 때 불린다. 실행 코루틴이 시작되기 전에 보여 줄 것(예: 예고선)이 있는 무기가 재정의한다.
    public virtual void OnWindupStarted(CharacterController2D controller) { }

    // 이 기술을 연출하는 모형의 무기 이름(PlayerWeaponAbility의 무기별 모형 표에서 찾는다). null이면 그 세트의 기술용 모형을 쓴다.
    // 칼을 뽑아 드는 기술처럼, 어느 세트에 넣어도 정해진 무기의 모형으로 연출해야 하는 기술이 재정의한다.
    public virtual string PresentationWeaponName => null;

    // 회수 Clip이 대기 자세에서 끝나는 기술인지. 그러면 대기로 넘어갈 때 섞지 않는다(PlayerWeaponAbility.EndAttack).
    public virtual bool RecoverEndsAtIdle => false;

    [Serializable]
    public struct DamageInfo
    {
        public float damage;
        public DamageType damageType;

        // 발사 시 복사된 출처를 유지하여 무기 교체 후 적중도 구분한다.
        [NonSerialized] public WeaponBaseSO sourceWeapon;

        // 공격 시작 위치와 방향을 보관해 발사 후 플레이어가 이동해도 밀리는 방향을 유지한다.
        [NonSerialized] public Vector2 knockbackOrigin;
        [NonSerialized] public int knockbackFacing;
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
