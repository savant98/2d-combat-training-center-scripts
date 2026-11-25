using System.Collections.Generic;
using UnityEngine;

// 비행 적 경로 찾기(EnemyFlightNavigator)가 함께 쓰는 점유 격자다. 같은 영역(방)·같은 몸(적 종류)이면 한 격자를 나눠 쓴다.
// 칸은 '그 칸 중심에 이 몸의 Transform이 있을 때 지형과 겹치는가'(몸 크기를 반영한 구성 공간)이고 간격은 0.5칸이다.
// 칸 중심은 0.5칸의 배수에 둔다. 타일 지형(정수 칸)의 틈 가운데가 늘 칸 중심이 되어 좁은 틈도 놓치지 않는다.
// 질의는 그 적의 RaycastMotor2D.CastBody(이동과 같은 충돌 규칙)뿐이고 위치는 바꾸지 않는다.
// - 처음에는 모두 모름이다. 경로를 찾으면서 필요한 칸만 조사한다(탐색 한 번에 조사 상한, 넘치면 모르는 칸은 열린 것으로 본다).
//   쓰는 동안 프레임마다 모르는 칸도 미리 채운다(프레임 질의 상한의 절반 안). 그래서 긴 우회로도 몇 초 안에 정확한 경로가 된다.
// - 시간 분할 갱신: 쓰는 동안 프레임마다 아는 칸 몇 개를 차례로 다시 조사한다(파괴 Prop·생성 발판이 바뀌면 따라온다).
//   IdleTime 넘게 쉰 격자는 그동안 갱신이 멈춰 지형 변화(Wave 전환·방 재생성)를 모르므로, 같은 키로 다시 잡을 때 모두 모름으로 되돌린다.
// - 불일치 즉시: 경로를 따라가다 몸 쓸기가 막히면 그 구간의 칸을 바로 다시 조사한다(ReprobeSegment).
// 탐색은 8방향 A*(대각은 두 축이 모두 열려야 지난다)다. 이웃 순서와 동점 처리가 고정이라 같은 격자에서는 늘 같은 경로가 나온다.
public sealed class EnemyFlightGrid
{
    public const float CellSize = .5f;
    private const int MaxGrids = 8;
    private const int MaxCells = 48000;
    private const int MaxWidth = 320;
    // 이 시간(초) 동안 경로를 찾지 않은 격자는 시간 분할 갱신을 쉰다.
    private const float IdleTime = 3f;
    private const byte Unknown = 0, Free = 1, Blocked = 2;
    private const float Diagonal = 1.41421356f;
    private static readonly int[] StepX = { 1, -1, 0, 0, 1, -1, 1, -1 };
    private static readonly int[] StepY = { 0, 0, 1, -1, 1, 1, -1, -1 };
    private static readonly List<EnemyFlightGrid> grids = new();
    private static readonly List<int> cellPath = new(128);

    // Domain Reload를 끈 Play 반복에서도 이전 실행의 격자가 남지 않게 한다.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetGrids() => grids.Clear();

    public static int Count => grids.Count;
    public static void ClearAll() => grids.Clear();

    public string BodyKey { get; }
    public Rect Area { get; }
    public int Width { get; }
    public int Height { get; }
    public int KnownCells { get; private set; }
    public int LastExpanded { get; private set; }
    public int LastProbes { get; private set; }

    private readonly Vector2 origin;   // 칸 (0, 0)의 중심
    private readonly byte[] state;
    private readonly float[] cost;
    private readonly int[] parent;
    private readonly int[] openStamp;
    private readonly int[] closedStamp;
    private int stamp;
    private int[] heap = new int[256];
    private float[] heapKey = new float[256];
    private int heapCount;
    private float lastUsed;
    private int lastTickFrame = -1;
    private int cursor;
    private int fillCursor;
    private int searchStart = -1;
    private int probesLeft;

