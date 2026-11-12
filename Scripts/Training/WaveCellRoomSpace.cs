using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// 일반 Platform으로 둘러싸인 시작점의 4방향 연결 공간을 구한다. OneWay는 호출부에서 제외한다.
public static class WaveCellRoomSpace
{
    private const int MaximumSearchCells = 262144;
    private static readonly Vector2Int[] Directions =
        { Vector2Int.left, Vector2Int.right, Vector2Int.down, Vector2Int.up };

    // region을 주면 찾은 공간의 빈 칸을 거기에 담는다(비운 집합을 넘긴다. 실패하면 내용은 믿지 않는다).
    public static bool TryGetBounds(HashSet<Vector2Int> solids, Vector2 seed, out Rect bounds, HashSet<Vector2Int> region = null)
    {
        bounds = default;
        if (solids.Count == 0 || !float.IsFinite(seed.x) || !float.IsFinite(seed.y)) return false;
        int left = solids.Min(p => p.x), right = solids.Max(p => p.x);
        int bottom = solids.Min(p => p.y), top = solids.Max(p => p.y);
        long width = (long)right - left + 1, height = (long)top - bottom + 1;
        if (width > MaximumSearchCells / height || width < 3 || height < 3) return false;
        var start = Vector2Int.FloorToInt(seed);
        if (solids.Contains(start) || start.x <= left || start.x >= right || start.y <= bottom || start.y >= top) return false;

        var visited = region ?? new HashSet<Vector2Int>();
        visited.Add(start);
        var queue = new Queue<Vector2Int>();
        queue.Enqueue(start);
        var min = start;
        var max = start;
        while (queue.Count > 0)
        {
            var cell = queue.Dequeue();
            // 외부로 열린 맵의 끝없는 빈 공간을 실내로 오인하지 않는다.
            if (cell.x == left || cell.x == right || cell.y == bottom || cell.y == top) return false;
            min = Vector2Int.Min(min, cell);
            max = Vector2Int.Max(max, cell);
            foreach (var direction in Directions)
            {
                var next = cell + direction;
                if (!solids.Contains(next) && visited.Add(next)) queue.Enqueue(next);
            }
        }
        bounds = Rect.MinMaxRect(min.x, min.y, max.x + 1, max.y + 1);
        return true;
    }
}
