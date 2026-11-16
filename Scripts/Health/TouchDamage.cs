using System.Collections.Generic;
using UnityEngine;

[DefaultExecutionOrder(150)]
public class TouchDamage : MonoBehaviour
{
    public float damage = 10f;
    [Tooltip("몸통은 패턴의 공격 구간을 따릅니다. 검·다리 등 별도 무기 판정은 끕니다.")]
    [SerializeField] private bool isBodyContact = true;
    public bool IsBodyContact => isBodyContact;
    public float CurrentDamage => isActiveAndEnabled && contact != null && contact.enabled && enemy != null
        && enemy.CanDealContactDamage
        ? isBodyContact ? enemy.ResolveBodyContactDamage(damage) : enemy.ResolveContactDamage(damage) : 0f;
    private EnemyController enemy;
    private Collider2D contact;
    private ContactFilter2D filter;
    private readonly List<Collider2D> contacts = new List<Collider2D>(8);
    private readonly HashSet<PlayerHealth> damaged = new HashSet<PlayerHealth>();
    private double capturedAt = double.NaN;
    private float capturedDamage;
    private static double lastQueryTick = double.NaN;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetQueryTick() => lastQueryTick = double.NaN;

    private void Awake()
    {
        enemy = GetComponentInParent<EnemyController>();
        contact = GetComponent<Collider2D>();
        filter = new ContactFilter2D();
        filter.SetLayerMask(LayerMask.GetMask("Player"));
        filter.useTriggers = true;
    }

    internal void CaptureBodyContactStep()
    {
        if (!isBodyContact) return;
        capturedAt = Time.fixedTimeAsDouble;
        capturedDamage = CurrentDamage;
    }

    private void FixedUpdate()
    {
        // 이동 전 공격 구간과 이동 후 실제 형상을 함께 사용해 착지한 물리 틱의 공격을 보존한다.
        if (!isBodyContact || capturedAt != Time.fixedTimeAsDouble || capturedDamage <= 0f
            || contact == null || !contact.enabled || enemy == null || !enemy.CanDealContactDamage) return;
        if (lastQueryTick != Time.fixedTimeAsDouble)
        {
            lastQueryTick = Time.fixedTimeAsDouble;
            RaycastMotor2D.InvalidateAttackQuerySync();
        }
        enemy.Motor.OverlapAttackCollider(contact, filter, contacts);
        damaged.Clear();
        foreach (var other in contacts)
        {
            var health = other.GetComponentInParent<PlayerHealth>();
            if (health != null && damaged.Add(health)) enemy.ApplyBodyContactStep(health, capturedDamage);
        }
    }

    private void OnDisable()
    {
        capturedAt = double.NaN;
        capturedDamage = 0f;
    }

    private void OnTriggerStay2D(Collider2D collision)
    {
        // 몸통은 위의 모터 질의만 사용하고 기존 무기의 Animation 구간은 그대로 유지한다.
        if (isBodyContact || !isActiveAndEnabled || enemy == null || contact == null || !contact.enabled) return;
        var health = collision.GetComponentInParent<PlayerHealth>();
        if (health != null) enemy.RequestTouchDamage(health, damage, false);
    }
}
