using UnityEngine;

// Admin 레이저 비 패턴.
// 준비: teleportTriggerTime에 플레이어 왼쪽·오른쪽 중 무작위로 고른 쪽으로 순간이동한다(EnemyTeleporter).
//       고른 쪽 자리에 벽이 가까워 몸통이 들어가지 못하면 반대편에 나타난다(LaserShotAbility의 순간이동 규칙과 같다).
//       다시 나타나면 플레이어를 바라본 채 반대쪽으로 감속하며 물러나고, 수직 레이저 줄에 예고선이 laserInterval 간격으로 펼쳐진다.
// 실행: Admin 반대쪽 끝 줄부터 Admin 쪽으로 laserInterval 간격으로 천장 아래에서 바닥까지 단발 레이저를 내리꽂는다
//       (Admin이 플레이어 왼쪽이면 오른쪽 끝부터, 오른쪽이면 왼쪽 끝부터).
//       줄마다 예고선이 나타나고 previewLeadTime이 지나면 발사하며, 그 순간 그 줄의 예고선을 거둔다.
// 회수: 남은 물러남 속도를 마저 줄인다. 이미 발사한 레이저는 제 수명(laserDuration)대로 사라진다.
// 줄 배치: 순간이동을 시작하는 순간의 플레이어 위치를 기준으로 ProjectileSpawnUtility의 슬롯(laserSpacing 간격)에 놓는다.
//          3번째 줄(슬롯 0)이 플레이어 x다. 끝 줄이 벽(또는 카메라 경계)을 넘으면 넘은 줄 수만큼 묶음 전체를 한 슬롯씩
//          반대쪽으로 옮기고, 룸이 좁아 그래도 넘는 줄은 비운다(발사 순번은 그대로). 줄의 시작 높이는 그 x의 천장 아래다.
//          순간이동 자리는 같은 순간에 정하며, 몸통이 그쪽 끝 줄을 덮지 않게 그 줄에서 몸통 반지름 이상 바깥에 둔다.
// 지형 질의는 모두 모터 RaycastEnvironment를 쓴다.
public class LaserRainAbility : AiAbility
{
    // 레이저 줄(월드 좌표). 순번 0이 첫 발(Admin 반대쪽 끝)이다.
    public struct Lane
    {
        public bool active;
        public Vector2 origin;
        public bool hasCeiling;
        public float previewTime;   // 예고선이 나타난 Time.time(아직이면 -1)
        public float fireTime;      // 발사한 Time.time(아직이면 -1)
    }

    private enum Phase { WaitingForTeleport, Teleporting, Retreating }

    private LaserRainDataSO rainData;
    private EnemyTeleporter teleporter;
    private Phase phase;
    private Lane[] lanes = new Lane[0];
    private LaserPreviewLine[] previews = new LaserPreviewLine[0];
    private bool lanesReady;
    private bool executing;
    private float firstFireTime;    // 첫 발 시각(준비 중에는 예정 시각, 실행이 시작되면 실제 시각)
    private int teleportSide;       // 플레이어 기준 순간이동한 쪽(+1 오른쪽)
    private int teleportSerial;     // 멈춘 뒤 늦게 끝난 순간이동을 무시하기 위한 번호
    private int retreatDir;
    private float retreatSpeed;

    public int LaneCount => lanes.Length;
    public Lane GetLane(int order) => lanes[order];
    public LaserPreviewLine GetPreview(int order) => previews[order];
    public float FirstFireTime => firstFireTime;
    public Vector2 LastTeleportTarget { get; private set; }
    // 무작위로 먼저 고른 쪽과 실제로 나타난 쪽(플레이어 기준, +1 오른쪽)
    public int FirstSideChoice { get; private set; }
    public int TeleportSide => teleportSide;
    // 벽 때문에 줄 묶음을 옮긴 슬롯 수(+면 오른쪽으로)
    public int LaneSlotShift { get; private set; }
    public int RetreatDirection => retreatDir;
    public int FiredCount { get; private set; }

    private void Awake()
    {
        rainData = (LaserRainDataSO)data;
    }

    private void OnDisable()
    {
        // 풀 반환으로 꺼질 때도 예고선(별도 풀 개체)이 남지 않게 거둔다.
        ReturnAllPreviews();
    }

