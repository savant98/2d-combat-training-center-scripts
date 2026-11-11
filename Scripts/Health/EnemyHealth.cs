using UnityEngine;
using System.Collections;
using System;
using System.Collections.Generic;

public class EnemyHealth : HealthBase
{
    [Header("체력 SO")]
    [SerializeField] private EnemyHealthDataSO healthData;
    public EnemyHealthDataSO HealthData => healthData;

    private void ApplyHealthData()
    {
        if (healthData != null) maxHP = Mathf.Max(1f, healthData.maxHP);
    }

    [Header("Ai Brain")]
    [SerializeField] private AiBrain brain;

    [Header("Shild Data")]
    public EnemyShieldSystem enemyShieldSystem;

    [Header("Hit Sprite Effect")]
    [SerializeField] private FeedbackDataSO destroyFeedback;
    [SerializeField] private EnemyUI enemyUI;
    [SerializeField] private SpriteRenderer[] hitSprites;

    public bool IsBossEnemy { get; set; }
    public float MinimumHealth => minimumHealth;

    // 적이 실제로 잃은 체력(무적 차단·체력 하한 적용 뒤, 남은 체력을 넘는 피해는 뺀다). 체력이 줄기 직전에 나간다.
    // 훈련 기록이 읽기만 한다. 구독자는 게임 상태를 바꾸지 않는다.
    public static event Action<EnemyHealth, WeaponBaseSO.DamageInfo, float> DamageApplied;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetDamageApplied() => DamageApplied = null;

    private readonly Dictionary<int,UnityEngine.Object> invulnerabilityOwners = new Dictionary<int,UnityEngine.Object>();
    private readonly List<int> expiredInvulnerability = new List<int>();
    private int nextInvulnerability;
    public bool IsDamageBlocked
    {
        get
        {
            expiredInvulnerability.Clear();
            foreach(var pair in invulnerabilityOwners)
                if(pair.Value == null || (pair.Value is Behaviour behaviour && !behaviour.isActiveAndEnabled)) expiredInvulnerability.Add(pair.Key);
            foreach(int id in expiredInvulnerability) invulnerabilityOwners.Remove(id);
            return IsInvincible || invulnerabilityOwners.Count>0;
        }
    }
    /// <summary>호출한 공격의 임시 무적만 반환하며 기존 무적 상태는 보존한다.</summary>
    public IDisposable AcquireInvulnerability(UnityEngine.Object owner)
    {
        int id=++nextInvulnerability; invulnerabilityOwners[id]=owner; return new InvulnerabilityLease(this,id);
    }
    private sealed class InvulnerabilityLease : IDisposable
    {
        private EnemyHealth health; private readonly int id;
        public InvulnerabilityLease(EnemyHealth health,int id) { this.health=health; this.id=id; }
        public void Dispose() { if(health!=null) health.invulnerabilityOwners.Remove(id); health=null; }
    }
    private EnemyController controller;
    private float minimumHealth;
    private Coroutine coroutine;
    protected override void Awake()
    {
        ApplyHealthData();
        base.Awake();
        controller = GetComponentInParent<EnemyController>();
        if (enemyShieldSystem != null)
            enemyShieldSystem.SetShieldSystem(this, enemyUI, brain);
    }
    // 연출 전투의 현재 인스턴스만 보호하고 공유 Prefab의 체력 설정은 바꾸지 않는다.
    public void SetMinimumHealth(float value)
    {
        minimumHealth = Mathf.Clamp(value, 0f, Mathf.Max(0f, maxHP));
        if (minimumHealth > 0f)
            currentHP = Mathf.Max(currentHP, minimumHealth);
    }
    private void OnDisable()
    {
        // 풀에서 다시 사용될 때 일반 전투로 보호 상태가 이어지지 않게 한다.
        minimumHealth = 0f; invulnerabilityOwners.Clear();
    }
    protected override void Die()
    {
        FeedbackPlayer.Instance.Play(destroyFeedback, transform);
        OnDeath?.Invoke();
    }
    public override void TakeDamage(WeaponBaseSO.DamageInfo damageInfo, Vector3 hitPosition)
    {
        if(IsDamageBlocked) return;
        if (minimumHealth > 0f)
        {
            // Die 호출 전에 양의 피해만 남은 허용량으로 제한한다. 음수 피해는 기존 동작을 유지한다.
            damageInfo.damage = Mathf.Min(damageInfo.damage, Mathf.Max(0f, currentHP - minimumHealth));
        }
        float applied = Mathf.Clamp(damageInfo.damage, 0f, Mathf.Max(0f, currentHP));
        if (applied > 0f)
        {
            // 무적·0 피해·사망 후 풀 반환에는 외력을 남기지 않는다.
            if (currentHP > damageInfo.damage && controller != null)
                controller.ApplyWeaponKnockback(damageInfo);
            DamageApplied?.Invoke(this, damageInfo, applied);
        }
        base.TakeDamage(damageInfo, hitPosition);

        enemyUI.ShowHP(currentHP / maxHP, IsBossEnemy);
        FloatingTextManager.Instance.ShowDamage(damageInfo, hitPosition + Vector3.up * 0.5f);

        if (hitSprites != null)
        {
            if (coroutine != null)
                StopCoroutine(coroutine);

            if (currentHP <= 0)
            {
                return;
            }
            coroutine = StartCoroutine(HitSpriteFeedback());
        }
    }
    public override void ResetHealth()
    {
        ApplyHealthData();
        base.ResetHealth();

        foreach (var sprite in hitSprites)
        {
            if (sprite != null)
                sprite.color = new Color(1f, 1f, 1f, 0f);
        }
    }
    private IEnumerator HitSpriteFeedback()
    {
        // 타격 직후 모든 스프라이트를 빨간색으로 변경
        foreach (var sprite in hitSprites)
        {
            if (sprite != null)
                sprite.color = Color.red;
        }

        float alphaValue = 1f;

        while (alphaValue > 0.02f)
        {
            alphaValue -= Time.deltaTime * 5f;

            // 서서히 투명해지는 연출을 모든 스프라이트에 적용
            foreach (var sprite in hitSprites)
            {
                if (sprite != null)
                    sprite.color = new Color(1f, 0f, 1f, alphaValue);
            }

            yield return null;
        }

        // 연출 종료 후 완전 투명화
        foreach (var sprite in hitSprites)
        {
            if (sprite != null)
                sprite.color = new Color(1f, 1f, 1f, 0f);
        }
    }

}

