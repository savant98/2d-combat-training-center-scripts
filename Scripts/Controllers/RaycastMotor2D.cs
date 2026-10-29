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
        return Physics2D.Raycast(origin, direction, distance, collisionLayer);
    }

    public float ClampHeightAboveFloor(Vector2 referencePosition, float maxDistance)
    {
        var floor = RaycastEnvironment(referencePosition + Vector2.up * 0.1f, Vector2.down, maxDistance);
        if (floor.collider == null || col == null) return referencePosition.y;
        float bottomOffset = col.bounds.min.y - transform.position.y;
        return Mathf.Max(referencePosition.y, floor.point.y - bottomOffset + skinWidth + 0.01f);
    }

    public int OverlapAttackBox(Vector2 center, Vector2 size, float angle,
        Collider2D[] results, LayerMask targetLayer)
    {
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
        CalculateRaySpacing();

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
            if (Physics2D.Raycast(origin, Vector2.down, distance, collisionLayer))
                return true;
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

            RaycastHit2D hit = Physics2D.Raycast(
                rayOrigin,
                Vector2.right * direction,
                distance,
                collisionLayer);

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

            RaycastHit2D hit = Physics2D.Raycast(
                rayOrigin,
                Vector2.up * direction,
                distance,
                collisionLayer);

            if (!hit) continue;

            move.y = (hit.distance - skinWidth) * direction;

            if (direction == 1) info.up = true;
            else info.down = true;

            break;
        }
    }

}

