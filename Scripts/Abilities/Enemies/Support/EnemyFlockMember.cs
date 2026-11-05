using UnityEngine;

// 같은 flockId의 적을 하나의 무리(EnemyFlock)로 묶는 등록 정보다. 직접 이동하지 않고 위치와 역할만 무리에 알린다.
// 비행 패턴(FlyingPatrolAbility·FlyingChaseAbility)이 역할을 바꾸고, 비활성화(Pool 반환)되면 무리에서 빠진다.
public class EnemyFlockMember : MonoBehaviour
{
    [Tooltip("같은 이름의 구성원끼리 하나의 무리가 됩니다. 종류가 다른 적은 다른 이름을 씁니다.")]
    [SerializeField] private string flockId = string.Empty;

    public string FlockId => flockId;
    public EnemyFlock Flock { get; private set; }
    public EnemyFlockRole Role { get; private set; } = EnemyFlockRole.None;
    public Vector2 Position => transform.position;

    private void OnEnable()
    {
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
