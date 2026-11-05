using System.Collections.Generic;
using UnityEngine;

// 무리 안에서 구성원이 맡은 역할이다.
public enum EnemyFlockRole
{
    None,       // 등장 중이거나 정지한 상태다. 대형과 분리 계산에서 빠진다.
    Formation,  // 거리 유지 대형에 참여한다.
    Chase       // 플레이어에게 접근 중이다. 대형에서는 빠지지만 다른 구성원과는 계속 서로 밀어낸다.
}

// 같은 무리(EnemyFlockMember)의 좌우 전환, 거리 유지 대형, 분리를 계산한다. 이동과 물리 질의는 하지 않는다.
// - 대형: 거리 유지 중인 구성원 가운데 무리 중심에 가장 가까운 구성원이 기준 구성원(Leader)이 되어 유지 지점으로 간다.
//   나머지는 기준 구성원을 둘러싼 고리 칸으로 가므로 무리가 기준 구성원을 따라 함께 움직인다(Reynolds의 Leader Following).
//   기준 구성원은 대형을 떠날 때(추적·비활성화)까지 유지한다. 이동 중에는 뒤따르는 구성원 쪽으로 무리 중심이 밀리므로
//   중심 거리로 계속 다시 고르면 기준과 칸이 바뀌며 구성원끼리 엇갈린다.
// - 칸 배정: 기준 구성원에 가까운 구성원부터 안쪽 고리에 넣고, 고리 안에서는 둘레의 각도 순서를 지켜 서로 엇갈려 지나가지 않게 한다.
// - 분리: 정해진 거리보다 가까운 구성원끼리 가까울수록 강하게 밀어내고(Reynolds Boids의 Separation, 1/거리 가중),
//   밀어내는 방향을 조금 돌려 정면으로 마주친 구성원이 옆으로 비켜 가게 한다.
// 대형 계산은 프레임마다 한 번만 하고, 칸 배정은 구성원·기준 구성원이 바뀌거나 일정 시간이 지났을 때만 다시 한다.
public sealed class EnemyFlock
{
    // 구성이 그대로여도 이 간격(초)마다 칸 배정을 다시 해 넉백 등으로 생긴 어긋남을 바로잡는다.
    private const float ReassignInterval = 1.5f;
    // 분리 방향을 모든 구성원이 반시계 방향으로 돌리는 기본 각도(도)다. 정면으로 마주친 두 구성원이 서로 반대쪽으로 비켜 간다.
    // 비행 데이터(FlyingAbilityDataSO.separationSidestep)가 적마다 정하며, 0이면 밀어내기만 한다(Drone).
    private const float DefaultSidestepAngle = 25f;

    private static readonly Dictionary<string, EnemyFlock> flocks = new();

    // Domain Reload를 끈 Play 반복에서도 이전 실행의 무리가 남지 않게 한다.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetFlocks() => flocks.Clear();

    public static EnemyFlock Join(string flockId, EnemyFlockMember member)
    {
        string key = flockId ?? string.Empty;
        if (!flocks.TryGetValue(key, out EnemyFlock flock))
        {
            flock = new EnemyFlock();
            flocks.Add(key, flock);
        }

        flock.Add(member);
        return flock;
    }

    private readonly List<EnemyFlockMember> members = new();
    private readonly List<EnemyFlockMember> formation = new();
    private readonly List<EnemyFlockMember> followers = new();
    private readonly List<EnemyFlockMember> ringGroup = new();
    private readonly List<Vector2> ringOffsets = new();
    private readonly List<int> ringSizes = new();
    private readonly Dictionary<EnemyFlockMember, Vector2> slotOffsets = new();

    private EnemyFlockMember leader;
    private bool layoutDirty = true;
    private int solvedFrame = -1;
    private float nextReassignTime;
    private float solvedSpacing = -1f;
    private float solvedHeightRatio = -1f;

    private int side;
    private float nextSideSwitchTime;

    private Vector2 holdPoint;
    private int holdPointFrame = -1;

    public int MemberCount => members.Count;
    public EnemyFlockMember Leader => leader;
    // 무리가 현재 유지하는 쪽이다. -1은 플레이어 왼쪽, 1은 오른쪽, 0은 아직 정하지 않음이다.
    public int Side => side;

    private void Add(EnemyFlockMember member)
    {
        if (member == null || members.Contains(member))
            return;

        members.Add(member);
        layoutDirty = true;
    }

