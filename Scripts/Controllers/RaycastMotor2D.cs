using UnityEngine;
public struct RaycastCollisionInfo2D
{
    public bool up;
    public bool down;
    public bool left;
    public bool right;

    public void Reset()
    {
        up = down = left = right = false;
    }
}
public struct RaycastMoveResult2D
{
    public Vector2 move;                // 보정된 이동값
    public RaycastCollisionInfo2D info; // 충돌 정보
}
//https://www.youtube.com/watch?v=vfpnyz1a_no Raycast 충돌처리 참조
public class RaycastMotor2D : MonoBehaviour
{
    // 설정
    private Collider2D col;
    private LayerMask collisionLayer;
    private float skinWidth;
    private int horizontalRayCount;
    private int verticalRayCount;

    // 내부 상태
    private Bounds bounds;
    private float horizontalRaySpacing;
    private float verticalRaySpacing;

    public RaycastCollisionInfo2D collisionInfo;

    public void SetRaycastMotor2D(Collider2D col, LayerMask collisionLayer, float skinWidth, int horizontalRayCount, int verticalRayCount)
    {
        this.col = col;
        this.collisionLayer = collisionLayer;
        this.skinWidth = skinWidth;
        this.horizontalRayCount = horizontalRayCount;
        this.verticalRayCount = verticalRayCount;
    }
    public void SetCollideLayer(LayerMask collisionLayer)
    {
        this.collisionLayer = collisionLayer;
    }
    // Shared environment and combat queries use the same motor as character movement.
    public RaycastHit2D RaycastEnvironment(Vector2 origin, Vector2 direction, float distance)
    {
        return RaycastMovement(origin, direction, distance, direction.y < -.5f);
    }

    // 이동 충돌 레이어와 다른 지형만 막는 공격(Player 레이저는 통과 발판을 지나간다)이 쓰는 벽 찾기.
    public RaycastHit2D RaycastEnvironment(Vector2 origin, Vector2 direction, float distance, LayerMask blockLayer)
    {
        return CircleCastEnvironment(origin, 0f, direction, distance, blockLayer);
    }

    private static readonly System.Collections.Generic.List<RaycastHit2D> environmentCastHits = new();
    private readonly System.Collections.Generic.List<RaycastHit2D> movementHits = new();
    private Collider2D groundCollider;
    private Collider2D ignoredOneWay;
    private float ignoreOneWayUntil;
    private static int OneWayLayer => 1 << LayerMask.NameToLayer("TransparentPlatform");
    public bool SupportsOneWayPlatforms { get; set; }

    // 모든 투사체는 OneWay를 통과하고 일반 지형의 가장 가까운 차단점만 사용한다.
    public static RaycastHit2D CircleCastEnvironment(Vector2 origin, float radius, Vector2 direction,
        float distance, LayerMask blockLayer)
    {
        if ((blockLayer.value & OneWayLayer) == 0)
            return Physics2D.CircleCast(origin, radius, direction, distance, blockLayer);
        var filter = new ContactFilter2D();
        filter.SetLayerMask(blockLayer); filter.useTriggers = Physics2D.queriesHitTriggers;
        environmentCastHits.Clear();
        Physics2D.CircleCast(origin, radius, direction, filter, environmentCastHits, distance);
        RaycastHit2D nearest = default;
        float nearestDistance = float.PositiveInfinity;
        foreach (var hit in environmentCastHits)
        {
            if (hit.collider == null || OneWayPlatform.IsSurface(hit.collider)) continue;
            if (hit.distance >= nearestDistance) continue;
            nearest = hit; nearestDistance = hit.distance;
        }
        environmentCastHits.Clear();
        return nearest;
    }

    public void ResetOneWayState()
    {
        groundCollider = ignoredOneWay = null;
        ignoreOneWayUntil = 0f;
    }

    public bool TryDropThroughOneWay()
    {
        if (!SupportsOneWayPlatforms || groundCollider == null
            || !OneWayPlatform.IsSurface(groundCollider)) return false;
        ignoredOneWay = groundCollider;
        ignoreOneWayUntil = Time.time + .4f;
        groundCollider = null;
        return true;
    }

