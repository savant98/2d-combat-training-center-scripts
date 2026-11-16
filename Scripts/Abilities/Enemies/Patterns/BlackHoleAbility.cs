using System.Collections.Generic;
using UnityEngine;

// Admin 블랙홀 패턴.
// 준비: teleportTriggerTime에 룸 중앙으로 순간이동하고(EnemyTeleporter), 다시 나타나면 몸통 중심에 블랙홀이 맺힌다(공간 왜곡만).
// 실행: 블랙홀이 열려 플레이어를 끌어당기고(AdminBlackHole → PlayerController.externalForce, 이동 속도를 조금 넘음),
//       블랙홀 바로 아래 기둥 장판이 닿으면 피해를 준다. 그동안 천장 아래에서 플레이어 크기의 큐브가 맵 전체 줄에 떨어진다(AdminFallingCube).
// 회수·중단: 블랙홀이 붕괴한다. 이미 떨어지는 큐브는 그대로 떨어져 부서진다.
// 룸 중앙: 플레이어 위치에서 모터 RaycastEnvironment로 좌우 벽을 찾아 가운데 x를 정하고, 그 x에서 바닥·천장의 가운데 y를 쓴다.
//          현재 카메라 경계(CinemachineConfiner2D)가 있으면 그 밖의 벽·천장은 같은 룸으로 보지 않는다(복도로 이어진 룸 대응).
public class BlackHoleAbility : AiAbility
{
    private enum WindupPhase { WaitingForTeleport, Teleporting, Forming }

    // 룸 범위 계산 결과(월드 좌표). 검증과 큐브 생성에 같이 쓴다.
    public struct RoomFrame
    {
        public float left, right, floor, ceiling;
        public bool hasLeftWall, hasRightWall, hasFloor, hasCeiling, usedCameraBounds;
        public Vector2 center;
    }

    private BlackHoleDataSO holeData;
    private EnemyTeleporter teleporter;
    private WindupPhase windupPhase;
    private AdminBlackHole activeHole;
    private RoomFrame room;
    private float windupElapsed;
    private float cubeTimer;
    private readonly List<int> recentLanes = new();
    private struct CubeReservation
    {
        public AdminFallingCube cube;
        public uint version;
        public float x;
    }
    private readonly List<CubeReservation> activeCubes = new();
    private readonly List<int> availableLanes = new();

    public RoomFrame LastRoom => room;
    public AdminBlackHole ActiveBlackHole => activeHole;
    public int SpawnedCubeCount { get; private set; }
    public Vector2 LastTeleportTarget { get; private set; }

    private void Awake()
    {
        holeData = (BlackHoleDataSO)data;
    }

    private void OnDisable()
    {
        // 풀 반환으로 꺼질 때도 블랙홀(별도 풀 개체)이 남지 않게 붕괴시킨다.
        CollapseBlackHole();
    }

    protected override void OnWindupStart()
    {
        teleporter = GetComponent<EnemyTeleporter>();
        windupPhase = WindupPhase.WaitingForTeleport;
        windupElapsed = 0f;
        SpawnedCubeCount = 0;
        recentLanes.Clear();
        CollapseBlackHole();
        controller.Velocity = Vector2.zero;
    }

    protected override void OnWindup(float t)
    {
        windupElapsed = t;
        controller.Velocity = Vector2.zero;
        if (windupPhase != WindupPhase.WaitingForTeleport || t < holeData.teleportTriggerTime)
            return;

        windupPhase = WindupPhase.Teleporting;
        room = FindRoomFrame();
        LastTeleportTarget = room.center - holeData.bodyCenterOffset;

        if (teleporter == null)
        {
            HandleTeleportFinished();
            return;
        }

        teleporter.TeleportSequence(
            LastTeleportTarget,
            feedbackPlayer,
            holeData.teleportStartFeedback,
            holeData.teleportEndFeedback,
            holeData.teleportDisappearDelay,
            holeData.teleportAppearDelay,
            HandleTeleportFinished);
    }

    protected override void OnExecuteStart()
    {
        controller.Velocity = Vector2.zero;
        // 순간이동이 준비 시간 안에 끝나지 못했으면(심한 프레임 지연) 순간이동 목표 자리에서 바로 연다.
        if (activeHole == null)
        {
            Vector2 center = windupPhase == WindupPhase.Teleporting
                ? LastTeleportTarget + holeData.bodyCenterOffset
                : (Vector2)controller.transform.position + holeData.bodyCenterOffset;
            SpawnBlackHole(0.05f, center);
        }
        activeHole?.Ignite();
        cubeTimer = -Mathf.Max(0f, holeData.cubeStartDelay);
    }

    protected override void OnExecute(float t)
    {
        controller.Velocity = Vector2.zero;
        UpdateCubeRain(Time.deltaTime);
    }

    protected override void OnRecoverStart()
    {
        controller.Velocity = Vector2.zero;
        CollapseBlackHole();
    }

    protected override void OnRecover(float t)
    {
        controller.Velocity = Vector2.zero;
    }

    protected override void OnStop()
    {
        controller.Velocity = Vector2.zero;
        CollapseBlackHole();
    }