    public void Leave(EnemyFlockMember member)
    {
        members.Remove(member);
        slotOffsets.Remove(member);
        if (leader == member)
            leader = null;
        layoutDirty = true;

        // 무리가 비면 다음에 생성되는 무리는 처음부터 좌우를 다시 정한다.
        if (members.Count == 0)
        {
            side = 0;
            holdPointFrame = -1;
        }
    }

    public void MarkLayoutDirty() => layoutDirty = true;

    public int CountRole(EnemyFlockRole role)
    {
        int count = 0;
        foreach (EnemyFlockMember member in members)
        {
            if (member != null && member.Role == role)
                count++;
        }
        return count;
    }

    // 무리 전체가 함께 쓰는 좌우 방향이다. 처음 요청한 구성원이 있는 쪽에서 시작하고 interval(초)마다 반대로 바꾼다.
    public int ResolveSide(int preferredSide, float interval)
    {
        float now = Time.time;
        if (side == 0)
        {
            side = preferredSide < 0 ? -1 : 1;
            nextSideSwitchTime = now + interval;
        }
        else if (now >= nextSideSwitchTime)
        {
            side = -side;
            nextSideSwitchTime = now + Mathf.Max(0.1f, interval);
        }
        return side;
    }

    // 같은 프레임에 다른 구성원이 이미 계산한 유지 지점을 다시 쓴다.
    public bool TryGetHoldPoint(out Vector2 point)
    {
        point = holdPoint;
        return holdPointFrame == Time.frameCount;
    }

    public void SetHoldPoint(Vector2 point)
    {
        holdPoint = point;
        holdPointFrame = Time.frameCount;
    }

    // 대형에서 이 구성원이 갈 지점이다. 기준 구성원은 유지 지점, 나머지는 기준 구성원의 현재 위치 + 배정된 고리 칸이다.
    public Vector2 GetFormationPoint(EnemyFlockMember member, Vector2 holdPoint, float spacing, float heightRatio)
    {
        Solve(spacing, heightRatio);
        if (member == null || leader == null || member == leader)
            return holdPoint;

        return slotOffsets.TryGetValue(member, out Vector2 offset) ? leader.Position + offset : holdPoint;
    }

    // member를 가까운 구성원에게서 밀어내는 속도(칸/초)다. distance 이상 떨어지면 0이고, 가까울수록 1/거리 비율로 커진다.
    // 거리가 distance의 절반이면 strength, 4분의 1이면 strength의 3배이며, 그보다 가까우면 4배까지 늘어난다.
    // 방향은 sidestepAngle만큼 돌려 정면으로 마주쳐도 멈춰 서지 않고 옆으로 비켜 지나가게 한다.
    public Vector2 GetSeparation(EnemyFlockMember member, float distance, float strength, float sidestepAngle = DefaultSidestepAngle)
    {
        if (member == null || distance <= 0f || strength <= 0f)
            return Vector2.zero;

        Vector2 position = member.Position;
        Vector2 push = Vector2.zero;
        foreach (EnemyFlockMember other in members)
        {
            if (other == null || other == member || other.Role == EnemyFlockRole.None)
                continue;

            Vector2 away = position - other.Position;
            float gap = away.magnitude;
            if (gap >= distance)
                continue;

            // 같은 자리에 겹쳤으면 생성 순서로 좌우를 정해 두 구성원이 같은 방향으로 밀리지 않게 한다.
            Vector2 direction = gap > 0.0001f
                ? away / gap
                : (member.GetInstanceID() > other.GetInstanceID() ? Vector2.right : Vector2.left);
            float ratio = gap / distance;
            push += direction * ((1f - ratio) / Mathf.Max(ratio, 0.25f));
        }

        float angle = sidestepAngle * Mathf.Deg2Rad, cos = Mathf.Cos(angle), sin = Mathf.Sin(angle);
        Vector2 sidestep = new(push.x * cos - push.y * sin, push.x * sin + push.y * cos);
        return sidestep * strength;
    }

    private void Solve(float spacing, float heightRatio)
    {
        int frame = Time.frameCount;
        if (solvedFrame == frame)
            return;
        solvedFrame = frame;

        formation.Clear();
        foreach (EnemyFlockMember member in members)
        {
            if (member != null && member.Role == EnemyFlockRole.Formation)
                formation.Add(member);
        }

        if (formation.Count == 0)
        {
            leader = null;
            slotOffsets.Clear();
            return;
        }

        EnemyFlockMember nextLeader = PickLeader();
        bool reassign = layoutDirty || nextLeader != leader || Time.time >= nextReassignTime
            || !Mathf.Approximately(spacing, solvedSpacing) || !Mathf.Approximately(heightRatio, solvedHeightRatio);
        leader = nextLeader;
        if (!reassign)
            return;

        layoutDirty = false;
        solvedSpacing = spacing;
        solvedHeightRatio = heightRatio;
        nextReassignTime = Time.time + ReassignInterval;
        AssignSlots(spacing, heightRatio);
    }