    // 같은 몸·같은 영역의 격자를 찾고, 없으면 만든다. 오래 쓰지 않은 격자부터 정리해 MaxGrids를 넘지 않는다.
    // 영역이 정확히 같지 않아도 최근(IdleTime 안)에 쓴 같은 몸의 격자가 a·b를 모두 담으면 그것을 이어 쓴다
    // (방 밖 목표·주변 영역이 움직일 때마다 격자를 새로 만들어 조사한 칸을 버리던 것을 막는다).
    public static EnemyFlightGrid Acquire(string bodyKey, Rect area, Vector2 a, Vector2 b)
    {
        Rect snapped = Snap(area);
        float now = Time.time;
        EnemyFlightGrid near = null;
        for (int i = 0; i < grids.Count; i++)
        {
            EnemyFlightGrid grid = grids[i];
            if (grid.BodyKey != bodyKey) continue;
            if (Same(grid.Area, snapped))
            {
                if (now - grid.lastUsed > IdleTime) grid.Forget();
                grid.lastUsed = now;
                return grid;
            }
            if (now - grid.lastUsed <= IdleTime && grid.Contains(a) && grid.Contains(b) && (near == null || grid.lastUsed > near.lastUsed)) near = grid;
        }
        if (near != null)
        {
            near.lastUsed = now;
            return near;
        }
        return Create(bodyKey, snapped, now);
    }

    public static EnemyFlightGrid Acquire(string bodyKey, Rect area)
    {
        Rect snapped = Snap(area);
        float now = Time.time;
        for (int i = 0; i < grids.Count; i++)
        {
            EnemyFlightGrid grid = grids[i];
            if (grid.BodyKey == bodyKey && Same(grid.Area, snapped))
            {
                if (now - grid.lastUsed > IdleTime) grid.Forget();
                grid.lastUsed = now;
                return grid;
            }
        }
        return Create(bodyKey, snapped, now);
    }

    private static Rect Snap(Rect area)
    {
        float minX = Mathf.Floor(area.xMin / CellSize) * CellSize, minY = Mathf.Floor(area.yMin / CellSize) * CellSize;
        float maxX = Mathf.Ceil(area.xMax / CellSize) * CellSize, maxY = Mathf.Ceil(area.yMax / CellSize) * CellSize;
        return Rect.MinMaxRect(minX, minY, maxX, maxY);
    }

    private static EnemyFlightGrid Create(string bodyKey, Rect snapped, float now)
    {
        if (grids.Count >= MaxGrids)
        {
            int oldest = 0;
            for (int i = 1; i < grids.Count; i++)
                if (grids[i].lastUsed < grids[oldest].lastUsed) oldest = i;
            grids.RemoveAt(oldest);
        }

        var created = new EnemyFlightGrid(bodyKey, snapped) { lastUsed = now };
        grids.Add(created);
        return created;
    }

    // 오래 쉰 격자의 지식을 버린다(쉬는 동안 시간 분할 갱신이 멈춰 옛 지형의 막힘·빈 칸이 남아 있을 수 있다).
    // 'near' 재사용은 IdleTime 안에 쓴 격자만 고르므로 여기를 거치지 않는다.
    private void Forget()
    {
        System.Array.Clear(state, 0, state.Length);
        KnownCells = 0;
        cursor = 0;
        fillCursor = 0;
    }

    private static bool Same(Rect a, Rect b) =>
        Mathf.Abs(a.xMin - b.xMin) < .01f && Mathf.Abs(a.yMin - b.yMin) < .01f
        && Mathf.Abs(a.xMax - b.xMax) < .01f && Mathf.Abs(a.yMax - b.yMax) < .01f;

    private EnemyFlightGrid(string bodyKey, Rect area)
    {
        BodyKey = bodyKey;
        Area = area;
        int width = Mathf.RoundToInt(area.width / CellSize) + 1;
        int height = Mathf.RoundToInt(area.height / CellSize) + 1;
        Vector2 start = area.min;
        // 너무 큰 영역은 가운데를 남기고 자른다(메모리 상한). 밖의 출발·목표는 경로 없음으로 보고 국소 회피가 맡는다.
        if (width > MaxWidth)
        {
            start.x += (width - MaxWidth) / 2 * CellSize;
            width = MaxWidth;
        }
        int maxHeight = Mathf.Max(1, MaxCells / width);
        if (height > maxHeight)
        {
            start.y += (height - maxHeight) / 2 * CellSize;
            height = maxHeight;
        }

        Width = Mathf.Max(1, width);
        Height = Mathf.Max(1, height);
        origin = start;
        int count = Width * Height;
        state = new byte[count];
        cost = new float[count];
        parent = new int[count];
        openStamp = new int[count];
        closedStamp = new int[count];
    }

