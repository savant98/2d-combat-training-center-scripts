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
        if (TryCounter(hitPosition))
            return;

        if (IsInvincible)
            return;

        if (!hitAbility.CanTakeDamage())
            return;

        // 데미지 적용
        currentHP -= damageInfo.damage;

        // 피격 이벤트 호출
        OnDamaged?.Invoke(damageInfo);

        UIManager.Instance.UpdateHP(currentHP, maxHP);
        FloatingTextManager.Instance.ShowDamage(damageInfo, transform.position + Vector3.up * 0.5f, Color.red);

        if (currentHP <= 0)
        {
            currentHP = 0;
            Die();
            return;
        }

        // 살아있을 때만 Hit 연출
        hitAbility.Hit();

        if (hitSprite != null)
        {
            if (coroutine != null)
                StopCoroutine(coroutine);

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

        IsInvincible = true;
        Debug.Log("Player Dead");
        // 플레이어 사망 연출, 리스폰 등
        hitMaterial.SetColor("_EmissionColor", Color.red * 3f);
        hitAbility.ForceStop();
        OnPlayerDeath?.Invoke();
    }
}