    // 현재 기준 구성원이 대형에 있으면 그대로 쓰고, 없을 때만 무리 중심에 가장 가까운 구성원을 고른다.
    private EnemyFlockMember PickLeader()
    {
        if (leader != null && formation.Contains(leader))
            return leader;

        Vector2 centroid = Vector2.zero;
        foreach (EnemyFlockMember member in formation)
            centroid += member.Position;
        centroid /= formation.Count;

        EnemyFlockMember nearest = null;
        float nearestDistance = float.MaxValue;
        foreach (EnemyFlockMember member in formation)
        {
            float distance = (member.Position - centroid).magnitude;
            if (distance < nearestDistance)
            {
                nearestDistance = distance;
                nearest = member;
            }
        }

        return nearest;
    }

    private void AssignSlots(float spacing, float heightRatio)
    {
        slotOffsets.Clear();
        followers.Clear();
        foreach (EnemyFlockMember member in formation)
        {
            if (member != leader)
                followers.Add(member);
        }

        if (followers.Count == 0)
            return;

        BuildRings(followers.Count, spacing, heightRatio);

        Vector2 center = leader.Position;
        followers.Sort((a, b) => (a.Position - center).sqrMagnitude.CompareTo((b.Position - center).sqrMagnitude));

        int start = 0;
        foreach (int count in ringSizes)
        {
            AssignRing(center, start, count);
            start += count;
        }
    }

    // 고리 하나의 칸을 나눈다. 구성원과 칸을 각각 둘레 각도 순으로 놓고, 총 이동 거리가 가장 짧은 회전 하나를 고른다.
    private void AssignRing(Vector2 center, int start, int count)
    {
        ringGroup.Clear();
        for (int i = 0; i < count; i++)
            ringGroup.Add(followers[start + i]);
        ringGroup.Sort((a, b) => AngleAround(center, a.Position).CompareTo(AngleAround(center, b.Position)));

        int bestShift = 0;
        float bestCost = float.MaxValue;
        for (int shift = 0; shift < count; shift++)
        {
            float cost = 0f;
            for (int i = 0; i < count; i++)
                cost += (center + ringOffsets[start + (i + shift) % count] - ringGroup[i].Position).sqrMagnitude;

            if (cost < bestCost)
            {
                bestCost = cost;
                bestShift = shift;
            }
        }

        for (int i = 0; i < count; i++)
            slotOffsets[ringGroup[i]] = ringOffsets[start + (i + bestShift) % count];
    }

    // count개의 고리 칸을 만든다. 칸은 기준 구성원에서 spacing 이상, 이웃 칸끼리도 spacing 이상 떨어진다.
    // 남은 수가 고리 정원보다 2개 이하로 많으면 다음 고리로 넘기지 않고 이 고리를 넓혀 한 줄로 둘러싼다(예: 8마리 = 기준 1 + 고리 7).
    private void BuildRings(int count, float spacing, float heightRatio)
    {
        ringOffsets.Clear();
        ringSizes.Clear();

        int remaining = count;
        int ring = 0;
        float radius = spacing;
        while (remaining > 0)
        {
            int capacity = Mathf.Max(1, Mathf.FloorToInt(2f * Mathf.PI * radius / spacing));
            int slots = remaining <= capacity + 2 ? remaining : capacity;
            float slotRadius = slots > 1 ? Mathf.Max(radius, spacing / (2f * Mathf.Sin(Mathf.PI / slots))) : radius;

            // 1~2칸은 기준 구성원의 옆에 두고, 3칸부터는 위쪽에서 시작해 둘러싼다. 바깥 고리는 반 칸 엇갈린다.
            float startAngle = slots <= 2 ? 0f : Mathf.PI * 0.5f;
            if (ring % 2 == 1)
                startAngle += Mathf.PI / slots;

            for (int i = 0; i < slots; i++)
            {
                float angle = startAngle + 2f * Mathf.PI * i / slots;
                ringOffsets.Add(new Vector2(Mathf.Cos(angle) * slotRadius, Mathf.Sin(angle) * slotRadius * heightRatio));
            }

            ringSizes.Add(slots);
            remaining -= slots;
            radius = slotRadius + spacing;
            ring++;
        }
    }

    private static float AngleAround(Vector2 center, Vector2 point)
    {
        Vector2 offset = point - center;
        return Mathf.Atan2(offset.y, offset.x);
    }
}