    protected override void OnWindupStart()
    {
        teleporter = GetComponent<EnemyTeleporter>();
        ReturnAllPreviews();

        int count = Mathf.Max(1, rainData.laserCount);
        if (lanes.Length != count)
        {
            lanes = new Lane[count];
            previews = new LaserPreviewLine[count];
        }
        for (int i = 0; i < count; i++)
            lanes[i] = new Lane { previewTime = -1f, fireTime = -1f };

        phase = Phase.WaitingForTeleport;
        lanesReady = false;
        executing = false;
        firstFireTime = Time.time + data.windupTime;
        teleportSide = 0;
        FirstSideChoice = 0;
        LaneSlotShift = 0;
        retreatDir = 0;
        retreatSpeed = 0f;
        FiredCount = 0;
        controller.Velocity = Vector2.zero;
    }

    protected override void OnWindup(float t)
    {
        if (phase == Phase.WaitingForTeleport)
        {
            controller.Velocity = Vector2.zero;
            if (t >= rainData.teleportTriggerTime)
                BeginTeleport();
        }
        else if (phase == Phase.Retreating)
            UpdateRetreat();
        // 순간이동 중의 이동은 EnemyTeleporter가 Velocity로 처리하므로 덮어쓰지 않는다.

        UpdateLanes();
    }

    protected override void OnExecuteStart()
    {
        executing = true;
        firstFireTime = Time.time;
        if (!lanesReady)
        {
            // 순간이동을 시작하지 못했으면(준비 시간보다 늦은 teleportTriggerTime) 지금 플레이어 위치로 줄을 정하고,
            // 지금 Admin 자리의 반대쪽 끝부터 쏜다.
            Bounds bounds = default;
            bool hasBounds = rainData.clampToCameraBounds && EnemyCameraBounds.TryGet(out bounds);
            Vector2 player = PlayerPosition();
            PrepareLanes(player, hasBounds, bounds);
            if (controller.transform.position.x < player.x)
                System.Array.Reverse(lanes);
        }
        if (phase != Phase.Retreating)
        {
            // 순간이동이 준비 시간 안에 끝나지 못해 예고선을 아직 보이지 못했으면(심한 프레임 지연)
            // 예고선이 previewLeadTime만큼 보인 뒤 쏘도록 첫 발을 미룬다.
            firstFireTime += Mathf.Max(0f, rainData.previewLeadTime);
        }

        if (phase == Phase.Retreating)
            UpdateRetreat();
        else if (phase == Phase.WaitingForTeleport)
            controller.Velocity = Vector2.zero;
        UpdateLanes();
    }

    protected override void OnExecute(float t)
    {
        if (phase == Phase.Retreating)
            UpdateRetreat();
        UpdateLanes();
    }

    // 모든 줄을 쏜 뒤에 회수로 넘어간다(데이터 시간이 짧게 잡혀도 마지막 줄을 건너뛰지 않는다).
    protected override bool CanFinishExecute(float elapsed) => elapsed >= data.executeTime && AllLanesFired();

    protected override void OnRecoverStart()
    {
        ReturnAllPreviews();
    }

    protected override void OnRecover(float t)
    {
        if (phase == Phase.Retreating)
            UpdateRetreat();
    }

    protected override void OnStop()
    {
        teleportSerial++;
        phase = Phase.WaitingForTeleport;
        executing = false;
        retreatSpeed = 0f;
        controller.Velocity = Vector2.zero;
        ReturnAllPreviews();
    }

    // ===== Teleport =====
    private void BeginTeleport()
    {
        phase = Phase.Teleporting;
        LastTeleportTarget = PlanTeleportAndLanes();

        int serial = ++teleportSerial;
        if (teleporter == null)
        {
            HandleTeleportFinished(serial);
            return;
        }

        teleporter.TeleportSequence(
            LastTeleportTarget,
            feedbackPlayer,
            rainData.teleportStartFeedback,
            rainData.teleportEndFeedback,
            rainData.teleportDisappearDelay,
            rainData.teleportAppearDelay,
            () => HandleTeleportFinished(serial));
    }

