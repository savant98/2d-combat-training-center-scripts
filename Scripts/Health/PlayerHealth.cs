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
    // 실제로 잃은 체력(남은 체력을 넘는 피해는 뺀다)과 실제로 회복한 체력. 훈련 기록이 읽기만 한다.
    public event Action<WeaponBaseSO.DamageInfo, float> OnDamageApplied;
    public event Action<WeaponBaseSO.DamageInfo, float> OnHealed;
    // 사망 처리부터 ResetHealth까지 true다. 사망 이벤트는 사망 한 번에 한 번만 나간다.
    public bool IsDead { get; private set; }
    // 지금 처리 중인 피격이 끝나면 사망한다(피격 이벤트 안에서 ForceDeath가 요청됨). 같은 피격을 듣는 쪽이 회복·되돌림을 건너뛸 때 본다.
    public bool IsDeathPending => deathRequested;
    private bool applyingDamage;
    private bool deathRequested;

    private Material hitMaterial;
    private Coroutine coroutine;
    private Color initialColor;
    // 회복 순간 본체가 빛나는 색과 원래 발광색으로 돌아오는 시간
    private static readonly Color HealFlashColor = new Color(0.25f, 1f, 0.45f) * 3.4f;
    private const float HealFlashTime = 0.45f;
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
        if (IsDead) return;
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
        float before = currentHP;
        currentHP -= damageInfo.damage;
        applyingDamage = true;
        try
        {
            OnDamageApplied?.Invoke(damageInfo, Mathf.Clamp(damageInfo.damage, 0f, Mathf.Max(0f, before)));
            OnDamaged?.Invoke(damageInfo);
        }
        finally { applyingDamage = false; }
        // 이 피격으로 패배가 확정됐으면(ForceDeath 요청) 피격 경직·히트스톱 없이 이 자리에서 사망 처리한다.
        // 피격 히트스톱이 걸리면 그것이 끝나면서 사망 연출의 시간 정지를 풀어 버린다.
        bool forced = deathRequested;
        deathRequested = false;
        if (forced) currentHP = 0;
        if (UIManager.Instance != null) UIManager.Instance.UpdateHP(currentHP, maxHP);
        if (FloatingTextManager.Instance != null)
            FloatingTextManager.Instance.ShowDamage(damageInfo, transform.position + Vector3.up * .5f, Color.red);
        if (currentHP <= 0)
        {
            currentHP = 0;
            if (grabbed || forced) CompleteDeath(); else Die();
            return;
        }
        if (!grabbed) hitAbility.Hit();
        if (hitSprite != null)
        {
            if (coroutine != null) StopCoroutine(coroutine);
            coroutine = StartCoroutine(HitSpriteFeedback());
        }
    }
    // 회복 순간 본체 톱니가 초록빛으로 번쩍였다가 원래 발광색으로 돌아온다. 피격 번쩍임과 같은 경로라 서로 끊어 준다.
    public override void Heal(WeaponBaseSO.DamageInfo damageInfo)
    {
        float before = currentHP;
        base.Heal(damageInfo);
        OnHealed?.Invoke(damageInfo, Mathf.Max(0f, currentHP - before));
        if (hitSprite == null) return;
        if (coroutine != null) StopCoroutine(coroutine);
        coroutine = StartCoroutine(HealSpriteFeedback());
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
    private IEnumerator HealSpriteFeedback()
    {
        float elapsed = 0f;
        while (elapsed < HealFlashTime)
        {
            hitMaterial.SetColor("_EmissionColor", Color.Lerp(HealFlashColor, initialColor, elapsed / HealFlashTime));
            elapsed += Time.deltaTime;
            yield return null;
        }

        hitMaterial.SetColor("_EmissionColor", initialColor);
    }
    public override void ResetHealth()
    {
        base.ResetHealth();
        UIManager.Instance.UpdateHP(maxHP, maxHP);
        IsInvincible = false;
        IsDead = false;
        hitMaterial.SetColor("_EmissionColor", initialColor);
    }
    protected override void Die()
    {
        if (IsInvincible)
            return;

        CompleteDeath();
    }
    // 무적·잡기 보호·반격 창·피격 무적과 무관하게 한 번만 사망 처리한다(훈련 성적이 미흡이 됐을 때 TrainingRunTracker가 부른다).
    // 이미 사망했으면 false. 피격 이벤트(OnDamaged) 안에서 부르면 그 피격 처리가 끝나는 자리에서 사망한다.
    public bool ForceDeath()
    {
        if (IsDead) return false;
        if (applyingDamage)
        {
            deathRequested = true;
            return true;
        }
        currentHP = 0;
        if (UIManager.Instance != null) UIManager.Instance.UpdateHP(currentHP, maxHP);
        hitAbility.CancelForGrab();     // 피격 경직 중이면 Hit 자세를 끈다
        CompleteDeath();
        return true;
    }
    private void CompleteDeath()
    {
        if (IsDead) return;
        IsDead = true;
        IsInvincible = true;
        if (coroutine != null) StopCoroutine(coroutine);
        Debug.Log("Player Dead");
        // 플레이어 사망 연출, 리스폰 등
        hitMaterial.SetColor("_EmissionColor", Color.red * 3f);
        hitAbility.ForceStop();
        OnPlayerDeath?.Invoke();
    }
}