    private RaycastHit2D RaycastMovement(Vector2 origin, Vector2 direction, float distance, bool allowOneWay)
    {
        int mask = collisionLayer.value | (allowOneWay && SupportsOneWayPlatforms ? OneWayLayer : 0);
        var filter = new ContactFilter2D();
        filter.SetLayerMask(mask); filter.useTriggers = Physics2D.queriesHitTriggers;
        movementHits.Clear();
        Physics2D.Raycast(origin, direction, filter, movementHits, distance);
        RaycastHit2D nearest = default;
        float nearestDistance = float.PositiveInfinity;
        foreach (var hit in movementHits)
        {
            if (hit.collider == null) continue;
            bool oneWay = OneWayPlatform.IsSurface(hit.collider);
            if (oneWay)
            {
                if (!allowOneWay || !SupportsOneWayPlatforms || hit.collider == ignoredOneWay) continue;
                if (col == null || col.bounds.min.y < hit.collider.bounds.max.y - skinWidth * 2f) continue;
            }
            else if ((collisionLayer.value & (1 << hit.collider.gameObject.layer)) == 0) continue;
            if (hit.distance >= nearestDistance) continue;
            nearest = hit; nearestDistance = hit.distance;
        }
        movementHits.Clear();
        return nearest;
    }

    public float ClampHeightAboveFloor(Vector2 referencePosition, float maxDistance)
    {
        var floor = RaycastEnvironment(referencePosition + Vector2.up * 0.1f, Vector2.down, maxDistance);
        if (floor.collider == null || col == null) return referencePosition.y;
        float bottomOffset = col.bounds.min.y - transform.position.y;
        return Mathf.Max(referencePosition.y, floor.point.y - bottomOffset + skinWidth + 0.01f);
    }

    // 바닥을 순찰하는 적이 발판 끝에서 돌아서도록, 진행 방향 쪽 몸 끝에서 lookAhead만큼 앞의 발밑에 바닥이 있는지 확인한다.
    // 광선은 몸 바닥 바로 위에서 depth만큼 아래로 쏜다. 앞이 벽이라 광선이 벽 속에서 시작하면 바닥이 있는 것으로 본다.
    public bool HasFloorAhead(int direction, float lookAhead, float depth)
    {
        if (col == null) return true;
        Bounds bodyBounds = col.bounds;
        float x = direction >= 0 ? bodyBounds.max.x + lookAhead : bodyBounds.min.x - lookAhead;
        float startY = bodyBounds.min.y + skinWidth * 2f;
        return RaycastEnvironment(new Vector2(x, startY), Vector2.down, depth + skinWidth * 2f).collider != null;
    }

    public int OverlapAttackBox(Vector2 center, Vector2 size, float angle,
        Collider2D[] results, LayerMask targetLayer)
    {
        PrepareAttackQueries();
        var filter = new ContactFilter2D();
        filter.SetLayerMask(targetLayer);
        filter.useTriggers = true;
        return Physics2D.OverlapBox(center, size, angle, filter, results);
    }

    // 범위 공격도 기존 모터에서 동기화한 물리 상태를 사용한다.
    public int OverlapAttackCircle(Vector2 center, float radius, ContactFilter2D filter,
        System.Collections.Generic.List<Collider2D> results)
    {
        PrepareAttackQueries();
        results.Clear();
        return Physics2D.OverlapCircle(center, Mathf.Max(0f, radius), filter, results);
    }

    private static double lastAttackQuerySyncTime = double.NaN;

    // 캐릭터 이동 후 실행되는 ProjectileBase에서 물리 틱당 한 번만 동기화한다.
    public static void PrepareAttackQueries()
    {
        double fixedTime = Time.fixedTimeAsDouble;
        if (lastAttackQuerySyncTime == fixedTime) return;
        Physics2D.SyncTransforms();
        lastAttackQuerySyncTime = fixedTime;
    }

    // 같은 틱의 판정 도중 새 탄이 배치된 경우에도 시작 위치를 반영한다.
    public static void InvalidateAttackQuerySync()
    {
        lastAttackQuerySyncTime = double.NaN;
    }