    private void HandleTeleportFinished(int serial)
    {
        // 순간이동 도중 Break·사망으로 패턴이 멈췄으면 무시한다.
        if (serial != teleportSerial || !IsRunning || phase != Phase.Teleporting)
            return;

        // 모터가 막아 목표에 못 미쳤어도 실제 자리를 기준으로 플레이어를 바라보고 반대쪽으로 물러난다.
        Vector2 player = PlayerPosition();
        float dx = player.x - controller.transform.position.x;
        int toPlayer = Mathf.Abs(dx) > 0.01f ? (dx > 0f ? 1 : -1) : -teleportSide;
        controller.FacingUpdate(toPlayer);
        retreatDir = -toPlayer;
        retreatSpeed = Mathf.Max(0f, rainData.retreatSpeed);
        controller.Velocity = new Vector2(retreatDir * retreatSpeed, 0f);
        phase = Phase.Retreating;
    }

    // 순간이동 자리와 레이저 줄을 같은 순간의 플레이어 위치로 함께 정한다.
    // 자리: 플레이어 왼쪽·오른쪽 중 무작위로 고른 쪽 teleportDistance(몸통 중심 기준), 바닥에서 bodyHeightAboveFloor 높이.
    //       몸통은 그쪽 끝 줄에서 몸통 반지름 이상 바깥에 둔다. 그 자리에 몸통이 들어갈 공간이 없을 만큼 벽(또는 카메라 경계)이
    //       가까우면 반대편을 쓰고, 양쪽 다 좁으면 덜 모자란 쪽에서 들어가는 만큼만 가며 몸통이 덮는 줄은 비운다.
    // 순서: 줄 자리는 쏘는 순서와 상관없이 같으므로 왼쪽부터 정해 두고, Admin이 플레이어 왼쪽이면 뒤집어 오른쪽 끝부터 쏜다.
    private Vector2 PlanTeleportAndLanes()
    {
        Bounds bounds = default;
        bool hasBounds = rainData.clampToCameraBounds && EnemyCameraBounds.TryGet(out bounds);
        Vector2 player = PlayerPosition();
        float floorAtPlayer = FindFloorY(player.x, player.y + 0.5f, player.y - 0.75f);
        float bodyY = floorAtPlayer + rainData.bodyHeightAboveFloor;

        PrepareLanes(player, hasBounds, bounds);

        FirstSideChoice = Random.value < 0.5f ? -1 : 1;
        float firstOffset = TeleportOffset(player, FirstSideChoice);
        float firstSpace = BodySpace(player, bodyY, FirstSideChoice, hasBounds, bounds);
        float otherOffset = TeleportOffset(player, -FirstSideChoice);
        float otherSpace = BodySpace(player, bodyY, -FirstSideChoice, hasBounds, bounds);
        bool firstFits = firstSpace >= firstOffset;
        bool otherFits = otherSpace >= otherOffset;
        bool useFirst = firstFits || (!otherFits && firstSpace - firstOffset >= otherSpace - otherOffset);
        teleportSide = useFirst ? FirstSideChoice : -FirstSideChoice;
        float offset = useFirst ? firstOffset : otherOffset;
        float space = useFirst ? firstSpace : otherSpace;
        if (teleportSide < 0)
            System.Array.Reverse(lanes);

        float x = player.x + teleportSide * Mathf.Clamp(offset, 0f, Mathf.Max(0f, space));
        for (int i = 0; i < lanes.Length; i++)
            if (lanes[i].active && Mathf.Abs(lanes[i].origin.x - x) < rainData.bodyRadius - 0.01f)
                lanes[i].active = false;

        float floorY = FindFloorY(x, bodyY, floorAtPlayer);
        return new Vector2(x, floorY + rainData.bodyHeightAboveFloor) - rainData.bodyCenterOffset;
    }

    // dir 쪽 순간이동 거리: teleportDistance. 단 몸통이 그쪽 끝 줄을 덮지 않게 그 줄에서 몸통 반지름 이상 바깥.
    private float TeleportOffset(Vector2 player, int dir)
    {
        float offset = rainData.teleportDistance;
        for (int i = 0; i < lanes.Length; i++)
            if (lanes[i].active)
                offset = Mathf.Max(offset, (lanes[i].origin.x - player.x) * dir + rainData.bodyRadius);
        return offset;
    }

