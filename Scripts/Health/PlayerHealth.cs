using UnityEngine;
using System.Collections;
using System;
public class PlayerHealth : HealthBase
{
    [Header("Health Data")]
    [SerializeField] private PlayerHealthDataSO healthData;

    [Header("Hit Sprite Effect ")]
    [SerializeField] private HitAbility hitAbility;
    [SerializeField] private SpriteRenderer hitSprite;

    public event Action OnPlayerDeath;
    public event Action<WeaponBaseSO.DamageInfo> OnDamaged;

    private Material hitMaterial;
    private Coroutine coroutine;
    private Color initialColor;
    protected override void Awake()
    {
        base.Awake();

        hitMaterial = Instantiate(hitSprite.material);
        hitSprite.material = hitMaterial;

        initialColor = hitMaterial.GetColor("_EmissionColor");

        maxHP = healthData.maxhp;
    }
    public override void TakeDamage(WeaponBaseSO.DamageInfo damageInfo, Vector3 hitPosition)
    {
        var grab = GetComponentInParent<PlayerController>().AbilityManager.Grab;
        // 잡기 보호는 반격 판정보다 먼저 적용하고 전역 무적 상태와 분리한다.
        if (grab != null && grab.BlocksOtherDamage) return;
        if (TryCounter(hitPosition) || IsInvincible || !hitAbility.CanTakeDamage()) return;
        ApplyDamage(damageInfo, hitPosition, false);
    }
    public bool TakeGrabDamage(UnityEngine.Object attacker, uint session, WeaponBaseSO.DamageInfo damageInfo, Vector3 hitPosition)
    {
        var grab = GetComponentInParent<PlayerController>().AbilityManager.Grab;
        if (grab == null || !grab.IsOwnedBy(attacker, session) || currentHP <= 0) return false;
        ApplyDamage(damageInfo, hitPosition, true); return true;
    }
    private void ApplyDamage(WeaponBaseSO.DamageInfo damageInfo, Vector3 hitPosition, bool grabbed)
    {
        currentHP -= damageInfo.damage;
        OnDamaged?.Invoke(damageInfo);
        if (UIManager.Instance != null) UIManager.Instance.UpdateHP(currentHP, maxHP);
        if (FloatingTextManager.Instance != null)
            FloatingTextManager.Instance.ShowDamage(damageInfo, transform.position + Vector3.up * .5f, Color.red);
        if (currentHP <= 0)
        {
            currentHP = 0;
            if (grabbed) CompleteDeath(); else Die();
            return;
        }
        if (!grabbed) hitAbility.Hit();
        if (hitSprite != null)
        {
            if (coroutine != null) StopCoroutine(coroutine);
            coroutine = StartCoroutine(HitSpriteFeedback());
        }
    }
    private bool TryCounter(Vector3 hitPosition)
    {
        if (!TryGetComponent<PlayerCounterState>(out var counter))
            return false;

        return counter.TryCounter(hitPosition);
    }
    private IEnumerator HitSpriteFeedback()
    {
        hitMaterial.SetColor("_EmissionColor", Color.red * 3f);
        float intensityValue = 3f;

        while (intensityValue > 0)
        {
            hitMaterial.SetColor("_EmissionColor", Color.red * intensityValue);
            intensityValue -= Time.deltaTime * 5f;
            yield return null;
        }

        hitMaterial.SetColor("_EmissionColor", initialColor);
    }
    public override void ResetHealth()
    {
        base.ResetHealth();
        UIManager.Instance.UpdateHP(maxHP, maxHP);
        IsInvincible = false;
        hitMaterial.SetColor("_EmissionColor", initialColor);
    }
    protected override void Die()
    {
        if (IsInvincible)
            return;

        CompleteDeath();
    }
    private void CompleteDeath()
    {
        IsInvincible = true;
        Debug.Log("Player Dead");
        // 플레이어 사망 연출, 리스폰 등
        hitMaterial.SetColor("_EmissionColor", Color.red * 3f);
        hitAbility.ForceStop();
        OnPlayerDeath?.Invoke();
    }
}
