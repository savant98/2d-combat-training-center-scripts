using UnityEngine;

public class TouchDamage : MonoBehaviour
{
    public float damage = 10f;

    private EnemyController enemy;

    private void Awake()
    {
        enemy = GetComponentInParent<EnemyController>();
    }

    private void OnTriggerStay2D(Collider2D collision)
    {
        if (!collision.TryGetComponent<PlayerHealth>(out var playerHealth))
            return;

        enemy.RequestTouchDamage(playerHealth, damage);
    }
}