    // 플레이어 위치에서 dir 쪽으로 몸통 중심이 갈 수 있는 거리(플레이어 높이·몸통 높이 두 광선 중 가까운 벽 기준).
    private float BodySpace(Vector2 player, float bodyY, int dir, bool hasBounds, Bounds bounds)
    {
        float wall = Mathf.Max(1f, rainData.wallSearchDistance);
        wall = Mathf.Min(wall, WallDistance(player, dir, wall));
        wall = Mathf.Min(wall, WallDistance(new Vector2(player.x, bodyY), dir, wall));
        if (hasBounds)
            wall = Mathf.Min(wall, dir > 0 ? bounds.max.x - player.x : player.x - bounds.min.x);
        return wall - rainData.bodyRadius - rainData.wallClearance;
    }

    private float WallDistance(Vector2 origin, int dir, float search)
    {
        var hit = controller.Motor.RaycastEnvironment(origin, new Vector2(dir, 0f), search);
        return hit.collider != null && hit.distance > 0.001f ? hit.distance : search;
    }

    private float FindFloorY(float x, float fromY, float fallback)
    {
        var hit = controller.Motor.RaycastEnvironment(new Vector2(x, fromY), Vector2.down, Mathf.Max(1f, rainData.verticalSearchDistance));
        return hit.collider != null && hit.distance > 0.001f ? hit.point.y : fallback;
    }

    // ===== Retreat =====
    private void UpdateRetreat()
    {
        retreatSpeed = Mathf.MoveTowards(retreatSpeed, 0f, rainData.retreatDeceleration * Time.deltaTime);
        controller.Velocity = new Vector2(retreatDir * retreatSpeed, 0f);
    }

    // ===== Lanes =====
    // 줄 자리를 왼쪽부터 오른쪽 순서로 정한다. 순번 i의 슬롯은 i - 가운데 순번이고(3번째 = 슬롯 0 = 플레이어 x), 슬롯 +가 오른쪽이다.
    // 쏘는 순서(Admin 반대쪽 끝부터)는 순간이동 쪽이 정해진 뒤 맞춘다.
    private void PrepareLanes(Vector2 player, bool hasBounds, Bounds bounds)
    {
        lanesReady = true;
        float search = Mathf.Max(1f, rainData.wallSearchDistance);
        float left = player.x - WallDistance(player, -1, search);
        float right = player.x + WallDistance(player, 1, search);
        if (hasBounds)
        {
            left = Mathf.Max(left, bounds.min.x);
            right = Mathf.Min(right, bounds.max.x);
        }
        float lo = left + rainData.laserEdgeMargin;
        float hi = right - rainData.laserEdgeMargin;

        int n = lanes.Length;
        int center = (n - 1) / 2;
        int back = -center, front = n - 1 - center;
        var anchor = new Vector2(player.x, 0f);
        var step = new Vector2(Mathf.Max(0.1f, rainData.laserSpacing), 0f);
        float SlotX(int slot) => ProjectileSpawnUtility.GetPositionFromSlot(anchor, step, slot).x;
        bool Inside(int slot)
        {
            float x = SlotX(slot);
            return x >= lo - 0.001f && x <= hi + 0.001f;
        }

        // 끝 줄이 벽을 넘으면 넘은 줄 수만큼 묶음을 한 슬롯씩 반대쪽으로 옮긴다(반대쪽 끝이 벽을 넘지 않는 데까지).
        // back은 왼쪽 끝, front는 오른쪽 끝 슬롯이다.
        int shift = 0;
        while (shift < n && !Inside(back + shift) && Inside(front + shift + 1))
            shift++;
        if (shift == 0)
            while (shift > -n && !Inside(front + shift) && Inside(back + shift - 1))
                shift--;
        LaneSlotShift = shift;

        for (int i = 0; i < n; i++)
        {
            int slot = i - center + shift;
            float x = SlotX(slot);
            lanes[i].active = Inside(slot);
            lanes[i].origin = new Vector2(x, FindLaserTop(x, player.y, hasBounds, bounds, out bool hasCeiling));
            lanes[i].hasCeiling = hasCeiling;
        }
    }

