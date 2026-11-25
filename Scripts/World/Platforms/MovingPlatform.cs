using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 움직이는 발판(MovingPlatform 레이어 17번의 Collider). 위에서만 서는 발판(OneWay와 같은 판정)이고, 위에 선 Player를 운반한다.
/// 한 물리 틱의 순서:
/// 1. 계획 — 소유 시설이 FixedUpdate(순서 −100)에서 <see cref="Plan"/>으로 이번 틱 이동량을 정한다(Transform은 아직 그대로).
/// 2. 캐릭터 — 운반을 지원하는 모터(RaycastMotor2D.SupportsMovingPlatforms, 지금은 Player만)가 이 발판 위에 서 있으면
///    자기 이동 뒤에 <see cref="PendingDelta"/>를 '운반 이동'으로 더한다(벽·천장·아래 지형 충돌 검사 포함). 이때 발판 Collider는 틱 시작 자리다.
/// 3. 적용 — 이 컴포넌트의 FixedUpdate(순서 100)가 발판 자신의 Transform만 옮긴다(DESIGN_V3의 유일한 위치 직접 변경 예외).
///    물리 시뮬레이션 단계가 Transform을 동기화하므로 다음 틱 질의는 새 자리를 본다.
/// 계획과 적용을 나누는 까닭: 자동 동기화가 꺼져 있어(Physics2DSettings) 같은 틱 안에서 누가 SyncTransforms를 불렀는지에 따라
/// 발판 Collider 자리가 달라지면 안 된다. 나누면 캐릭터가 볼 때 발판은 늘 틱 시작 자리에 있다.
/// 구성: 이 컴포넌트가 붙은 Transform이 움직이는 몸이다. 그 아래(자신 포함) MovingPlatform 레이어 Collider가 서는 면이다.
/// 새 레이어는 Main Camera가 그리지 않으므로 그 레이어 개체에는 Renderer를 두지 않는다(외형은 같은 Transform 아래 Default 레이어 개체).
/// 화면은 FixedMotionBody(자동 부착)가 Player와 같은 방식으로 물리 틱 사이를 보간해 그린다. Rigidbody2D는 붙이지 않는다(트리거 콜백 방지).
/// 계획하지 않은 틱에는 움직이지 않는다(계획은 그 틱에만 쓰인다).
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(100)]
public sealed class MovingPlatform : MonoBehaviour
{
    // "MovingPlatform" 레이어 번호. OneWayPlatform.SurfaceLayer와 같이 처음 쓸 때 한 번만 찾는다(직렬화 중 NameToLayer 호출 회피).
    private static int surfaceLayer = int.MinValue;
    // 켜져 있는 발판의 Collider → 발판. 모터가 바닥 Collider로 운반할 발판을 찾는다(GetComponentInParent 없이).
    private static readonly Dictionary<Collider2D, MovingPlatform> owners = new();

    /// <summary>"MovingPlatform" 레이어 번호(없으면 -1).</summary>
    public static int SurfaceLayer
    {
        get
        {
            if (surfaceLayer == int.MinValue)
                surfaceLayer = LayerMask.NameToLayer("MovingPlatform");
            return surfaceLayer;
        }
    }

    // Domain Reload를 끈 Play 반복에서도 Play마다 다시 찾고 등록표를 비운다.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetSurfaceLayer()
    {
        surfaceLayer = int.MinValue;
        owners.Clear();
    }

    /// <summary>그 Collider가 운반 발판 면(MovingPlatform 레이어)인지. 레이어가 없으면 늘 false. 모터는 이 레이어를 위에서만 서는 면으로 본다.</summary>
    public static bool IsSurface(Collider2D collider)
        => collider != null && SurfaceLayer >= 0 && collider.gameObject.layer == SurfaceLayer;

    /// <summary>그 Collider를 가진 켜진 발판을 찾는다(등록표 조회, 물리 질의 없음). 레이어만 맞고 발판이 없으면 false(서기만 하고 운반은 없다).</summary>
    public static bool TryGet(Collider2D collider, out MovingPlatform platform)
    {
        platform = null;
        return collider != null && owners.TryGetValue(collider, out platform) && platform != null;
    }

    private readonly List<Collider2D> colliders = new();
    private Vector2 plannedDelta;
    private bool planned;

