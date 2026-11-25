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
        FixedMotionBody.Attach(transform);
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

    // 몸 바닥이 feet 높이에 놓인다고 보고 그 몸이 설 바닥을 찾는다(Wave 준비 중 크레인이 구속 상자를 내려놓을 높이).
    // feet보다 above만큼 위에서 아래로 above + below만큼 본다. 통과 발판은 윗면이 feet보다 높으면 바닥으로 보지 않는다
    // (그 자리에 선 몸은 발판 아래에 있다). 지금 몸이 어디에 떠 있는지와는 관계없다.
    public RaycastHit2D RaycastFloorBelow(Vector2 feet, float above, float below)
    {
        return RaycastMovement(feet + Vector2.up * above, Vector2.down, above + below, true, feet.y);
    }

    // 몸통을 지금 자리에서 offset만큼 옮긴 곳이 이동을 막는 지형과 겹치는지(통과 발판은 제외).
    // 구속 상자에 실려 지형을 지나던 몸을 그 자리에 풀어도 되는지, 옮겨 놓을 자리가 비어 있는지 볼 때 쓴다.
    // 바닥에 서 있거나 벽에 붙어 있는 것은 겹친 것이 아니다. 물리 질의는 맞닿은 도형도(접촉 여유만큼 떨어져 있어도) 겹쳤다고 알리므로 몸통을 조금 줄여서 본다.
    public bool IsBodyBlockedAt(Vector2 offset)
    {
        if (col == null) return false;
        // Transform을 방금 옮겼어도 지금 위치의 몸통을 읽는다.
        Physics2D.SyncTransforms();
        Bounds body = col.bounds;
        float inset = (skinWidth + .06f) * 2f;
        var size = new Vector2(Mathf.Max(.01f, body.size.x - inset), Mathf.Max(.01f, body.size.y - inset));
        var filter = new ContactFilter2D();
        filter.SetLayerMask(collisionLayer); filter.useTriggers = Physics2D.queriesHitTriggers;
        bodyOverlaps.Clear();
        Physics2D.OverlapBox((Vector2)body.center + offset, size, 0f, filter, bodyOverlaps);
        bool blocked = false;
        foreach (var other in bodyOverlaps)
        {
            if (other == null || other == col || OneWayPlatform.IsSurface(other)) continue;
            blocked = true;
            break;
        }
        bodyOverlaps.Clear();
        return blocked;
    }

    // 몸통을 Transform이 position에 있다고 보고 direction으로 distance만큼 쓸어 처음 막는 지형을 찾는다(없으면 collider가 null).
    // 몸통은 이동 광선이 나가는 상자와 같다(Collider의 AABB를 skin만큼 줄인 상자). 걸러내기도 이동(RaycastMovement)과 같다:
    // 이동 충돌 레이어만 막고, 통과 발판·움직이는 발판은 그 개체가 지원하고 아래로 쓸 때만 몸 바닥보다 낮은 것을 막는다. 자기 몸통은 건너뛴다.
    // 시작 자리에서 이미 겹친 지형은 거리 0으로 맞는다(distance가 0이면 그 자리의 겹침만 본다).
    // 물리 동기화(SyncTransforms)는 하지 않는다. 지형은 대부분 멈춰 있고, 움직이는 Prop은 한 물리 틱 늦게 보여도 되는 질의(비행 적 경로 찾기·순간이동 경로 검사)에 쓴다.
    // 이동·다른 질의는 이 함수를 쓰지 않으므로 기존 개체의 동작은 그대로다.
    public RaycastHit2D CastBody(Vector2 position, Vector2 direction, float distance)
    {
        if (col == null) return default;
        GetBodyBox(position, out Vector2 center, out Vector2 size);
        float inset = skinWidth * 2f;
        size = new Vector2(Mathf.Max(.01f, size.x - inset), Mathf.Max(.01f, size.y - inset));
        float length = direction.magnitude;
        direction = length > 1e-5f ? direction / length : Vector2.up;
        bool allowOneWay = direction.y < -.5f;
        int movingBit = MovingLayer;
        int moving = allowOneWay && SupportsMovingPlatforms ? movingBit : 0;
        int mask = collisionLayer.value | (allowOneWay && SupportsOneWayPlatforms ? OneWayLayer : 0) | moving;
        var filter = new ContactFilter2D();
        filter.SetLayerMask(mask); filter.useTriggers = Physics2D.queriesHitTriggers;
        movementHits.Clear();
        Physics2D.BoxCast(center, size, 0f, direction, filter, movementHits, Mathf.Max(distance, 1e-4f));
        // 통과 발판 규칙의 몸 바닥은 실제 몸통(줄이기 전) 바닥이다.
        float bodyBottom = center.y - size.y * .5f - skinWidth;
        RaycastHit2D nearest = default;
        float nearestDistance = float.PositiveInfinity;
        foreach (var hit in movementHits)
        {
            if (hit.collider == null || hit.collider == col) continue;
            if (OneWayPlatform.IsSurface(hit.collider))
            {
                if (!allowOneWay || !SupportsOneWayPlatforms || hit.collider == ignoredOneWay) continue;
                if (bodyBottom < hit.collider.bounds.max.y - skinWidth * 2f) continue;
            }
            else if ((mask & movingBit) != 0 && MovingPlatform.IsSurface(hit.collider))
            {
                if (moving == 0 || hit.collider == ignoredOneWay) continue;
                if (bodyBottom < hit.collider.bounds.max.y - skinWidth * 2f) continue;
            }
            else if ((collisionLayer.value & (1 << hit.collider.gameObject.layer)) == 0) continue;
            if (hit.distance >= nearestDistance) continue;
            nearest = hit; nearestDistance = hit.distance;
        }
        movementHits.Clear();
        return nearest;
    }

    // position에 놓인 몸통 상자(중심·크기). 원·상자·캡슐은 모양으로 계산해 꺼진 Collider나 물리 동기화 전의 Transform 이동도 반영한다.
    private void GetBodyBox(Vector2 position, out Vector2 center, out Vector2 size)
    {
        Transform body = col.transform;
        Vector3 lossy = body.lossyScale;
        float scaleX = Mathf.Abs(lossy.x), scaleY = Mathf.Abs(lossy.y);
        Vector2 local = (Vector2)body.TransformPoint(col.offset) - (Vector2)transform.position;
        switch (col)
        {
            case CircleCollider2D circle:
                float diameter = circle.radius * 2f * Mathf.Max(scaleX, scaleY);
                size = new Vector2(diameter, diameter);
                break;
            case BoxCollider2D box:
                size = new Vector2(box.size.x * scaleX, box.size.y * scaleY) + Vector2.one * (box.edgeRadius * 2f * Mathf.Max(scaleX, scaleY));
                break;
            case CapsuleCollider2D capsule:
                size = new Vector2(capsule.size.x * scaleX, capsule.size.y * scaleY);
                break;
            default:
                Bounds bounds = col.bounds;
                size = bounds.size;
                local = (Vector2)bounds.center - (Vector2)transform.position;
                break;
        }
        center = position + local;
    }

    private static readonly System.Collections.Generic.List<RaycastHit2D> environmentCastHits = new();
    private static readonly System.Collections.Generic.List<Collider2D> bodyOverlaps = new();
    private readonly System.Collections.Generic.List<RaycastHit2D> movementHits = new();
    private Collider2D groundCollider;
    private Collider2D ignoredOneWay;
    private float ignoreOneWayUntil;
    // 레이어 번호는 OneWayPlatform이 처음 한 번만 찾아 둔 값을 쓴다(광선마다 하던 NameToLayer 문자열 검색 제거, 값은 같다).
    private static int OneWayLayer => 1 << OneWayPlatform.SurfaceLayer;
    public bool SupportsOneWayPlatforms { get; set; }
    // 자기 몸통(col)에 맞은 이동 광선을 건너뛴다. 기본은 꺼짐이라 기존 개체의 동작은 그대로다.
    // 몸통이 충돌 레이어 안의 고체인 개체(던전 중력 Prop의 SolidMount)만 켠다: 광선이 몸통 안에서 시작해 거리 0으로 자기 몸통에 맞으면
    // 아래로 움직일 때도 위로 밀려 올라가므로, 자기 몸통을 빼고 그 아래의 받침만 본다.
    public bool IgnoreOwnBodyHits { get; set; }
    // 움직이는 발판(MovingPlatform 레이어) 지원. 켠 개체만 그 발판 위에 서고(통과 발판과 같은 규칙: 위에서만 서고 아래·옆에서는 지나간다)
    // 발판이 이번 물리 틱에 움직일 양만큼 함께 옮겨진다(운반 이동). 끈 개체(적·투사체)는 그 발판을 통과하고 이동 계산은 예전과 같다. 지금은 Player만 켠다.
    public bool SupportsMovingPlatforms { get; set; }
    // 레이어 번호는 MovingPlatform이 처음 한 번만 찾아 둔 값을 쓴다. 레이어가 없으면 0(아무 발판도 없는 것과 같다).
    private static int MovingLayer => MovingPlatform.SurfaceLayer >= 0 ? 1 << MovingPlatform.SurfaceLayer : 0;
    // 운반 이동을 계산하는 동안 건너뛰는 발판(운반하는 발판 자신). 계산이 끝나면 바로 지운다.
    private Collider2D ignoredCarrier;
    // 움직이는 발판에서 위로 뛰어오른 순간 캐릭터가 이어받을 발판 속도(CharacterController2D가 한 번 읽고 지운다).
    private Vector2 takeoffVelocity;
    private bool hasTakeoff;
    // 마지막 이동에서 찾은 바닥 Collider(물리 질의 없음). 움직이는 발판이 자기 위에 선 Player를 알 때 쓴다.
    public Collider2D GroundCollider => groundCollider;

    // 이동 구속: 켜져 있는 동안 몸통이 이 월드 사각형 밖으로 나가지 못한다.
    // 실제 Collider를 만들지 않고 이 모터의 이동량만 막으므로 탄환·적·다른 물리 질의에는 영향이 없다(Wave 준비 중 Player 구속 상자).
    private bool confined;
    private Rect confinement;
    private Vector2 confinedBodyOffset;   // 몸통 중심의 Transform 기준 위치
    private Vector2 confinedBodySize;
    private float confinedSide, confinedTop;
    // 구속 영역을 옮기라고 받아 둔 양. 다음 이동에서 영역과 몸통에 함께 더한다.
    private Vector2 confinementCarry;
    public bool IsConfined => confined;
    public Rect Confinement => confinement;
    // 구속 계산에 쓰는 지금 몸통 사각형(Transform 기준). 구속 상자 표시가 어느 벽에 닿았는지 볼 때 쓴다.
    public Rect ConfinedBody
    {
        get
        {
            Vector2 center = (Vector2)transform.position + confinedBodyOffset;
            return new Rect(center - confinedBodySize * 0.5f, confinedBodySize);
        }
    }

    // 지금 몸통 둘레로 구속 영역을 잡는다. side는 좌우 여유, top은 머리 위 여유다. 바닥은 넓히지 않아 그 자리(공중이어도)에 선다.
    public Rect ConfineAroundBody(float side, float top)
    {
        // Transform을 방금 옮겼어도(텔레포트) 지금 위치의 몸통을 읽는다. 이후 계산은 Transform 기준이라 물리 동기화를 기다리지 않는다.
        Physics2D.SyncTransforms();
        Bounds body = col.bounds;
        confinedBodyOffset = (Vector2)body.center - (Vector2)transform.position;
        confinedBodySize = body.size;
        confinement = Rect.MinMaxRect(body.min.x - Mathf.Max(0f, side), body.min.y, body.max.x + Mathf.Max(0f, side), body.max.y + Mathf.Max(0f, top));
        confinedSide = side; confinedTop = top;
        confinementCarry = Vector2.zero;
        confined = true;
        return confinement;
    }
    // 구속 영역을 통째로 옮긴다(Wave 준비 중 크레인이 구속 상자를 들어 옮긴다). 다음 이동에서 영역과 몸통이 같은 양만큼 함께 옮겨지므로
    // 영역 안에서의 위치·접지·움직임(점프·무장)은 그대로다. 지형 충돌보다 구속이 우선하므로 옮기는 동안에는 지형을 지나간다.
    public void ShiftConfinement(Vector2 delta)
    {
        if (confined) confinementCarry += delta;
    }
    // 구속된 채 다른 곳이 몸을 순간이동시켰을 때 구속 영역도 같은 여유로 그 자리에 다시 잡는다.
    public void RefitConfinement()
    {
        if (confined) ConfineAroundBody(confinedSide, confinedTop);
    }
    public void ClearConfinement()
    {
        confined = false;
        confinementCarry = Vector2.zero;
    }

    // 모든 투사체는 OneWay를 통과하고 일반 지형의 가장 가까운 차단점만 사용한다.
    public static RaycastHit2D CircleCastEnvironment(Vector2 origin, float radius, Vector2 direction,
        float distance, LayerMask blockLayer)
    {
        if ((blockLayer.value & OneWayLayer) == 0)
            return radius <= 0f ? Physics2D.Raycast(origin, direction, distance, blockLayer)
                : Physics2D.CircleCast(origin, radius, direction, distance, blockLayer);
        var filter = new ContactFilter2D();
        filter.SetLayerMask(blockLayer); filter.useTriggers = Physics2D.queriesHitTriggers;
        environmentCastHits.Clear();
        if (radius <= 0f) Physics2D.Raycast(origin, direction, filter, environmentCastHits, distance);
        else Physics2D.CircleCast(origin, radius, direction, filter, environmentCastHits, distance);
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
        // 순간이동하면 통과 발판 무시와 함께 운반 상태(서 있던 움직이는 발판)도 지운다. 다음 이동에서 바닥을 다시 찾는다.
        groundCollider = ignoredOneWay = ignoredCarrier = null;
        ignoreOneWayUntil = 0f;
        takeoffVelocity = Vector2.zero;
        hasTakeoff = false;
    }

    public bool TryDropThroughOneWay()
    {
        // 움직이는 발판도 통과 발판처럼 아래 + 점프로 내려간다(지원하는 개체만).
        if (groundCollider == null) return false;
        bool oneWay = SupportsOneWayPlatforms && OneWayPlatform.IsSurface(groundCollider);
        if (!oneWay && !(SupportsMovingPlatforms && MovingPlatform.IsSurface(groundCollider))) return false;
        // 움직이는 발판 바로 아래에 단단한 바닥이 맞닿아 있으면(승강기 아래 정지점·출입구 바닥에 걸쳐 섬) 내려갈 곳이 없으므로
        // 하차로 보지 않는다: 점프 입력이 사라지지 않고 보통 점프로 나간다.
        if (!oneWay && HasSolidSupportBelow()) return false;
        ignoredOneWay = groundCollider;
        ignoreOneWayUntil = Time.time + .4f;
        groundCollider = null;
        return true;
    }

    // bodyBottom을 주면 통과 발판을 막을지 정할 때 몸 바닥이 그 높이에 있는 것으로 본다(주지 않으면 지금 몸통).
    private RaycastHit2D RaycastMovement(Vector2 origin, Vector2 direction, float distance, bool allowOneWay, float bodyBottom = float.NaN)
    {
        int movingBit = MovingLayer;
        int moving = allowOneWay && SupportsMovingPlatforms ? movingBit : 0;
        int mask = collisionLayer.value | (allowOneWay && SupportsOneWayPlatforms ? OneWayLayer : 0) | moving;
        var filter = new ContactFilter2D();
        filter.SetLayerMask(mask); filter.useTriggers = Physics2D.queriesHitTriggers;
        movementHits.Clear();
        Physics2D.Raycast(origin, direction, filter, movementHits, distance);
        RaycastHit2D nearest = default;
        float nearestDistance = float.PositiveInfinity;
        foreach (var hit in movementHits)
        {
            if (hit.collider == null || (IgnoreOwnBodyHits && hit.collider == col)) continue;
            bool oneWay = OneWayPlatform.IsSurface(hit.collider);
            if (oneWay)
            {
                if (!allowOneWay || !SupportsOneWayPlatforms || hit.collider == ignoredOneWay) continue;
                if (col == null || (float.IsNaN(bodyBottom) ? col.bounds.min.y : bodyBottom) < hit.collider.bounds.max.y - skinWidth * 2f) continue;
            }
            // 움직이는 발판: 지원하는 개체의 아래 방향 광선에서만 통과 발판과 같은 규칙으로 막는다. 지원하지 않으면 늘 통과한다
            // (이동 충돌 레이어에 그 레이어가 들어 있어도). 마스크에 그 레이어가 없으면 이 검사 자체를 하지 않는다.
            else if ((mask & movingBit) != 0 && MovingPlatform.IsSurface(hit.collider))
            {
                if (moving == 0 || hit.collider == ignoredOneWay || hit.collider == ignoredCarrier) continue;
                if (col == null || (float.IsNaN(bodyBottom) ? col.bounds.min.y : bodyBottom) < hit.collider.bounds.max.y - skinWidth * 2f) continue;
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

    // 몸통 Collider의 실제 형상을 재사용한다. 별도 Rigidbody나 충돌 모터는 만들지 않는다.
    public int OverlapAttackCollider(Collider2D source, ContactFilter2D filter,
        System.Collections.Generic.List<Collider2D> results)
    {
        PrepareAttackQueries();
        results.Clear();
        return source.Overlap(filter, results);
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
        // 지난 이동에서 서 있던 바닥(움직이는 발판에서 뛰어오른 틱을 알기 위해 지우기 전에 둔다).
        Collider2D previousGround = groundCollider;
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

        // 공중에서 움직이는 발판으로 내려앉기: 발판은 이번 틱 끝(순서 100)에 PendingDelta만큼 움직이므로 발판 기준 상대 이동으로 다시 본다
        // (올라오는 승강기로 떨어지거나, 올라가는 승강기에서 뛰었다가 내려앉을 때 발판을 뚫지 않게). 막 뛰어오른 그 발판은 이번 틱에 보지 않는다.
        if (SupportsMovingPlatforms && !confined && groundCollider == null)
            TryLandOnMovingPlatform(ref move, ref collisionInfo, desiredMove.y > 0f ? previousGround : null);

        if (confined)
        {
            ApplyConfinement(ref move, ref collisionInfo);
            if (confinementCarry != Vector2.zero)
            {
                // 영역이 서 있는 것처럼 구속을 적용한 뒤 영역과 몸통을 함께 옮긴다.
                confinement.position += confinementCarry;
                move += confinementCarry;
                confinementCarry = Vector2.zero;
            }
        }
        // 움직이는 발판 위에 섰으면 발판이 이번 틱에 움직일 양을 더한다. 구속 중에는 구속이 우선이라 운반하지 않는다.
        else if (SupportsMovingPlatforms && groundCollider != null)
            ApplyPlatformCarry(ref move, ref collisionInfo);
        // 움직이는 발판에서 위로 뛰어오른 틱: 발판이 이번 틱에 오를 양을 함께 더하고, 발판의 위쪽 속도를 캐릭터가 이어받게 알린다.
        // 이어받지 않으면 점프 속도보다 빨리 오르는 승강기에 곧바로 다시 붙잡혀 내릴 수 없다. 내려가는 발판의 속도는 이어받지 않는다.
        else if (SupportsMovingPlatforms && previousGround != null && desiredMove.y > 0f && groundCollider == null
                 && MovingPlatform.IsSurface(previousGround) && MovingPlatform.TryGet(previousGround, out var leftPlatform)
                 && leftPlatform.Velocity.y > 0f)
        {
            move += TakeoffCarry(previousGround, new Vector2(0f, Mathf.Max(0f, leftPlatform.PendingDelta.y)), move);
            takeoffVelocity = leftPlatform.Velocity;
            hasTakeoff = true;
        }

        return new RaycastMoveResult2D
        {
            move = move,
            info = collisionInfo
        };
    }

    // 지형 충돌을 처리한 이동량을 구속 영역 안으로 다시 줄인다. 막힌 면은 실제 벽·바닥·천장과 같은 충돌 정보로 알린다.
    private void ApplyConfinement(ref Vector2 move, ref RaycastCollisionInfo2D info)
    {
        Vector2 center = (Vector2)transform.position + confinedBodyOffset;
        Vector2 min = center - confinedBodySize * 0.5f;
        Vector2 max = center + confinedBodySize * 0.5f;

        // 영역이 몸통보다 좁은 축은 가운데에 맞춘다.
        float left = confinement.xMin - min.x;
        float right = confinement.xMax - max.x;
        if (left > right) left = right = (left + right) * 0.5f;

        if (move.x < left) { move.x = left; info.left = true; }
        else if (move.x > right) { move.x = right; info.right = true; }

        float down = confinement.yMin - min.y;
        float up = confinement.yMax - max.y;
        if (down > up) down = up = (down + up) * 0.5f;

        if (move.y < down) { move.y = down; info.down = true; }
        else if (move.y > up) { move.y = up; info.up = true; }
        // 구속 바닥에 닿아 서 있으면 실제 바닥처럼 접지로 본다(공중에서 갇혀도 그 자리에 선다).
        else if (move.y <= 0f && move.y - down <= skinWidth + 0.001f) info.down = true;
    }

    // 운반 이동: 서 있는 움직이는 발판(MovingPlatform)이 이번 물리 틱에 움직일 양(PendingDelta)을 자기 이동 뒤에 더한다.
    // 발판은 계획(순서 -100) → 캐릭터 이동(순서 0) → 적용(순서 100)으로 움직이므로 여기서 보는 발판 Collider는 늘 틱 시작 자리다.
    // 운반 양도 일반 이동과 같은 광선으로 벽·천장·아래 지형에 막히는지 본다(운반하는 발판 자신은 제외).
    // 천장에 막히면 덜 올라가고 발판은 계속 올라가므로 몸은 발판 아래로 빠진다(위에서만 서는 발판이라 끼지 않는다).
    // 몸 바로 아래(skin 두 배 안)에 이동 충돌 레이어의 고체가 있는지(통과 발판·움직이는 발판 제외).
    private bool HasSolidSupportBelow()
    {
        CalculateRaySpacing();
        for (int i = 0; i < verticalRayCount; i++)
        {
            Vector2 origin = new Vector2(bounds.min.x + verticalRaySpacing * i, bounds.min.y);
            if (RaycastMovement(origin, Vector2.down, skinWidth * 2f + .02f, false)) return true;
        }
        return false;
    }

    // 발판이 한 물리 틱에 움직일 수 있는 양의 여유(승강기 최고 14칸/초 × 0.01초 = 0.14보다 넉넉히).
    private const float MovingPlatformReach = .3f;

    // 공중일 때 아래의 움직이는 발판을 발판 기준 상대 이동으로 본다. 이번 틱에 몸 바닥이 발판 윗면(이동 뒤)보다 내려가게 되면 그 윗면에 세운다:
    // 자기 이동은 지금 간격만큼(move.y = -간격), 발판 이동은 뒤이은 운반(ApplyPlatformCarry)이 더한다.
    private void TryLandOnMovingPlatform(ref Vector2 move, ref RaycastCollisionInfo2D info, Collider2D skip)
    {
        int movingBit = MovingLayer;
        if (movingBit == 0 || col == null) return;
        float reach = Mathf.Max(0f, -move.y) + skinWidth + MovingPlatformReach;
        var filter = new ContactFilter2D();
        filter.SetLayerMask(movingBit); filter.useTriggers = Physics2D.queriesHitTriggers;
        float bodyBottom = col.bounds.min.y;
        for (int i = 0; i < verticalRayCount; i++)
        {
            Vector2 origin = new Vector2(bounds.min.x + verticalRaySpacing * i + move.x, bounds.min.y);
            movementHits.Clear();
            Physics2D.Raycast(origin, Vector2.down, filter, movementHits, reach);
            foreach (var hit in movementHits)
            {
                var surface = hit.collider;
                if (surface == null || surface == skip || surface == ignoredOneWay || !MovingPlatform.IsSurface(surface)) continue;
                if (!MovingPlatform.TryGet(surface, out var platform)) continue;
                // 몸 바닥이 이미 발판 윗면 아래면(지나침) 통과 발판 규칙대로 건너뛴다.
                if (bodyBottom < surface.bounds.max.y - skinWidth * 2f) continue;
                float gap = Mathf.Max(0f, hit.distance - skinWidth);
                float relative = move.y - platform.PendingDelta.y;
                if (relative >= 0f || -relative < gap) continue;
                move.y = -gap;
                info.down = true;
                groundCollider = surface;
                return;
            }
        }
    }

    /// <summary>움직이는 발판에서 뛰어오른 순간 이어받을 발판 속도를 한 번 꺼낸다(없으면 false). CharacterController2D가 이동 직후 부른다.</summary>
    public bool TryConsumeTakeoffVelocity(out Vector2 velocity)
    {
        velocity = takeoffVelocity;
        bool had = hasTakeoff;
        hasTakeoff = false;
        takeoffVelocity = Vector2.zero;
        return had;
    }

    // 뛰어오른 틱의 추가 이동(발판이 이번 틱에 오를 양). 천장에 막히는지 본다(떠난 발판 자신은 제외).
    private Vector2 TakeoffCarry(Collider2D carrier, Vector2 carry, Vector2 move)
    {
        if (carry == Vector2.zero) return carry;
        Bounds start = bounds;
        bounds.center += (Vector3)move;
        var carryInfo = new RaycastCollisionInfo2D();
        ignoredCarrier = carrier;
        try { VerticalCollisions(ref carry, ref carryInfo); }
        finally
        {
            ignoredCarrier = null;
            bounds = start;
        }
        return carry;
    }

    private void ApplyPlatformCarry(ref Vector2 move, ref RaycastCollisionInfo2D info)
    {
        Collider2D carrier = groundCollider;
        if (!MovingPlatform.IsSurface(carrier) || !MovingPlatform.TryGet(carrier, out var platform)) return;
        Vector2 carry = platform.PendingDelta;
        if (carry == Vector2.zero) return;
        Bounds start = bounds;
        // 자기 이동을 마친 몸 자리에서 광선을 쏜다.
        bounds.center += (Vector3)move;
        var carryInfo = new RaycastCollisionInfo2D();
        ignoredCarrier = carrier;
        try
        {
            if (carry.x != 0) HorizontalCollisions(ref carry, ref carryInfo);
            if (carry.y != 0) VerticalCollisions(ref carry, ref carryInfo);
        }
        finally
        {
            ignoredCarrier = null;
            bounds = start;
        }
        move += carry;
        // 운반되는 동안에도 발판 위에 선 것이다. 내려가다 아래 지형에 닿았으면 VerticalCollisions가 그 지형을 새 바닥으로 바꿔 둔다.
        info.down = true;
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