    // position·angle은 이 Collider가 연결된 기존 Rigidbody2D의 월드 배치다.
    // 실제 Collider 형상과 오프셋을 사용하며 결과는 진행 거리순으로 반환된다.
    public int CastAttackPath(Vector2 position, float angle, Vector2 move,
        ContactFilter2D filter, System.Collections.Generic.List<RaycastHit2D> results)
    {
        float distance = move.magnitude;
        if (distance <= 0f)
        {
            results.Clear();
            return 0;
        }
        return col.Cast(position, angle, move / distance, filter, results, distance, true);
    }

    public int OverlapAttackCollider(Vector2 position, float angle,
        ContactFilter2D filter, System.Collections.Generic.List<Collider2D> results)
    {
        return col.Overlap(position, angle, filter, results);
    }

    private void CalculateRaySpacing()
    {
        bounds = col.bounds;
        bounds.Expand(skinWidth * -2);

        horizontalRaySpacing = bounds.size.y / (horizontalRayCount - 1);
        verticalRaySpacing = bounds.size.x / (verticalRayCount - 1);
    }
    public RaycastMoveResult2D Move(Vector2 desiredMove)
    {
        if (ignoredOneWay != null && (Time.time >= ignoreOneWayUntil || !ignoredOneWay.enabled
            || !ignoredOneWay.gameObject.activeInHierarchy || col.bounds.max.y < ignoredOneWay.bounds.min.y))
            ignoredOneWay = null;
        CalculateRaySpacing();
        groundCollider = null;

        RaycastCollisionInfo2D collisionInfo = new();
        collisionInfo.Reset();

        Vector2 move = desiredMove;

        if (move.x != 0)
            HorizontalCollisions(ref move, ref collisionInfo);

        if (move.y != 0)
            VerticalCollisions(ref move, ref collisionInfo);
        else
            collisionInfo.down = HasGroundSupport(move.x);

        return new RaycastMoveResult2D
        {
            move = move,
            info = collisionInfo
        };
    }

    private bool HasGroundSupport(float horizontalMove)
    {
        // 수평 대시나 정지 중에도 이동 후 발밑을 확인해 실제 발판 이탈만 알린다.
        // 위치는 보정하지 않고 기존 바닥 충돌과 같은 레이 간격·레이어를 사용한다.
        float distance = skinWidth + 0.001f;
        for (int i = 0; i < verticalRayCount; i++)
        {
            Vector2 origin = new Vector2(bounds.min.x + verticalRaySpacing * i + horizontalMove, bounds.min.y);
            var hit = RaycastMovement(origin, Vector2.down, distance, true);
            if (hit.collider != null) { groundCollider = hit.collider; return true; }
        }
        return false;
    }

    private void HorizontalCollisions(ref Vector2 move, ref RaycastCollisionInfo2D info)
    {
        float direction = Mathf.Sign(move.x);
        float distance = Mathf.Abs(move.x) + skinWidth;

        for (int i = 0; i < horizontalRayCount; i++)
        {
            Vector2 rayOrigin = (direction == 1)
                ? new Vector2(bounds.max.x, bounds.min.y)
                : new Vector2(bounds.min.x, bounds.min.y);

            rayOrigin += Vector2.up * (horizontalRaySpacing * i);

            RaycastHit2D hit = RaycastMovement(rayOrigin, Vector2.right * direction, distance, false);

            if (!hit) continue;

            move.x = (hit.distance - skinWidth) * direction;
            //distance = hit.distance;

            if (direction == -1) info.left = true;
            else info.right = true;

            break;
        }
    }

    private void VerticalCollisions(ref Vector2 move, ref RaycastCollisionInfo2D info)
    {
        float direction = Mathf.Sign(move.y);
        float distance = Mathf.Abs(move.y) + skinWidth;

        for (int i = 0; i < verticalRayCount; i++)
        {
            Vector2 rayOrigin = (direction == 1)
                ? new Vector2(bounds.min.x, bounds.max.y)
                : new Vector2(bounds.min.x, bounds.min.y);

            rayOrigin += Vector2.right * (verticalRaySpacing * i + move.x);

            RaycastHit2D hit = RaycastMovement(rayOrigin, Vector2.up * direction, distance, direction < 0f);

            if (!hit) continue;

            move.y = (hit.distance - skinWidth) * direction;

            if (direction == 1) info.up = true;
            else { info.down = true; groundCollider = hit.collider; }

            break;
        }
    }

}