    public Vector2 CenterOf(int index) => origin + new Vector2(index % Width, index / Width) * CellSize;

    private Vector2Int CellOf(Vector2 point) => new(
        Mathf.RoundToInt((point.x - origin.x) / CellSize),
        Mathf.RoundToInt((point.y - origin.y) / CellSize));

    private bool InGrid(Vector2Int cell) => cell.x >= 0 && cell.y >= 0 && cell.x < Width && cell.y < Height;

    public bool Contains(Vector2 point) => InGrid(CellOf(point));

    // 검증용: 그 점이 든 칸을 지금 막힌 것으로 알고 있는지(모름은 false).
    public bool IsKnownBlocked(Vector2 point)
    {
        Vector2Int cell = CellOf(point);
        return InGrid(cell) && state[cell.y * Width + cell.x] == Blocked;
    }

    private bool Probe(int index, RaycastMotor2D motor, bool countCast = true)
    {
        if (countCast) EnemyFlightNavigator.NoteCast();
        bool blocked = motor.CastBody(CenterOf(index), Vector2.up, 0f).collider != null;
        if (state[index] == Unknown) KnownCells++;
        state[index] = blocked ? Blocked : Free;
        return !blocked;
    }

    // 모르는 칸이 남았는지(미리 채우기가 끝났는지).
    public bool FullyKnown => KnownCells >= state.Length;

    // 시간 분할 갱신: 이 프레임에 아직 하지 않았고 최근에 쓴 격자면
    // ① 모르는 칸을 fillBudget개까지 미리 조사하고(탐색 한 번의 조사 상한을 넘는 긴 우회로도 몇 초 안에 정확해진다)
    // ② 아는 칸을 budget개까지 차례로 다시 조사한다(파괴 Prop·생성 발판 변화 추종).
    public void Tick(RaycastMotor2D motor, int budget, int fillBudget)
    {
        int frame = Time.frameCount;
        if (frame == lastTickFrame || motor == null || KnownCells == 0 || Time.time - lastUsed > IdleTime) return;
        lastTickFrame = frame;
        int total = state.Length;
        if (fillBudget > 0 && KnownCells < total) Fill(motor, fillBudget);
        int scanLimit = Mathf.Min(total, budget * 8);
        int probed = 0;
        for (int scanned = 0; scanned < scanLimit && probed < budget; scanned++)
        {
            if (cursor >= total) cursor = 0;
            if (state[cursor] != Unknown)
            {
                Probe(cursor, motor);
                probed++;
            }
            cursor++;
        }
    }

    // 모르는 칸만 fillCursor부터 차례로 조사한다(최대 budget칸, 바이트 비교만 하는 훑기는 budget × 32칸까지).
    private void Fill(RaycastMotor2D motor, int budget)
    {
        int total = state.Length;
        int scanLimit = Mathf.Min(total, budget * 32);
        int probed = 0;
        for (int scanned = 0; scanned < scanLimit && probed < budget; scanned++)
        {
            if (fillCursor >= total) fillCursor = 0;
            if (state[fillCursor] == Unknown)
            {
                Probe(fillCursor, motor);
                probed++;
            }
            fillCursor++;
        }
    }

    // 검증용: 모르는 칸을 모두 조사한다(같은 지식 → 같은 경로 검사의 전제).
    public void FillAllForCheck(RaycastMotor2D motor)
    {
        if (motor == null) return;
        for (int i = 0; i < state.Length; i++)
            if (state[i] == Unknown) Probe(i, motor, false);   // 검증용 — 실행 중 프레임 질의 몫에 넣지 않는다
    }

