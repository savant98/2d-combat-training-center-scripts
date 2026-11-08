using UnityEngine;

// 같은 flockId의 적을 하나의 무리(EnemyFlock)로 묶는 등록 정보다. 직접 이동하지 않고 위치와 역할만 무리에 알린다.
// 비행 패턴(FlyingPatrolAbility·FlyingChaseAbility)이 역할을 바꾸고, 비활성화(Pool 반환)되면 무리에서 빠진다.
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
    public Vector2 SpawnPosition { get; private set; }
    public bool HasDetectedPlayer { get; private set; }
    public string EffectiveFlockId => courseGroup ? courseGroupId : flockId;
    public float DetectionDistance => courseGroup ? courseDetectionDistance : detectionDistance;
    public Vector2 DetectionOrigin => courseGroup ? detectionOrigin : hasSpawnPosition ? SpawnPosition : Position;

    public bool CanDetect(Vector2 position) => DetectionDistance <= 0f
        || (position - DetectionOrigin).sqrMagnitude <= DetectionDistance * DetectionDistance;

    // 최초 감지 이후에는 범위를 다시 검사하지 않는다. 패턴·Freeze·순간이동 중에도 교전 상태를 유지한다.
    public bool TryDetectPlayer(Vector2 position)
    {
        if (HasDetectedPlayer) return true;
        if (!CanDetect(position)) return false;
        HasDetectedPlayer = true;
        Flock?.MarkLayoutDirty();
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
        Role = EnemyFlockRole.None;
        Flock = EnemyFlock.Join(flockId, this);
    }

    private void OnDisable()
    {
        Role = EnemyFlockRole.None;
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
