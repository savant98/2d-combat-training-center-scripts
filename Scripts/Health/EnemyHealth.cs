using UnityEngine;
using System.Collections;
using System;

public class EnemyHealth : HealthBase
{
    [Header("Ai Brain")]
    [SerializeField] private AiBrain brain;

    [Header("Shild Data")]
    public EnemyShieldSystem enemyShieldSystem;

    [Header("Hit Sprite Effect")]
    [SerializeField] private FeedbackData destroyFeedback;
    [SerializeField] private EnemyUI enemyUI;
    [SerializeField] private SpriteRenderer[] hitSprites;

    public bool IsBossEnemy { get; set; }

    private Coroutine coroutine;
    protected override void Awake()
    {
        base.Awake();
        if (enemyShieldSystem != null)
            enemyShieldSystem.SetShieldSystem(this, enemyUI, brain);
    }
    protected override void Die()
    {
        FeedbackPlayer.Instance.Play(destroyFeedback, transform);
        OnDeath?.Invoke();
    }
    public override void TakeDamage(WeaponBase.DamageInfo damageInfo, Vector3 hitPosition)
    {
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