    // 플레이어 높이에서 위로 천장을 찾는다. 없으면 카메라 경계 위, 그것도 없으면 플레이어 위 fallbackTopHeight.
    private float FindLaserTop(float x, float playerY, bool hasBounds, Bounds bounds, out bool hasCeiling)
    {
        float top = playerY + rainData.fallbackTopHeight;
        hasCeiling = false;
        var hit = controller.Motor.RaycastEnvironment(new Vector2(x, playerY), Vector2.up, Mathf.Max(1f, rainData.verticalSearchDistance));
        if (hit.collider != null && hit.distance > 0.001f)
        {
            top = hit.point.y;
            hasCeiling = true;
        }
        else if (hasBounds)
            top = bounds.max.y;

        if (hasBounds && top > bounds.max.y)
            top = bounds.max.y;
        return top - rainData.laserTopMargin;
    }

    private void UpdateLanes()
    {
        if (!lanesReady)
            return;

        float now = Time.time;
        float interval = Mathf.Max(0f, rainData.laserInterval);
        float lead = Mathf.Max(0.01f, rainData.previewLeadTime);
        // 예고선은 Admin이 다시 나타난 뒤(또는 실행이 시작된 뒤)부터 펼친다.
        bool canShow = phase == Phase.Retreating || executing;
        for (int i = 0; i < lanes.Length; i++)
        {
            if (!lanes[i].active || lanes[i].fireTime >= 0f)
                continue;

            float fireAt = firstFireTime + i * interval;
            if (canShow && lanes[i].previewTime < 0f && now >= fireAt - lead)
                ShowPreview(i);

            if (executing && now >= fireAt)
            {
                FireLane(i);
                continue;
            }

            if (previews[i] != null)
                previews[i].UpdatePreviewVisual(fireAt - now, lead);
        }
    }

    private bool AllLanesFired()
    {
        if (!lanesReady)
            return false;
        for (int i = 0; i < lanes.Length; i++)
            if (lanes[i].active && lanes[i].fireTime < 0f)
                return false;
        return true;
    }

    private void ShowPreview(int i)
    {
        lanes[i].previewTime = Time.time;
        if (rainData.previewLinePrefab == null || WorldEntityPool.Instance == null)
            return;

        var preview = WorldEntityPool.Instance.Spawn(rainData.previewLinePrefab, lanes[i].origin, Quaternion.identity);
        preview.Show(
            lanes[i].origin,
            Vector2.down,
            rainData.laserLength,
            rainData.previewLineWidth,
            rainData.laserBlockMask,
            rainData.previewLineColor,
            -1f);
        previews[i] = preview;
    }

    private void FireLane(int i)
    {
        lanes[i].fireTime = Time.time;
        FiredCount++;
        ReturnPreview(i);
        if (rainData.laserPrefab == null || WorldEntityPool.Instance == null)
            return;

        // 레이저는 제 수명(laserDuration)이 끝나면 스스로 풀로 돌아가므로 참조를 들고 있지 않는다.
        var laser = WorldEntityPool.Instance.Spawn(rainData.laserPrefab, lanes[i].origin, Quaternion.identity);
        laser.Fire(
            rainData.laserDamageInfo,
            lanes[i].origin,
            Vector2.down,
            rainData.laserLength,
            rainData.laserDuration,
            rainData.laserBlockMask,
            rainData.laserDamageMask,
            rainData.laserWidth,
            controller);
    }

    private void ReturnPreview(int i)
    {
        var preview = previews[i];
        previews[i] = null;
        // 웨이브 정리(WorldEntityPool.DespawnAll) 등으로 이미 돌아간 예고선과, Play 종료로 풀이 먼저 사라진 경우는 반환하지 않는다.
        if (preview != null && preview.isActiveAndEnabled && WorldEntityPool.Instance != null)
            preview.ForceReturnToPool();
    }

    private void ReturnAllPreviews()
    {
        for (int i = 0; i < previews.Length; i++)
            ReturnPreview(i);
    }

    // ===== Player =====
    private static bool TryGetPlayer(out PlayerController player)
    {
        player = GameManager.Instance != null ? GameManager.Instance.Player : null;
        return player != null;
    }

    // 플레이어가 없으면 Admin이 바라보는 쪽 teleportDistance 앞을 기준으로 삼는다.
    private Vector2 PlayerPosition()
    {
        if (TryGetPlayer(out var player))
            return player.transform.position;
        return (Vector2)controller.transform.position + rainData.bodyCenterOffset
               + new Vector2(controller.Facing * rainData.teleportDistance, 0f);
    }
}