    // 불일치 즉시 갱신: a → b 구간이 지나는 칸을 바로 다시 조사한다(최대 maxProbes칸).
    public void ReprobeSegment(Vector2 a, Vector2 b, RaycastMotor2D motor, int maxProbes)
    {
        if (motor == null) return;
        Vector2 delta = b - a;
        int steps = Mathf.Clamp(Mathf.CeilToInt(delta.magnitude / CellSize), 1, Mathf.Max(1, maxProbes));
        int previous = -1;
        for (int i = 0; i <= steps; i++)
        {
            Vector2Int cell = CellOf(a + delta * (i / (float)steps));
            if (!InGrid(cell)) continue;
            int index = cell.y * Width + cell.x;
            if (index == previous) continue;
            previous = index;
            Probe(index, motor);
        }
    }

    // start → goal 경로의 꺾이는 점(칸 중심)을 output에 넣는다. 첫 점은 출발 칸 다음이고 마지막 점은 목표 칸 중심이다.
    // 목표 칸이 막혀 있으면 둘레 4칸 안에서 가장 가까운 빈 칸을 목표로 쓴다. 출발·목표가 격자 밖이거나 길이 없으면 false.
    public bool FindPath(Vector2 start, Vector2 goal, RaycastMotor2D motor, int probeBudget, int expandLimit, List<Vector2> output)
    {
        output.Clear();
        LastExpanded = 0;
        LastProbes = 0;
        if (motor == null) return false;
        lastUsed = Time.time;
        Vector2Int startCell = CellOf(start), goalCell = CellOf(goal);
        if (!InGrid(startCell) || !InGrid(goalCell)) return false;

        probesLeft = Mathf.Max(0, probeBudget);
        searchStart = startCell.y * Width + startCell.x;
        // 지금 서 있는 칸이 막힌 것으로 남아 있으면(지형이 사라짐·반 칸 차이) 바로 다시 본다. 출발 칸은 결과와 관계없이 지난다.
        if (state[searchStart] == Blocked && probesLeft > 0) { probesLeft--; Probe(searchStart, motor); }

        int goalIndex = goalCell.y * Width + goalCell.x;
        if (!Passable(goalIndex, motor))
        {
            goalIndex = NearestFree(goalCell, 4, motor);
            if (goalIndex < 0) return Finish(false, probeBudget);
        }
        if (goalIndex == searchStart)
        {
            output.Add(CenterOf(goalIndex));
            return Finish(true, probeBudget);
        }

        NextStamp();
        heapCount = 0;
        cost[searchStart] = 0f;
        parent[searchStart] = -1;
        openStamp[searchStart] = stamp;
        Push(searchStart, Heuristic(searchStart, goalIndex));
        int expanded = 0;
        while (heapCount > 0 && expanded < expandLimit)
        {
            int current = Pop();
            if (closedStamp[current] == stamp) continue;
            closedStamp[current] = stamp;
            expanded++;
            if (current == goalIndex)
            {
                LastExpanded = expanded;
                Build(current, output);
                return Finish(true, probeBudget);
            }

            int cx = current % Width, cy = current / Width;
            for (int k = 0; k < 8; k++)
            {
                int nx = cx + StepX[k], ny = cy + StepY[k];
                if (nx < 0 || ny < 0 || nx >= Width || ny >= Height) continue;
                int next = ny * Width + nx;
                if (closedStamp[next] == stamp || !Passable(next, motor)) continue;
                bool diagonal = k >= 4;
                if (diagonal && (!Passable(cy * Width + nx, motor) || !Passable(ny * Width + cx, motor))) continue;
                float g = cost[current] + (diagonal ? Diagonal : 1f);
                if (openStamp[next] == stamp && g >= cost[next] - 1e-5f) continue;
                openStamp[next] = stamp;
                cost[next] = g;
                parent[next] = current;
                Push(next, g + Heuristic(next, goalIndex));
            }
        }

        LastExpanded = expanded;
        return Finish(false, probeBudget);
    }

    private bool Finish(bool found, int probeBudget)
    {
        LastProbes = Mathf.Max(0, probeBudget) - probesLeft;
        searchStart = -1;
        return found;
    }