    private void HandleTeleportFinished()
    {
        // 순간이동 도중 Break·사망으로 패턴이 멈췄으면 블랙홀을 만들지 않는다.
        if (State != AbilityState.Windup || windupPhase != WindupPhase.Teleporting)
            return;

        controller.Velocity = Vector2.zero;
        // 위성·강착원반이 같은 방향(반시계)으로 돌게 오른쪽을 본다.
        controller.FacingUpdate(1);
        windupPhase = WindupPhase.Forming;
        // 모터가 막아 목표에 못 미쳤어도 실제 몸통 중심에 맺히게 한다.
        SpawnBlackHole(Mathf.Max(0.05f, data.windupTime - windupElapsed),
            (Vector2)controller.transform.position + holeData.bodyCenterOffset);
    }

    private void SpawnBlackHole(float formDuration, Vector2 center)
    {
        if (holeData.blackHolePrefab == null || WorldEntityPool.Instance == null)
            return;

        activeHole = WorldEntityPool.Instance.Spawn(holeData.blackHolePrefab, center, Quaternion.identity);
        activeHole.BeginForming(controller, holeData.pull, holeData.field, formDuration);
    }

    private void CollapseBlackHole()
    {
        if (activeHole == null)
            return;

        activeHole.Collapse();
        activeHole = null;
    }

    // ===== Cube Rain =====
    private void UpdateCubeRain(float deltaTime)
    {
        if (holeData.cubePrefab == null || WorldEntityPool.Instance == null)
            return;

        cubeTimer += deltaTime;
        int guard = 8;
        while (cubeTimer >= 0f && guard-- > 0)
        {
            SpawnCube();
            float jitter = Random.Range(-holeData.cubeIntervalJitter, holeData.cubeIntervalJitter);
            cubeTimer -= Mathf.Max(0.05f, holeData.cubeInterval * (1f + jitter));
        }
    }

    private void SpawnCube()
    {
        float half = 0.75f;
        float left = room.left + holeData.cubeWallMargin + half;
        float right = room.right - holeData.cubeWallMargin - half;
        if (right < left)
            left = right = (room.left + room.right) * 0.5f;

        int laneCount = Mathf.Max(1, Mathf.FloorToInt((right - left) / Mathf.Max(0.5f, holeData.cubeLaneWidth)) + 1);
        float step = laneCount > 1 ? (right - left) / (laneCount - 1) : 0f;
        int lane = PickLane(laneCount, left, step);
        // 빈 줄이 없으면 이번 생성을 생략한다. 호출자는 타이머를 소비해 다음 프레임에 몰지 않는다.
        if (lane < 0) return;

        float x = left + lane * step;
        float y = room.hasCeiling ? room.ceiling - holeData.cubeCeilingOffset : room.center.y + holeData.cubeFallbackHeight;

        var cube = WorldEntityPool.Instance.Spawn(holeData.cubePrefab, new Vector3(x, y, 0f), Quaternion.identity);
        cube.ConfigureFall(holeData.cubeGravity, holeData.cubeMaxFallSpeed);
        cube.Initialize(controller, holeData.cubeDamageInfo, 0f, 0, holeData.cubeCollideLayer);
        activeCubes.Add(new CubeReservation { cube = cube, version = cube.SpawnVersion, x = x });
        SpawnedCubeCount++;
    }

    // 예고 중이거나 떨어지는 모든 큐브를 예약으로 취급한다. 회수·중단 뒤 남은 큐브도 계속 고려한다.
    private int PickLane(int laneCount, float left, float step)
    {
        for (int i = activeCubes.Count - 1; i >= 0; i--)
        {
            var reservation = activeCubes[i];
            if (reservation.cube == null || !reservation.cube.IsFalling || reservation.cube.SpawnVersion != reservation.version)
                activeCubes.RemoveAt(i);
        }
        availableLanes.Clear();
        float spacing = Mathf.Max(.5f, step);
        var player = GameManager.Instance != null ? GameManager.Instance.Player : null;
        for (int lane = 0; lane < laneCount; lane++)
        {
            float x = left + lane * step;
            if (IsSafeCubePosition(x, spacing) && HasEscapeLane(x, laneCount, left, step, player))
                availableLanes.Add(lane);
        }
        if (availableLanes.Count == 0) return -1;
        int selected = -1;
        if (player != null && step > 0 && Random.value < holeData.cubePlayerTargetChance)
        {
            int target = Mathf.Clamp(Mathf.RoundToInt((player.transform.position.x - left) / step), 0, laneCount - 1);
            if (availableLanes.Contains(target)) selected = target;
        }
        if (selected < 0)
        {
            // 최근 줄을 우선 피하되 안전한 후보가 그것뿐이면 다시 쓸 수 있다.
            int preferred = 0;
            foreach (int lane in availableLanes) if (!recentLanes.Contains(lane)) preferred++;
            int pick = Random.Range(0, preferred > 0 ? preferred : availableLanes.Count);
            foreach (int lane in availableLanes)
            {
                if (preferred > 0 && recentLanes.Contains(lane)) continue;
                if (pick-- == 0) { selected = lane; break; }
            }
        }
        recentLanes.Add(selected);
        while (recentLanes.Count > Mathf.Max(0, holeData.cubeRecentLaneMemory)) recentLanes.RemoveAt(0);
        return selected;
    }

