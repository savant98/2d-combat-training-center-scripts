using UnityEngine;

// Ability 선택은 유지하고, 면 이동 직전 동종의 위치만 읽어 순찰 방향을 정한다.
[CreateAssetMenu(menuName = "Enemy/AI Rules/Bulwark Patrol Direction")]
public sealed class BulwarkPatrolDirectionRuleSO : AiPatternRuleSO
{
    [SerializeField, Min(0f)] private float separationPadding = .3f;
    private readonly Collider2D[] hits = new Collider2D[64];

    public override bool CanHandle(AiPatternContext context, AiAbility candidate) => false;
    public override AiAbility Resolve(AiPatternContext context, AiAbility candidate) => candidate;

    public override bool TryResolvePatrolDirection(AiPatternContext context, AiAbility ability, int proposedDirection, out int direction)
    {
        direction = proposedDirection;
        if (ability is not TumblePatrolAbility || ability.AbilityKey != "Bulwark.Patrol" || context.Controller == null) return false;
        var self = context.Controller;
        float size = ((TumblePatrolDataSO)ability.Data).tumbleSize;
        // 두 개체가 동시에 한 면씩 접근해도 몸체가 겹치기 전에 방향을 바꾼다.
        float lookAhead = size * 2f + separationPadding;
        Vector2 center = self.transform.position;
        int count = self.Motor.OverlapAttackBox(center, new Vector2(size + lookAhead * 2f, size), 0, hits, LayerMask.GetMask("Enemy"));
        EnemyController overlap = null;
        float closest = float.PositiveInfinity;
        bool ahead = false;
        for (int i = 0; i < count; i++)
        {
            var peer = hits[i] != null ? hits[i].GetComponentInParent<EnemyController>() : null;
            if (peer == null || peer == self || !peer.gameObject.activeInHierarchy) continue;
            var peerPatrol = peer.Brain.GetAbilityByKey(ability.AbilityKey) as TumblePatrolAbility;
            if (peerPatrol == null) continue;
            float peerSize = ((TumblePatrolDataSO)peerPatrol.Data).tumbleSize;
            Vector2 delta = (Vector2)peer.transform.position - center;
            float halfSpan = (size + peerSize) * .5f;
            if (Mathf.Abs(delta.y) >= halfSpan - .1f) continue;
            float distance = Mathf.Abs(delta.x);
            if (distance < halfSpan - .01f)
            {
                if (distance < closest || Mathf.Approximately(distance, closest) && (overlap == null || peer.GetInstanceID() < overlap.GetInstanceID()))
                { closest = distance; overlap = peer; }
            }
            else if (delta.x * proposedDirection > 0f && distance - halfSpan < lookAhead) ahead = true;
        }
        System.Array.Clear(hits, 0, count);
        if (overlap != null)
        {
            float deltaX = self.transform.position.x - overlap.transform.position.x;
            direction = Mathf.Abs(deltaX) > .01f ? (deltaX > 0f ? 1 : -1)
                : self.GetInstanceID() < overlap.GetInstanceID() ? -1 : 1;
        }
        else if (ahead) direction = -proposedDirection;
        return true;
    }
}
