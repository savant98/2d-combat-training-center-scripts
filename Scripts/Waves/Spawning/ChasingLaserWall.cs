using System.Collections.Generic;
using UnityEngine;

// 훈련 구간의 시작 쪽(바닥·벽 속)에 얇은 레이저를 켜 두고, 잠시 기다렸다가 진행 방향으로 밀어 플레이어를 쫓는다.
// 앞장선 레이저 뒤로 일정 간격마다 레이저가 시작 위치에서 새로 나와 일렬로 나란히 함께 쫓아오며, 구간을 뒤에서부터 막는다.
// 표시·피해 판정은 기존 LaserBeam을 그대로 쓰고, 이 컴포넌트는 레이저를 놓을 위치와 시점만 정한다.
// 플레이어가 이 레이저에 맞으면 같은 웨이브의 TutorialPositionResetter가 시작 위치로 되돌리고, 레이저도 시작 위치에서 다시 기다렸다가 쫓는다.
// 코스에 놓인 적에게 맞은 피해로는 다시 시작하지 않는다. 피해의 출처는 LaserBeam.PlayerHitSource로 구분한다(IsCourseLaserHit).
public sealed class ChasingLaserWall : WorldEntityBase
{
    // 지금 켜져 있는 코스 레이저 벽. Domain Reload를 끈 설정에서도 Play마다 비운다.
    private static readonly List<ChasingLaserWall> ActiveWalls = new List<ChasingLaserWall>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => ActiveWalls.Clear();

    // PlayerHealth.OnDamaged를 받는 중에 부르면, 이번 피해가 켜져 있는 코스 레이저 벽의 레이저에서 왔는지 알려 준다.
    public static bool IsCourseLaserHit
    {
        get
        {
            var source = LaserBeam.PlayerHitSource;
            if (source == null) return false;
            foreach (var wall in ActiveWalls)
                if (wall != null && wall.Owns(source)) return true;
            return false;
        }
    }

    public enum Phase { Holding, Chasing, Halted }

    [Header("Direction")]
    [SerializeField, Tooltip("레이저가 쫓아가는 방향입니다. 등반은 위, 달리기는 오른쪽입니다.")]
    private Vector2 travelDirection = Vector2.up;
    [SerializeField, Tooltip("레이저 빔이 뻗는 방향입니다. 진행 방향과 수직으로 둡니다.")]
    private Vector2 beamDirection = Vector2.right;

    [Header("Laser")]
    [SerializeField, Tooltip("앞장서 움직이는 레이저입니다. 반복음을 냅니다.")]
    private LaserBeam leadLaserPrefab;
    [SerializeField, Tooltip("앞장선 레이저 뒤로 줄지어 따라오는 레이저입니다. 반복음이 겹치지 않도록 소리 없는 Prefab을 씁니다.")]
    private LaserBeam trailLaserPrefab;
    [SerializeField] private WeaponBaseSO.DamageInfo laserDamage;
    [SerializeField, Min(0f), Tooltip("생성 위치에서 빔 방향으로 뻗는 길이(칸)입니다. 구간의 안쪽 폭에 맞춥니다.")]
    private float laserLength = 26f;
    [SerializeField, Min(.05f)] private float laserWidth = .25f;
    [SerializeField, Tooltip("피해를 줄 대상 레이어입니다.")]
    private LayerMask damageMask;
    [SerializeField, Tooltip("레이저를 막는 레이어입니다. 발판을 통과하도록 비워 둡니다.")]
    private LayerMask blockMask;

    [Header("Chase")]
    [SerializeField, Min(0f), Tooltip("켜진 뒤 움직이기 전에 기다리는 시간(초)입니다. 맞은 뒤에도 시작 위치에서 같은 시간만큼 기다립니다.")]
    private float holdDuration = 2f;
    [SerializeField, Min(0f), Tooltip("초당 나아가는 거리(칸)입니다.")]
    private float speed = 1.25f;
    [SerializeField, Min(0f), Tooltip("생성 위치에서 나아갈 수 있는 최대 거리(칸)입니다.")]
    private float maxTravel = 84f;
    [SerializeField, Min(0f), Tooltip("줄지어 따라오는 레이저 사이 간격(칸)입니다. 0이면 앞장선 레이저 하나만 쫓아옵니다.")]
    private float trailSpacing = 2f;

    private LaserBeam lead;
    private readonly List<LaserBeam> trail = new List<LaserBeam>();
    private PlayerHealth subscribedHealth;
    private float travel;
    private float timer;
    // 맞힌 다음 프레임에 처음부터 다시 기다린다. 같은 피해 알림을 받는 다른 쪽(위치 초기화·코스 적)이 출처를 확인하기 전에
    // 맞힌 레이저를 거두면 IsCourseLaserHit가 거짓이 되고, 피해 판정 도중에 그 레이저를 풀로 돌려보내게 되기 때문이다.
    private bool holdRequested;