    private bool IsSafeCubePosition(float x, float spacing)
    {
        foreach (var reserved in activeCubes)
            if (Mathf.Abs(reserved.x - x) < spacing * .5f) return false;
        // 어느 위치를 중심으로 보아도 양옆 두 줄 간격 안에는 최대 두 개만 둔다.
        // 플레이어가 다른 줄로 이동해도 세 개가 연속으로 몰린 구간이 생기지 않는다.
        for (int i = 0; i < activeCubes.Count; i++)
            for (int j = i + 1; j < activeCubes.Count; j++)
            {
                float minimum = Mathf.Min(x, Mathf.Min(activeCubes[i].x, activeCubes[j].x));
                float maximum = Mathf.Max(x, Mathf.Max(activeCubes[i].x, activeCubes[j].x));
                if (maximum - minimum <= spacing * 4f + .001f) return false;
            }
        return true;
    }

    private bool HasEscapeLane(float candidate, int laneCount, float left, float step, PlayerController player)
    {
        if (player == null) return true;
        float reach = Mathf.Max(.5f, step) * 2f;
        float halfPlayer = player.Health != null && player.Health.GetComponent<Collider2D>() != null
            ? player.Health.GetComponent<Collider2D>().bounds.extents.x : .75f;
        float clear = .75f + halfPlayer + .1f;
        for (int lane = 0; lane < laneCount; lane++)
        {
            float escape = left + lane * step;
            if (Mathf.Abs(escape - player.transform.position.x) > reach || Mathf.Abs(escape - candidate) < clear) continue;
            if (holeData.field.enabled && activeHole != null && escape > activeHole.FieldRect.xMin - halfPlayer - .1f
                && escape < activeHole.FieldRect.xMax + halfPlayer + .1f) continue;
            bool blocked = false;
            foreach (var reserved in activeCubes)
                if (Mathf.Abs(reserved.x - escape) < clear) { blocked = true; break; }
            if (!blocked) return true;
        }
        return false;
    }

    // ===== Room Center =====
    public RoomFrame FindRoomFrame()
    {
        var motor = controller.Motor;
        var player = GameManager.Instance != null ? GameManager.Instance.Player : null;
        Vector2 origin = player != null
            ? (Vector2)player.transform.position
            : (Vector2)controller.transform.position + holeData.bodyCenterOffset;

        var frame = new RoomFrame();
        float search = Mathf.Max(1f, holeData.wallSearchDistance);
        frame.left = origin.x - search;
        frame.right = origin.x + search;

        var leftHit = motor.RaycastEnvironment(origin, Vector2.left, search);
        if (leftHit.collider != null && leftHit.distance > 0.001f) { frame.left = leftHit.point.x; frame.hasLeftWall = true; }
        var rightHit = motor.RaycastEnvironment(origin, Vector2.right, search);
        if (rightHit.collider != null && rightHit.distance > 0.001f) { frame.right = rightHit.point.x; frame.hasRightWall = true; }

        Bounds bounds = default;
        bool hasBounds = holeData.clampToCameraBounds && EnemyCameraBounds.TryGet(out bounds);
        if (hasBounds)
        {
            frame.usedCameraBounds = true;
            if (frame.left < bounds.min.x) { frame.left = bounds.min.x; frame.hasLeftWall = false; }
            if (frame.right > bounds.max.x) { frame.right = bounds.max.x; frame.hasRightWall = false; }
        }

        float x = (frame.left + frame.right) * 0.5f;
        float vertical = Mathf.Max(1f, holeData.verticalSearchDistance);
        frame.floor = origin.y - 1f;
        var floorHit = motor.RaycastEnvironment(new Vector2(x, origin.y + 0.5f), Vector2.down, vertical);
        if (floorHit.collider != null && floorHit.distance > 0.001f) { frame.floor = floorHit.point.y; frame.hasFloor = true; }

        frame.ceiling = frame.floor + holeData.fallbackCenterHeight * 2f;
        var ceilingHit = motor.RaycastEnvironment(new Vector2(x, frame.floor + 0.5f), Vector2.up, vertical);
        if (ceilingHit.collider != null && ceilingHit.distance > 0.001f) { frame.ceiling = ceilingHit.point.y; frame.hasCeiling = true; }
        if (hasBounds && frame.ceiling > bounds.max.y)
            frame.ceiling = bounds.max.y;

        float minY = frame.floor + holeData.bodyRadius + holeData.verticalClearance;
        float maxY = frame.ceiling - holeData.bodyRadius - holeData.verticalClearance;
        float y = frame.hasCeiling || hasBounds ? (frame.floor + frame.ceiling) * 0.5f : frame.floor + holeData.fallbackCenterHeight;
        y = maxY >= minY ? Mathf.Clamp(y, minY, maxY) : minY;
        frame.center = new Vector2(x, y);
        return frame;
    }
}
