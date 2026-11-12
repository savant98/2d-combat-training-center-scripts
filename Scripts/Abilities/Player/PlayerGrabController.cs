using UnityEngine;

// 공격 소유자·세션 번호를 검증해 잡기 이동과 전용 피해만 허용한다.
public sealed class PlayerGrabController : MonoBehaviour
{
    private PlayerController player;
    private PlayerGrabVisual visual;
    private Object owner;
    private uint version;
    private PlayerGrabDataSO data;
    private bool gravity, ignoreExternal, lockFacing;
    private float protectionUntil;
    private bool damageApplied;
    private CinematicLetterbox.Lease letterbox;
    private RoomManager roomManager;
    public bool IsActive { get; private set; }
    public bool BlocksOtherDamage => IsActive || Time.time < protectionUntil;
    public GrabCinematicPresentation Presentation { get; private set; }
    public Object Owner => owner;
    public uint Session => version;
    private void Awake()
    {
        player = GetComponent<PlayerController>();
        visual = gameObject.AddComponent<PlayerGrabVisual>(); visual.Initialize(player);
        player.Health.OnPlayerDeath += HandleDeath;
    }
    private void Start()
    {
        roomManager = RoomManager.Instance;
        if (roomManager != null) roomManager.OnRoomChanged += HandleRoomChanged;
    }
    public bool TryBegin(Object attacker, PlayerGrabDataSO settings, out uint session)
    {
        session = 0;
        // Wave 준비 중 구속 상자에 갇힌 동안에는 잡히지 않는다(갇힌 동안의 피해 차단·무장 유지와 같은 기준).
        if (IsActive || !player.isActiveAndEnabled || !player.CanReceiveInput || WavePlayerStart.IsHolding(player) || attacker == null || settings == null || settings.presentationPrefab == null || settings.restrainedLoop == null || settings.wireMaterial == null
            || settings.letterboxPrefab == null || player.Health.currentHP <= 0
            || (RoomTransitionManager.Instance != null && RoomTransitionManager.Instance.IsTransitioning)) return false;
        // 반격 창이 열려 있으면 잡기도 다른 공격처럼 반격으로 받아낸다. 잡기는 실패하고 공격자는 빗나간 경로로 간다.
        if (player.PlayerCounterState != null && player.PlayerCounterState.TryCounter(AttackerPosition(attacker))) return false;
        player.AbilityManager.InterruptAllAbilities();
        // 이전 공격의 자식 표시 키를 중립으로 돌린 뒤 타격 Collider를 정리한다.
        if (player.animator.enabled) { player.animator.Rebind(); player.animator.Update(0); }
        player.AbilityManager.AttackAbility.CancelForGrab();
        player.AbilityManager.JumpAbility.ClearPendingInput();
        player.AbilityManager.DashAbility.ClearPendingInput();
        player.AbilityManager.HitAbility.CancelForGrab();
        gravity = player.GravityOn; ignoreExternal = player.IgnoreExternalForces; lockFacing = player.LockFacing;
        owner = attacker; data = settings; version++; if (version == 0) version++;
        session = version; IsActive = true; damageApplied = false;
        player.GravityOn = false; player.IgnoreExternalForces = true; player.LockFacing = true;
        player.Velocity = player.externalForce = Vector2.zero;
        player.AbilityManager.StateMachine.SetState(PlayerState.Grabbed);
        player.AbilityManager.StateMachine.SetAttackPhase(AbilityState.None);
        visual.Begin(settings);
        Presentation = Instantiate(settings.presentationPrefab);
        Presentation.Acquire(attacker);
        letterbox = CinematicLetterbox.Acquire(settings.letterboxPrefab, attacker);
        return true;
    }
    private Vector3 AttackerPosition(Object attacker)
    {
        if (attacker is Component component) return component.transform.position;
        if (attacker is GameObject go) return go.transform.position;
        return player.transform.position;
    }
    public bool IsOwnedBy(Object attacker, uint session) => IsActive && attacker != null && owner != null && owner == attacker && version == session;
    public bool SetVelocity(Object attacker, uint session, Vector2 velocity)
    {
        if (!IsOwnedBy(attacker, session)) return false;
        player.Velocity = velocity; return true;
    }
    public void SetVisualRotation(Object attacker, uint session, float degrees)
    { if (IsOwnedBy(attacker, session)) visual.SetRotation(degrees); }
    public bool Complete(Object attacker, uint session, WeaponBaseSO.DamageInfo damage, Vector3 position)
    {
        if (!IsOwnedBy(attacker, session)) return false;
        bool applied = ApplyDamage(attacker, session, damage, position);
        Release(attacker, session); return applied;
    }
    // 충격 연출과 화면 복귀가 끝날 때까지 포박을 유지할 수 있도록 피해와 해제를 나눈다.
    public bool ApplyDamage(Object attacker, uint session, WeaponBaseSO.DamageInfo damage, Vector3 position)
    {
        if (!IsOwnedBy(attacker, session) || damageApplied) return false;
        damageApplied = true;
        return player.Health.TakeGrabDamage(attacker, session, damage, position);
    }
    public bool HasLetterbox => letterbox != null;
    /// <summary>소유 공격의 레터박스만 해제하고 잡기·입력 차단은 유지한다.</summary>
    public bool HideLetterbox(Object attacker,uint session,float duration=.2f)
    {
        if(!IsOwnedBy(attacker,session)) return false;
        letterbox?.Release(duration); letterbox=null; return true;
    }
    public void Release(Object attacker, uint session) { if (IsOwnedBy(attacker, session)) Abort(); }
    public void Abort(bool resetProtection = false)
    {
        if (IsActive)
        {
            IsActive = false; owner = null;
            protectionUntil = Time.time + (data != null ? data.releaseProtectionTime : 0);
            player.GravityOn = gravity; player.IgnoreExternalForces = ignoreExternal; player.LockFacing = lockFacing;
            player.Velocity = player.externalForce = Vector2.zero;
            player.AbilityManager.StateMachine.SetState(PlayerState.Idle);
            visual.End();
            if (Presentation != null) Presentation.Release(); Presentation = null;
            letterbox?.Dispose(); letterbox = null;
        }
        if (resetProtection) protectionUntil = 0;
    }
    private void Update()
    {
        if (!IsActive) return;
        var component = owner as Component;
        if (owner == null || (owner is GameObject go && !go.activeInHierarchy) || (component != null && !component.gameObject.activeInHierarchy)
            || (owner is Behaviour behaviour && !behaviour.isActiveAndEnabled)
            || (owner is AiAbility ability && !ability.IsRunning)) Abort();
    }
    private void HandleDeath() => Abort();
    private void HandleRoomChanged(RoomType room) => Abort(true);
    private void OnDisable() => Abort(true);
    private void OnDestroy()
    {
        if (player != null && player.Health != null) player.Health.OnPlayerDeath -= HandleDeath;
        if (roomManager != null) roomManager.OnRoomChanged -= HandleRoomChanged;
    }
}