    // 아는 빈 칸은 지나고 막힌 칸은 못 지난다. 모르는 칸은 조사 몫이 남았으면 조사하고, 다 썼으면 열린 것으로 본다(낙관).
    private bool Passable(int index, RaycastMotor2D motor)
    {
        if (index == searchStart) return true;
        byte known = state[index];
        if (known == Free) return true;
        if (known == Blocked) return false;
        if (probesLeft <= 0) return true;
        probesLeft--;
        return Probe(index, motor);
    }

    private int NearestFree(Vector2Int center, int radius, RaycastMotor2D motor)
    {
        for (int r = 1; r <= radius; r++)
        {
            int best = -1;
            int bestDistance = int.MaxValue;
            for (int dy = -r; dy <= r; dy++)
            {
                for (int dx = -r; dx <= r; dx++)
                {
                    if (Mathf.Abs(dx) != r && Mathf.Abs(dy) != r) continue;
                    var cell = new Vector2Int(center.x + dx, center.y + dy);
                    if (!InGrid(cell)) continue;
                    int index = cell.y * Width + cell.x;
                    int distance = dx * dx + dy * dy;
                    if (distance >= bestDistance || !Passable(index, motor)) continue;
                    best = index;
                    bestDistance = distance;
                }
            }
            if (best >= 0) return best;
        }
        return -1;
    }

    private float Heuristic(int a, int b)
    {
        int dx = Mathf.Abs(a % Width - b % Width), dy = Mathf.Abs(a / Width - b / Width);
        int low = Mathf.Min(dx, dy);
        return dx + dy + (Diagonal - 2f) * low;
    }

    private void NextStamp()
    {
        if (stamp == int.MaxValue)
        {
            System.Array.Clear(openStamp, 0, openStamp.Length);
            System.Array.Clear(closedStamp, 0, closedStamp.Length);
            stamp = 0;
        }
        stamp++;
    }

    // 목표에서 출발까지 거슬러 올라간 칸을 뒤집어 방향이 바뀌는 칸과 마지막 칸만 남긴다.
    private void Build(int goalIndex, List<Vector2> output)
    {
        cellPath.Clear();
        for (int index = goalIndex; index >= 0; index = parent[index])
        {
            cellPath.Add(index);
            if (index == searchStart) break;
        }
        cellPath.Reverse();
        int last = cellPath.Count - 1;
        for (int i = 1; i <= last; i++)
        {
            if (i < last)
            {
                int a = cellPath[i - 1], b = cellPath[i], c = cellPath[i + 1];
                int dx1 = b % Width - a % Width, dy1 = b / Width - a / Width;
                int dx2 = c % Width - b % Width, dy2 = c / Width - b / Width;
                if (dx1 == dx2 && dy1 == dy2) continue;
            }
            output.Add(CenterOf(cellPath[i]));
        }
    }

    private void Push(int index, float key)
    {
        if (heapCount == heap.Length)
        {
            System.Array.Resize(ref heap, heap.Length * 2);
            System.Array.Resize(ref heapKey, heapKey.Length * 2);
        }
        int i = heapCount++;
        heap[i] = index;
        heapKey[i] = key;
        while (i > 0)
        {
            int up = (i - 1) / 2;
            if (!Less(i, up)) break;
            Swap(i, up);
            i = up;
        }
    }

    private int Pop()
    {
        int top = heap[0];
        heapCount--;
        if (heapCount > 0)
        {
            heap[0] = heap[heapCount];
            heapKey[0] = heapKey[heapCount];
            int i = 0;
            while (true)
            {
                int left = i * 2 + 1, right = left + 1, smallest = i;
                if (left < heapCount && Less(left, smallest)) smallest = left;
                if (right < heapCount && Less(right, smallest)) smallest = right;
                if (smallest == i) break;
                Swap(i, smallest);
                i = smallest;
            }
        }
        return top;
    }

    // 키가 같으면 칸 번호가 작은 쪽이 먼저다(결정적 순서).
    private bool Less(int a, int b) => heapKey[a] < heapKey[b] || (heapKey[a] == heapKey[b] && heap[a] < heap[b]);

    private void Swap(int a, int b)
    {
        (heap[a], heap[b]) = (heap[b], heap[a]);
        (heapKey[a], heapKey[b]) = (heapKey[b], heapKey[a]);
    }
}
