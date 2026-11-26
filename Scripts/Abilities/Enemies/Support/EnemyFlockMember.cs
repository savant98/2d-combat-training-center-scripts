using UnityEngine;

// 같은 flockId의 적을 하나의 무리(EnemyFlock)로 묶는 등록 정보다. 직접 이동하지 않고 위치와 역할만 무리에 알린다.
// 비행 패턴(FlyingPatrolAbility·FlyingChaseAbility)이 역할을 바꾸고, 비활성화(Pool 반환)되면 무리에서 빠진다.
// 6차: 던전 전투 방은 DungeonEnemyEngagement가 방·종류 단위 무리와 감지 규칙(즉시 교전·무리 감지 공유)을 정하고,
// 길찾기 우선 무리는 이동이 막힌 구성원을 NavigationBlocked로 알려 무리가 기준 구성원을 바꾸게 한다.
public class EnemyFlockMember : MonoBehaviour
{
    [Tooltip("같은 이름의 구성원끼리 하나의 무리가 됩니다. 종류가 다른 적은 다른 이름을 씁니다.")]
    [SerializeField] private string flockId = string.Empty;
    [SerializeField, Min(0f), Tooltip("플레이어 최대 감지 거리(칸)입니다. 0이면 제한하지 않습니다. 코스에서는 군집 생성 지점을 기준으로 합니다.")]
    private float detectionDistance;

    private bool courseGroup;
    private Vector2 detectionOrigin;
    private float courseDetectionDistance;
    private string courseGroupId;
    private bool hasSpawnPosition;
    private EnemyFlightNavigator navigator;
    public Vector2 SpawnPosition { get; private set; }
    public bool HasDetectedPlayer { get; private set; }
    public string EffectiveFlockId => courseGroup ? courseGroupId : flockId;
    public float DetectionDistance => courseGroup ? courseDetectionDistance : detectionDistance;
    // Prefab에 정한 감지 거리(칸)다. 코스·던전 무리 설정으로 바꾼 값과 관계없다.
    public float BaseDetectionDistance => detectionDistance;
    public Vector2 DetectionOrigin => courseGroup ? detectionOrigin : hasSpawnPosition ? SpawnPosition : Position;

    // 길찾기 우선 무리(6차): 이 구성원의 이동이 막혀 있는지(회피가 직선이 아닌 상태가 오래 이어지거나 도달 불가).
    // FlyingPatrolAbility가 정하고, EnemyFlock이 막힌 기준 구성원을 바꿀 때 읽는다. 물리 질의는 하지 않는다.
    public bool NavigationBlocked { get; set; }
    // 이 적의 공용 회피(실행 중 비행 패턴이 붙인다). 아직 없으면 null이다.
    public EnemyFlightNavigator Navigator
    {
        get
        {
            if (navigator == null) TryGetComponent(out navigator);
            return navigator;
        }
    }

    public bool CanDetect(Vector2 position) => DetectionDistance <= 0f
        || (position - DetectionOrigin).sqrMagnitude <= DetectionDistance * DetectionDistance;

    // 최초 감지 이후에는 범위를 다시 검사하지 않는다. 패턴·Freeze·순간이동 중에도 교전 상태를 유지한다.
    // 무리 감지 공유가 켜진 무리(EnemyFlock.ShareDetection — 던전 재입장 순찰)는 다른 구성원이 먼저 감지했으면 범위 밖이어도 감지한다.
    public bool TryDetectPlayer(Vector2 position)
    {
        if (HasDetectedPlayer) return true;
        EnemyFlock flock = Flock;
        bool alerted = flock != null && flock.Alerted;
        if (!alerted && !CanDetect(position)) return false;
        HasDetectedPlayer = true;
        if (flock != null)
        {
            flock.NotifyDetected();
            flock.MarkLayoutDirty();
        }
        return true;
    }

    // Pool 활성화 직후에는 이전 위치일 수 있으므로, 생성 위치 배치가 끝난 첫 패턴에서 한 번 기록한다.
    public void RememberSpawnPosition()
    {
        if (hasSpawnPosition) return;
        SpawnPosition = Position;
        hasSpawnPosition = true;
    }

    // 코스의 서로 떨어진 군집이 대형·감지 기준을 공유하지 않게 한다.
    public void ConfigureCourseGroup(string groupId, Vector2 origin, float radius)
    {
        RememberSpawnPosition();
        Flock?.Leave(this);
        Flock = EnemyFlock.Join(groupId, this);
        courseGroup = true;
        courseGroupId = groupId;
        detectionOrigin = origin;
        courseDetectionDistance = radius;
    }

    // 던전 전투 방의 무리(6차, DungeonEnemyEngagement): 코스 무리와 같고, shareDetection이면 한 구성원이 감지할 때 무리 전체가 교전한다.
    // 같은 키의 무리는 마지막 설정을 따른다(한 방의 같은 구성은 늘 같은 값을 준다).
    public void ConfigureEncounterGroup(string groupId, Vector2 origin, float radius, bool shareDetection)
    {
        ConfigureCourseGroup(groupId, origin, radius);
        if (Flock != null) Flock.ShareDetection = shareDetection;
    }

    public string FlockId => flockId;
    public EnemyFlock Flock { get; private set; }
    public EnemyFlockRole Role { get; private set; } = EnemyFlockRole.None;
    public Vector2 Position => transform.position;

    private void OnEnable()
    {
        courseGroup = false;
        courseGroupId = null;
        HasDetectedPlayer = false;
        hasSpawnPosition = false;
        NavigationBlocked = false;
        Role = EnemyFlockRole.None;
        Flock = EnemyFlock.Join(flockId, this);
    }

    private void OnDisable()
    {
        Role = EnemyFlockRole.None;
        NavigationBlocked = false;
        Flock?.Leave(this);
        Flock = null;
    }

    public void SetRole(EnemyFlockRole role)
    {
        if (Role == role)
            return;

        Role = role;
        Flock?.MarkLayoutDirty();
    }
}