    public Phase CurrentPhase { get; private set; }
    public float CurrentTravel => travel;
    public bool IsHolding => CurrentPhase == Phase.Holding;
    public bool IsHalted => CurrentPhase == Phase.Halted;
    public LaserBeam LeadLaser => lead != null && lead.gameObject.activeInHierarchy ? lead : null;
    public Vector2 TravelDirection => travelDirection.normalized;
    public int ActiveLaserCount
    {
        get
        {
            int count = LeadLaser != null ? 1 : 0;
            foreach (var laser in trail)
                if (laser != null && laser.gameObject.activeInHierarchy) count++;
            return count;
        }
    }

    public override void OnSpawnedFromPool()
    {
        base.OnSpawnedFromPool();
        if (!ActiveWalls.Contains(this)) ActiveWalls.Add(this);
        Subscribe();
        Hold();
    }

    // 앞장선 레이저나 줄지어 따라오는 레이저 가운데 하나인지
    public bool Owns(LaserBeam laser)
    {
        if (laser == null) return false;
        if (laser == lead) return true;
        foreach (var item in trail)
            if (item == laser) return true;
        return false;
    }

    private Vector2 OriginAt(float distance) => (Vector2)transform.position + TravelDirection * distance;

    // 시작 위치에 앞장선 레이저 하나만 켜 두고 기다린다.
    private void Hold()
    {
        holdRequested = false;
        ClearTrail();
        travel = 0f;
        StopLead();
        lead = Fire(leadLaserPrefab, OriginAt(0f));
        CurrentPhase = Phase.Holding;
        timer = holdDuration;
    }

    private void Update()
    {
        if (holdRequested && CurrentPhase != Phase.Halted)
        {
            Hold();
            return;
        }

        switch (CurrentPhase)
        {
            case Phase.Holding:
                timer -= Time.deltaTime;
                if (timer <= 0f) CurrentPhase = Phase.Chasing;
                break;
            case Phase.Chasing:
                if (LeadLaser == null) return;
                travel = Mathf.Min(maxTravel, travel + speed * Time.deltaTime);
                lead.SetOrigin(OriginAt(travel));
                // 시작 위치에 다음 자리가 생기면 새 레이저가 나와 줄 끝에 붙는다.
                while (trailSpacing > 0f && travel - (trail.Count + 1) * trailSpacing >= 0f)
                {
                    var laser = Fire(trailLaserPrefab != null ? trailLaserPrefab : leadLaserPrefab, OriginAt(travel - (trail.Count + 1) * trailSpacing));
                    if (laser == null) break;
                    trail.Add(laser);
                }
                // 모든 레이저가 앞장선 레이저와 같은 간격을 지키며 일렬로 함께 나아간다.
                for (int i = 0; i < trail.Count; i++)
                    if (trail[i] != null && trail[i].gameObject.activeInHierarchy) trail[i].SetOrigin(OriginAt(travel - (i + 1) * trailSpacing));
                break;
        }
    }

    private LaserBeam Fire(LaserBeam prefab, Vector2 origin)
    {
        if (prefab == null || WorldEntityPool.Instance == null) return null;
        var laser = WorldEntityPool.Instance.Spawn(prefab, origin, Quaternion.identity);
        // 구간이 끝나거나 맞았을 때 직접 회수하므로 자체 수명은 충분히 길게 둔다.
        laser.Fire(laserDamage, origin, beamDirection.normalized, laserLength, 3600f, blockMask, damageMask, laserWidth);
        return laser;
    }

    private void StopLead()
    {
        if (lead != null && lead.gameObject.activeInHierarchy) lead.ForceReturnToPool();
        lead = null;
    }

    private void ClearTrail()
    {
        foreach (var laser in trail)
            if (laser != null && laser.gameObject.activeInHierarchy) laser.ForceReturnToPool();
        trail.Clear();
    }

    // 구간 목표에 닿으면 레이저를 모두 거두고 더 이상 다시 시작하지 않는다.
    public void Halt()
    {
        holdRequested = false;
        StopLead();
        ClearTrail();
        Unsubscribe();
        CurrentPhase = Phase.Halted;
    }

    // 코스 레이저에 맞으면 다음 프레임에 남긴 레이저를 거두고 시작 위치에서 다시 기다린다. 코스 적에게 맞은 피해는 무시한다.
    private void HandlePlayerDamaged(WeaponBaseSO.DamageInfo _)
    {
        if (CurrentPhase == Phase.Halted || !IsCourseLaserHit) return;
        holdRequested = true;
    }

    private void Subscribe()
    {
        Unsubscribe();
        var player = GameManager.Instance != null ? GameManager.Instance.Player : null;
        if (player == null || player.Health == null) return;
        subscribedHealth = player.Health;
        subscribedHealth.OnDamaged += HandlePlayerDamaged;
    }

    private void Unsubscribe()
    {
        if (subscribedHealth != null) subscribedHealth.OnDamaged -= HandlePlayerDamaged;
        subscribedHealth = null;
    }

    public override void ForceReturnToPool()
    {
        Unsubscribe();
        StopLead();
        ClearTrail();
        ActiveWalls.Remove(this);
        base.ForceReturnToPool();
    }

    // 풀 전체 정리로 바로 비활성화될 때도 구독이 남지 않게 한다. 레이저는 같은 정리에서 함께 회수된다.
    private void OnDisable()
    {
        Unsubscribe();
        ActiveWalls.Remove(this);
        lead = null;
        trail.Clear();
    }
}
