using UnityEngine;

public sealed class EnemyBurn : MonoBehaviour
{
    public const int Capacity = 5;
    private const float TickInterval = 0.25f;
    private const float DecayInterval = 5f;
    private const float DamagePerStack = 1f;

    private EnemyHealth health;
    private Collider2D body;
    private float nextTick, nextDecay;
    private SpriteRenderer[] embers;
    public int Stacks { get; private set; }

    public static void Apply(EnemyHealth target)
    {
        if (target == null || target.currentHP <= 0f || !target.isActiveAndEnabled) return;
        var burn = target.GetComponent<EnemyBurn>();
        if (burn == null) burn = target.gameObject.AddComponent<EnemyBurn>();
        burn.health = target;
        if (burn.body == null) burn.body = target.GetComponentInChildren<Collider2D>();
        if (burn.Stacks == 0)
        {
            burn.nextTick = Time.time + TickInterval;
            burn.nextDecay = Time.time + DecayInterval;
        }
        burn.Stacks = Mathf.Min(Capacity, burn.Stacks + 1);
        burn.enabled = true;
        WeaponTraitFeedbackSO.Play(WeaponTraitCue.BurnApply, target.transform.position, Vector2.up);
    }

    private void Update()
    {
        if (health.currentHP <= 0f) { Clear(); return; }
        float now = Time.time;
        while (Stacks > 0 && Mathf.Min(nextDecay, nextTick) <= now)
        {
            if (nextDecay <= nextTick + 0.0001f)
            {
                Stacks--;
                nextDecay += DecayInterval;
            }
            else
            {
                nextTick += TickInterval;
                var hit = new WeaponBaseSO.DamageInfo { damage = Stacks * DamagePerStack };
                if (WeaponTraitEffects.ApplyEnemyHit(health, hit, transform.position))
                    WeaponTraitFeedbackSO.Play(WeaponTraitCue.BurnTick, transform.position + Vector3.up * 0.45f, Vector2.up);
                if (!isActiveAndEnabled || health.currentHP <= 0f) { Clear(); return; }
            }
        }
        if (Stacks == 0) Clear();
    }

    private void LateUpdate()
    {
        if (embers == null) embers = new SpriteRenderer[Capacity];
        Vector3 top = transform.position + Vector3.up;
        if (body != null)
        {
            var bounds = body.bounds;
            top = new Vector3(bounds.center.x, bounds.max.y, bounds.center.z);
        }
        for (int i = 0; i < embers.Length; i++)
        {
            if (i < Stacks && embers[i] == null)
            {
                embers[i] = WeaponTraitEffects.MakeSprite("BurnStack", transform, null, 46);
                embers[i].sprite = WeaponTraitEffects.FlameSprite;
            }
            var ember = embers[i];
            if (ember == null) continue;
            ember.enabled = i < Stacks;
            if (!ember.enabled) continue;
            float pulse = Mathf.Sin(Time.time * 8f + i * 1.65f) * 0.5f + 0.5f;
            Vector3 center = top + new Vector3((i - (Stacks - 1) * 0.5f) * 0.265f, 0.34f + pulse * 0.052f, 0f);
            ember.transform.SetPositionAndRotation(center, Quaternion.Euler(0f, 0f, (pulse - 0.5f) * 12f));
            ember.transform.localScale = new Vector3(0.80f, 0.82f + pulse * 0.12f, 1f);
        }
    }

    public void Clear()
    {
        Stacks = 0;
        if (embers != null)
            foreach (var ember in embers)
                if (ember != null) ember.enabled = false;
        enabled = false;
    }

    private void OnDisable() => Clear();
}
