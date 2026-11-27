using UnityEngine;

public class PlayerCounterState : MonoBehaviour
{
    private static ProjectileBase currentImpactProjectile;
    /// <summary>지금 피격 판정을 실행 중인 투사체(반격 성공 시 해당 투사체를 즉시 식별·소멸시키기 위해 기록).</summary>
    public static ProjectileBase CurrentImpactProjectile => currentImpactProjectile;

    internal static void PushImpactProjectile(ProjectileBase projectile)
    {
        currentImpactProjectile = projectile;
    }

    internal static void PopImpactProjectile(ProjectileBase projectile)
    {
        if (currentImpactProjectile == projectile)
            currentImpactProjectile = null;
    }

    public bool IsWaiting { get; private set; }
    public bool IsSuccess { get; private set; }
    /// <summary>반격 성공 무적이 켜져 있는가(CounterShield 잔광 표시가 읽는다).</summary>
    public bool IsProtected => protectedHealth != null;
    /// <summary>반격 성공 무적의 남은 시간(초).</summary>
    public float ProtectionRemaining => protectedHealth != null ? Mathf.Max(0f, protectionEndTime - Time.time) : 0f;

    private BoxCollider2D counterRange;
    public Collider2D CounterRange => counterRange;
    private void SetRange(bool enabled)
    {
        if (enabled && counterRange == null)
        {
            var body = GetComponentInChildren<PlayerHealth>().GetComponent<Collider2D>();
            if (body == null) body = GetComponentInChildren<Collider2D>();
            if (body == null) return;
            var sensor = new GameObject("CounterRange");
            sensor.layer = LayerMask.NameToLayer("Player");
            sensor.transform.SetParent(transform, false);
            counterRange = sensor.AddComponent<BoxCollider2D>();
            counterRange.isTrigger = true;
            sensor.transform.position = body.bounds.center;
            Vector3 scale = transform.lossyScale;
            counterRange.size = new Vector2(body.bounds.size.x / Mathf.Abs(scale.x), body.bounds.size.y / Mathf.Abs(scale.y)) * 1.5f;
        }
        if (counterRange != null) counterRange.enabled = enabled;
        RaycastMotor2D.InvalidateAttackQuerySync();
    }
    private System.Action<Vector3> onSuccess;
    private PlayerHealth protectedHealth;
    private bool savedInvulnerability;
    private float protectionEndTime;         // 무적이 끝나는 시각
    /// <summary>
    /// 반격 성공 무적을 지금부터 duration초 동안 켠다. 공격 상태와 무관하게 이어지므로 회수 동작이 끝나도(대시로 취소해도) 유지되어,
    /// 몸으로 부딪히는 공격을 반격한 뒤 판정 밖으로 빠져나갈 시간을 준다.
    /// 반격 창 자체에는 무적을 걸지 않는다. 걸어 두면 IsInvincible을 먼저 검사하는 공격(탄환·유도탄·던진 큐브·충격파·장판)이
    /// TakeDamage까지 오지 못해 반격이 발동되지 않는다. 창이 열린 동안의 피해는 TryCounter가 모두 받아낸다.
    /// </summary>
    public void BeginInvulnerability(PlayerHealth health, float duration)
    {
        protectionEndTime = Time.time + duration;
        if (protectedHealth != null) return;
        protectedHealth = health; savedInvulnerability = health.IsInvincible; health.IsInvincible = true;
    }
    public void EndInvulnerability()
    {
        if (protectedHealth == null) return;
        if (protectedHealth.currentHP > 0) protectedHealth.IsInvincible = savedInvulnerability;
        protectedHealth = null;
    }
    private void Update()
    {
        if (protectedHealth != null && Time.time >= protectionEndTime) EndInvulnerability();
    }
    private void OnDisable() { EndCounter(); EndInvulnerability(); }

    public void StartCounter(System.Action<Vector3> onSuccess)
    {
        IsWaiting = true;
        IsSuccess = false;
        this.onSuccess = onSuccess;
        SetRange(true);
    }

    public bool TryCounter(Vector3 hitPosition)
    {
        if (!IsWaiting)
            return false;

        IsWaiting = false;
        IsSuccess = true;
        SetRange(false);

        onSuccess?.Invoke(hitPosition);
        return true;
    }

    public void EndCounter()
    {
        SetRange(false);
        IsWaiting = false;
        IsSuccess = false;
        onSuccess = null;
    }
}