    /// <summary>이번 물리 틱 이동량(계획 단계에서 정해짐, 월드). 계획하지 않았거나 이미 적용했으면 0.</summary>
    public Vector2 PendingDelta => planned ? plannedDelta : Vector2.zero;

    /// <summary>이번(또는 마지막으로 적용한) 틱 이동 속도(월드 칸/초). 계획 없이 지난 틱에는 0.</summary>
    public Vector2 Velocity { get; private set; }

    /// <summary>이번 틱 계획이 아직 적용되지 않고 남아 있는지(Plan 뒤 ~ 순서 100의 적용 전).</summary>
    public bool HasPendingPlan => planned;

    /// <summary>
    /// 소유 시설이 FixedUpdate(순서 −100, <c>[DefaultExecutionOrder(-100)]</c>)에서 이번 틱 이동량을 정한다. 같은 틱에 다시 부르면 마지막 값이 쓰인다.
    /// 캐릭터가 읽은 뒤(순서 0 이후)에 부르면 그 틱에 운반되지 않은 채 발판만 움직이므로 반드시 캐릭터보다 먼저 부른다.
    /// </summary>
    public void Plan(Vector2 delta)
    {
        if (float.IsNaN(delta.x) || float.IsNaN(delta.y) || float.IsInfinity(delta.x) || float.IsInfinity(delta.y)) delta = Vector2.zero;
        plannedDelta = delta;
        planned = true;
        float dt = Time.fixedDeltaTime;
        Velocity = dt > 0f ? delta / dt : Vector2.zero;
    }

    /// <summary>Player가 이 발판 위에 서 있는지(모터 GroundCollider 기준, 물리 질의 없음). 점프·낙하 중이면 false.</summary>
    public bool HasRider(PlayerController player)
    {
        if (player == null || player.Motor == null) return false;
        return TryGet(player.Motor.GroundCollider, out var platform) && platform == this;
    }

    /// <summary>
    /// 발판을 순간이동시킨다(배치·풀 재사용·정지점 맞추기). 남은 계획을 지우고 화면 보간도 새 자리에서 시작한다.
    /// 위에 선 캐릭터는 옮기지 않는다(그 캐릭터는 다음 틱에 발판을 잃고 떨어진다). 움직이는 동안의 이동은 Plan으로 한다.
    /// </summary>
    public void Warp(Vector3 position)
    {
        planned = false;
        plannedDelta = Vector2.zero;
        Velocity = Vector2.zero;
        transform.position = position;
        if (TryGetComponent<FixedMotionBody>(out var body)) body.ResetPose();
    }

    /// <summary>발판 아래 Collider를 다시 모은다(실행 중에 서는 면 Collider를 더하거나 뺐을 때).</summary>
    public void RefreshColliders()
    {
        Unregister();
        if (isActiveAndEnabled) Register();
    }

    private void Awake()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        // 새 레이어 개체의 Renderer는 Main Camera가 그리지 않아 보이지 않는다(외형은 Default 레이어에 둔다).
        if (SurfaceLayer >= 0)
            foreach (var renderer in GetComponentsInChildren<Renderer>(true))
                if (renderer.gameObject.layer == SurfaceLayer)
                    Debug.LogWarning($"MovingPlatform: MovingPlatform 레이어 개체 '{renderer.name}'에 Renderer가 있습니다(카메라가 그리지 않음).", renderer);
#endif
    }

    private void OnEnable()
    {
        Register();
        FixedMotionBody.Attach(transform);
    }

    private void OnDisable()
    {
        Unregister();
        planned = false;
        plannedDelta = Vector2.zero;
        Velocity = Vector2.zero;
    }

    private void Register()
    {
        colliders.Clear();
        GetComponentsInChildren(true, colliders);
        foreach (var collider in colliders)
            if (collider != null) owners[collider] = this;
    }

    private void Unregister()
    {
        foreach (var collider in colliders)
            if (collider != null && owners.TryGetValue(collider, out var owner) && owner == this) owners.Remove(collider);
        colliders.Clear();
    }

    // 발판 적용(순서 100): 운반을 지원하는 캐릭터 모터가 모두 이번 틱 운반 양을 읽은 뒤 발판 자신만 옮긴다.
    private void FixedUpdate()
    {
        if (!planned)
        {
            Velocity = Vector2.zero;
            return;
        }
        planned = false;
        if (plannedDelta != Vector2.zero) transform.position += (Vector3)plannedDelta;
    }
}
