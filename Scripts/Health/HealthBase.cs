using System;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Splines;
using static WeaponBaseSO;

public abstract class HealthBase : MonoBehaviour
{
    [Header("Health Settings")]
    public float maxHP = 100f;
    public float currentHP;
    public bool IsInvincible = false;

    public Action OnDeath;

    protected virtual void Awake()
    {
        currentHP = maxHP;
    }
    public virtual void ResetHealth()
    {
        currentHP = maxHP;
    }
    public virtual void Heal(DamageInfo damageInfo)
    {
        currentHP += damageInfo.damage;

        if (currentHP >= maxHP)
        {
            currentHP = maxHP;
        }

        UIManager.Instance.UpdateHP(currentHP, maxHP);
        FloatingTextManager.Instance.ShowDamage(damageInfo, transform.position + Vector3.up * 0.5f, Color.lightGreen);
    }
    public virtual void TakeDamage(DamageInfo damageInfo, Vector3 hitPosition)
    {
        if (IsInvincible)
            return;

        currentHP -= damageInfo.damage;

        //GameManager.Instance.feedbackPlayer.Play(hitFeedback, transform);

        if (currentHP <= 0)
        {
            currentHP = 0;
            Die();
        }
    }
    protected abstract void Die();
}
