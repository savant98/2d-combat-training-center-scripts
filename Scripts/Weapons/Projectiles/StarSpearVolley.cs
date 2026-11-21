using System;
using System.Collections.Generic;
using UnityEngine;

// StarSpear 일제 사격(2026-11-08): Player 머리 위 발사점(별 문양)을 조금 늦게 따라다니며 별 창을 소환·유지·조준·발사한다.
// - 따라다니기: 발사점은 매 고정 단계 (Player 위치 + 발사점 보정 - 지금 자리) × followRate × 단계 시간만큼 다가간다(PlayerDrone과 같은 식).
//   판정이 없는 외형이라 모터 없이 자리만 옮긴다. 창도 이 발사점을 기준으로 놓이므로 무리 전체가 살짝 늦게 따라온다.
//   Player가 followSnapDistance보다 멀어지면(방 이동 등 순간 이동) 맵을 가로질러 날아오지 않게 바로 붙는다(대시 중 지연은 약 3.3칸).
// - 소환: StarSpearWeaponSO.SummonTime(i)마다 창 하나를 부채꼴 자리(발사점에서 fanRadius, 뒤쪽 → 앞쪽 순서)에 꺼낸다.
// - 유지: 매 고정 단계 발사점 + 자리로 창을 놓는다(StarSpear.Hold, 판정 없음). 창끝은 바깥(부채꼴 반지름 방향)을 향한다.
// - 조준·발사 순서: 소환 순서대로 한 창씩 조준을 시작한다. 첫 창은 FirstAimTime부터, 다음 창은 앞 창이 조준을 시작하고 launchInterval 뒤부터.
//   조준을 시작하려는 순간 창 자리에서 targetSearchRadius 안에 적이 없으면 시작하지 않고 기다린다(기다린 시간을 누적한다).
//   기다린 시간이 noTargetWaitTime에 이르면 더 기다리지 않고 Player가 보는 쪽(수평)을 조준한다.
//   조준은 aimTime 동안 창끝을 목표(판정 상자 중심) 쪽으로 돌리고 조금 뒤로 당기며, 끝나는 순간 목표까지의 직선 방향으로 Launch한다.
//   조준 중 목표가 사라지면 가장 가까운 적을 다시 찾고, 없으면(기다릴 시간이 남았을 때) 이 창부터 조준을 멈추고 다시 기다린다.
// - 끝: 모든 창을 쏜 뒤 발사점 별 문양이 줄어들며 사라지고 풀로 돌아간다. Player가 사라지면 남은 창을 거두고 끝낸다.
// - 레일건 충전 추가타(2026-11-21 무장 특성 3차 → 4차 개편, 사용자 요청 "별창 발사 시의 추가타도 플레이어 주위에서 시간차로, 별창 이펙트의 미니 사이즈로"
//   — 사용자 화면 검수 전 시안): 충전 상태(PlayerWeaponTraits.IsRailCharged)로 시작한 일제 사격이면 본 창을 쏠 때마다 본 창 뒤
//   echoInterval(0.2·0.4초)에 miniSpearCount(2)발의 미니 별창을 RailEcho로 쏜다. 미니 별창은 머리 위 발사점이 아니라 Player 몸 위쪽·아래쪽 바깥에
//   작은 별 문양(RailgunWeaponSO.miniSpearShotPrefab)과 함께 나타나 본 창의 목표(사라졌으면 Player 둘레의 가장 가까운 적, 없으면 본 창이 날아간 쪽)로
//   날아간다. 본 창마다 다른 자리부터 써서 겹치지 않는다. 피해 = 별창 × miniSpearDamageScale, 폭발 반경 = 별창 × miniSpearRadiusScale.
//   미니 별창을 다 쏠 때까지(또는 버려질 때까지) 일제 사격이 남는다(검증 값 MiniLaunchedCount를 읽을 수 있게). Player가 쓰러지거나
//   무장 특성이 초기화되면 남은 미니 별창은 버린다(RailEchoShot이 스스로 확인).
// 시간은 고정 단계로 센다(프레임 수와 무관). Player 이동(FixedUpdate) 뒤에 자리를 잡도록 실행 순서를 탄(100)보다 늦춘다.
[DefaultExecutionOrder(110)]
[UnityEngine.Scripting.APIUpdating.MovedFrom(false, sourceNamespace: "", sourceAssembly: "Assembly-CSharp", sourceClassName: "StarCannonVolley")]
public class StarSpearVolley : WorldEntityBase
{
    [Header("Sigil")]
    [SerializeField] private Transform sigil;
    [SerializeField] private Transform sigilStar;
    [SerializeField, Min(0.01f)] private float sigilAppear = 0.15f;
    [SerializeField, Min(0.01f)] private float sigilFade = 0.25f;
    [SerializeField] private float sigilSpin = -90f;
    [SerializeField, Min(0f)] private float sigilPulse = 0.3f;

    [Header("Hold")]
    [SerializeField, Min(0f)] private float bobAmplitude = 0.05f;
    [SerializeField, Min(0f)] private float bobFrequency = 1.5f;
    [SerializeField, Min(0f)] private float aimPullBack = 0.18f;
    [SerializeField, Min(0f)] private float followSnapDistance = 10f;

    private const float Epsilon = 0.0001f;

    private sealed class Slot
    {
        public StarSpear spear;
        public float angle, summonAt, launchAt = -1f;
        public bool summoned, launched, aiming;
        public Collider2D target;
        public float aimBlend, aimAngle;                      // 조준 정도(0 = 부채꼴 자리, 1 = 목표 쪽)와 마지막 조준 각도
        public float summonedAge = -1f, launchedAge = -1f;   // 실제로 소환·발사한 시각(일제 사격 시작 기준)
        public Vector2 launchPosition, launchDirection, launchTargetPoint;
    }

    private readonly List<Slot> slots = new List<Slot>(8);
    private RailgunWeaponSO rail;
    private int miniSerial;          // 풀에서 다시 쓰일 때 늦게 온 미니 별창 알림을 버리기 위한 번호
    private int pendingMinis;        // 아직 쏘지 않은(버려지지도 않은) 미니 별창
    private float lastMiniQueuedAt = -10f;
    private StarSpearWeaponSO weapon;
    private Action<CharacterController2D, Vector2> onHitEnemy;
    private float age, finishedAt = -1f, lastSummonAt = -10f, nextAimAt, waited;
    private bool running;
    private bool railCharged;
    private Vector2 anchor;
    private Vector3 sigilScale = Vector3.one;

    public int SummonedCount { get; private set; }
    public int LaunchedCount { get; private set; }
    public float Age => age;
    public bool IsRunning => running && !isReturning;
    public bool IsWaiting { get; private set; }     // 이번 고정 단계에 주변에 적이 없어 조준을 미뤘는가
    public float WaitedTime => waited;              // 적이 없어 기다린 시간(누적)
    public Vector2 AnchorPosition => anchor;        // 지금 발사점(부채꼴 중심, Player를 늦게 따라감)
    public int SlotCount => slots.Count;
    public bool IsRailCharged => railCharged;               // 레일건 충전 상태로 시작했는가
    public int MiniLaunchedCount { get; private set; }      // 쏜 미니 별창 수
    public int PendingMiniCount => pendingMinis;            // 아직 쏘지 않은 미니 별창 수
    public StarSpear SpearAt(int index) => index >= 0 && index < slots.Count ? slots[index].spear : null;
    public float SlotAngle(int index) => index >= 0 && index < slots.Count ? slots[index].angle : 0f;
    // 검증·디버그용 읽기 전용 기록: 소환·발사 시각(초, 아직이면 -1), 발사 방향, 발사 순간 목표 판정 상자 중심(목표가 없으면 NaN)
    public float SummonedAgeOf(int index) => index >= 0 && index < slots.Count ? slots[index].summonedAge : -1f;
    public float LaunchedAgeOf(int index) => index >= 0 && index < slots.Count ? slots[index].launchedAge : -1f;
    public Vector2 LaunchPositionOf(int index) => index >= 0 && index < slots.Count ? slots[index].launchPosition : Vector2.zero;
    public Vector2 LaunchDirectionOf(int index) => index >= 0 && index < slots.Count ? slots[index].launchDirection : Vector2.zero;
    public Vector2 LaunchTargetPointOf(int index) => index >= 0 && index < slots.Count ? slots[index].launchTargetPoint : new Vector2(float.NaN, float.NaN);
    public float AimBlendOf(int index) => index >= 0 && index < slots.Count ? slots[index].aimBlend : 0f;   // 0 = 부채꼴 자리, 1 = 목표 쪽으로 다 돎

    protected override void Awake()
    {
        base.Awake();
        if (sigil == null) sigil = transform.Find("Sigil");
        if (sigilStar == null && sigil != null) sigilStar = sigil.Find("Star");
        if (sigil != null) sigilScale = sigil.localScale;
    }

    public override void OnSpawnedFromPool()
    {
        base.OnSpawnedFromPool();
        running = false;
        slots.Clear();
        rail = null;
        miniSerial++;
        pendingMinis = 0;
        lastMiniQueuedAt = -10f;
        MiniLaunchedCount = 0;
        age = waited = 0f;
        finishedAt = -1f;
        lastSummonAt = -10f;
        IsWaiting = false;
        SummonedCount = LaunchedCount = 0;
        if (sigil != null) sigil.localScale = Vector3.zero;
    }

    public void Begin(CharacterController2D owner, StarSpearWeaponSO weapon, WeaponBaseSO.DamageInfo damageInfo,
        Action<CharacterController2D, Vector2> onHitEnemy)
    {
        var traits = WeaponTraitEffects.Attack(owner)?.Traits;
        railCharged = traits != null && traits.IsRailCharged;
        rail = railCharged ? RailgunWeaponSO.FindFor(owner) : null;
        miniSerial++;
        pendingMinis = 0;
        lastMiniQueuedAt = -10f;
        MiniLaunchedCount = 0;
        Initialize(owner, damageInfo);   // 발사점 등장 피드백
        this.weapon = weapon;
        this.onHitEnemy = onHitEnemy;
        int facing = owner != null && owner.Facing < 0 ? -1 : 1;
        slots.Clear();
        for (int i = 0; i < weapon.spearCount; i++)
        {
            float angle = weapon.SlotAngle(i, facing);
            slots.Add(new Slot { angle = angle, aimAngle = angle, summonAt = weapon.SummonTime(i) });
        }
        anchor = owner != null ? (Vector2)owner.transform.position + weapon.firePointOffset : (Vector2)transform.position;
        transform.position = anchor;
        age = waited = 0f;
        finishedAt = -1f;
        lastSummonAt = -10f;
        nextAimAt = weapon.FirstAimTime;
        IsWaiting = false;
        SummonedCount = LaunchedCount = 0;
        running = true;
        Step(0f);   // 창은 실행 첫 프레임에 바로 소환한다
    }

    private void FixedUpdate()
    {
        if (!running || isReturning) return;
        Step(Time.fixedDeltaTime);
    }

    private void Step(float dt)
    {
        if (owner == null || !owner.gameObject.activeInHierarchy)
        {
            Abort();
            return;
        }
        age += dt;
        // PlayerDrone처럼 남은 거리에 비례해 다가간다(정확히 붙지 않고 조금 늦게 따라온다).
        Vector2 desired = (Vector2)owner.transform.position + weapon.firePointOffset;
        if ((desired - anchor).sqrMagnitude > followSnapDistance * followSnapDistance) anchor = desired;
        else anchor += (desired - anchor) * Mathf.Min(1f, weapon.followRate * dt);
        transform.position = anchor;

        for (int i = 0; i < slots.Count; i++)
        {
            var slot = slots[i];
            if (!slot.summoned && age >= slot.summonAt - Epsilon) Summon(slot, i);
            if (slot.summoned && !slot.launched && (slot.spear == null || !slot.spear.IsHeld))
                slot.launched = true;   // 풀 일괄 회수 등으로 이미 사라진 창은 건너뛴다.
        }
        Schedule(dt);
        bool allLaunched = true;
        for (int i = 0; i < slots.Count; i++)
        {
            var slot = slots[i];
            if (slot.launched) continue;
            if (slot.summoned) UpdateHeld(slot, i, dt);
            if (!slot.launched) allLaunched = false;
        }
        if (!allLaunched || WaitingForMinis()) return;   // 미니 별창을 다 쏠 때까지 남는다
        if (finishedAt < 0f) finishedAt = age;
        if (age - finishedAt >= sigilFade)
        {
            running = false;
            ReturnToPool();
        }
    }

    // 다음 창(소환 순서대로 아직 조준하지 않은 첫 창)의 조준을 시작할지 정한다. 주변에 적이 없으면 기다린다.
    private void Schedule(float dt)
    {
        IsWaiting = false;
        if (age < nextAimAt - Epsilon) return;
        Slot next = null;
        int index = -1;
        for (int i = 0; i < slots.Count; i++)
        {
            var slot = slots[i];
            if (slot.launched || slot.aiming) continue;
            if (!slot.summoned) return;
            next = slot;
            index = i;
            break;
        }
        if (next == null) return;
        Collider2D target = next.spear.FindNearestTarget(HeldPosition(next, index), weapon.targetSearchRadius);
        if (target == null && waited < weapon.noTargetWaitTime - Epsilon)
        {
            IsWaiting = true;
            waited += dt;
            return;
        }
        next.target = target;
        next.aiming = true;
        next.launchAt = age + weapon.aimTime;
        nextAimAt = age + weapon.launchInterval;
    }

    private void Summon(Slot slot, int index)
    {
        slot.summoned = true;
        if (weapon.spearPrefab == null || WorldEntityPool.Instance == null)
        {
            slot.launched = true;
            return;
        }
        Vector2 position = anchor + Direction(slot.angle) * weapon.fanRadius;
        var spear = WorldEntityPool.Instance.Spawn(weapon.spearPrefab, position, Quaternion.identity);
        spear.Initialize(owner, damageInfo, weapon.spearSpeed, index, weapon.platformLayer);
        spear.RailCharged = railCharged;
        spear.ConfigureFlight(weapon.turnRateDegrees, weapon.maxFlightTime, weapon.explosionRadius, weapon.targetSearchRadius);
        if (onHitEnemy != null) spear.OnHitEnemy += (character, hitPoint) => onHitEnemy(character, hitPoint);
        spear.Hold(position, slot.angle);
        slot.spear = spear;
        slot.summonedAge = age;
        SummonedCount++;
        lastSummonAt = age;
    }

    private Vector2 HeldPosition(Slot slot, int index)
    {
        float bob = Mathf.Sin((age * bobFrequency + index * 0.23f) * Mathf.PI * 2f) * bobAmplitude;
        return anchor + Direction(slot.angle) * weapon.fanRadius + Vector2.up * bob;
    }

    private void UpdateHeld(Slot slot, int index, float dt)
    {
        Vector2 position = HeldPosition(slot, index);
        if (slot.aiming && !StarSpear.IsTrackable(slot.target))
        {
            slot.target = slot.spear.FindNearestTarget(position, weapon.targetSearchRadius);
            if (slot.target == null && waited < weapon.noTargetWaitTime - Epsilon) CancelAim(index);
        }
        if (slot.aiming) slot.aimAngle = AimAngle(slot, position);
        slot.aimBlend = Mathf.MoveTowards(slot.aimBlend, slot.aiming ? 1f : 0f, weapon.aimTime > 0f ? dt / weapon.aimTime : 1f);
        float k = Smooth01(slot.aimBlend);
        float angle = Mathf.LerpAngle(slot.angle, slot.aimAngle, k);
        position -= Direction(angle) * (aimPullBack * k);
        slot.spear.Hold(position, angle);
        if (!slot.aiming || age < slot.launchAt - Epsilon) return;
        // 발사 순간 목표까지의 직선 방향(목표가 없으면 Player가 보는 쪽)
        Collider2D target = StarSpear.IsTrackable(slot.target) ? slot.target : null;
        slot.launchPosition = position;
        slot.launchDirection = Direction(AimAngle(slot, position));
        slot.launchTargetPoint = target != null ? (Vector2)target.bounds.center : new Vector2(float.NaN, float.NaN);
        slot.launchedAge = age;
        slot.aiming = false;
        slot.spear.Launch(slot.launchDirection, target);
        slot.launched = true;
        LaunchedCount++;
        QueueMinis(index, target, slot.launchPosition, slot.launchDirection);
    }

    // ===== 레일건 충전 추가타: 미니 별창(4차 — Player 둘레에서 시간차로) =====
    private void QueueMinis(int index, Collider2D target, Vector2 launchPosition, Vector2 direction)
    {
        if (!railCharged || rail == null || rail.miniSpearPrefab == null || rail.miniSpearShotPrefab == null || rail.miniSpearCount <= 0) return;
        if (owner is not PlayerController player) return;
        var info = damageInfo;
        info.damage *= rail.miniSpearDamageScale;
        info.knockbackFacing = 0;
        int serial = miniSerial;
        int queued = RailEcho.Begin(new RailEchoRequest
        {
            owner = player,
            rail = rail,
            kind = RailEchoKind.Spear,
            prefab = rail.miniSpearShotPrefab,
            damage = info,
            count = rail.miniSpearCount,
            interval = rail.echoInterval,
            slotBase = index * rail.miniSpearCount,
            facing = player.Facing,
            target = target,
            // 목표가 사라졌고 Player 둘레에도 적이 없으면 본 창이 날아간 쪽(최대 비행 거리의 절반 지점)으로
            fallbackPoint = launchPosition + direction * (weapon.spearSpeed * weapon.maxFlightTime * 0.5f),
            searchDirection = Vector2.zero,
            searchRange = weapon.targetSearchRadius,
            platformLayer = weapon.platformLayer,
            damageMask = rail.damageMask,
            spearWeapon = weapon,
            spearPrefab = rail.miniSpearPrefab,
            spearRadiusScale = rail.miniSpearRadiusScale,
            spearIndex = index,
            onFired = (shot, hits) =>
            {
                if (serial != miniSerial) return;
                MiniLaunchedCount++;
                pendingMinis = Mathf.Max(0, pendingMinis - 1);
            },
            onDropped = shot =>
            {
                if (serial == miniSerial) pendingMinis = Mathf.Max(0, pendingMinis - 1);
            },
        });
        pendingMinis += queued;
        if (queued > 0) lastMiniQueuedAt = age;
    }

    // 미니 별창을 아직 기다리는가. 알림이 끊겨도 영영 남지 않도록 마지막 예약 뒤 (발 수 + 1) × 간격 + 0.5초가 지나면 그만 기다린다.
    private bool WaitingForMinis()
    {
        if (pendingMinis <= 0 || rail == null) return false;
        return age - lastMiniQueuedAt < (rail.miniSpearCount + 1) * rail.echoInterval + 0.5f;
    }

    // index 창부터 조준을 멈추고(부채꼴 자리로 천천히 돌아감) 다음 단계에 다시 적을 찾는다.
    private void CancelAim(int index)
    {
        for (int i = index; i < slots.Count; i++)
        {
            var slot = slots[i];
            if (slot.launched || !slot.aiming) continue;
            slot.aiming = false;
            slot.target = null;
            slot.launchAt = -1f;
        }
        nextAimAt = Mathf.Min(nextAimAt, age);
    }

    private float AimAngle(Slot slot, Vector2 from)
    {
        if (StarSpear.IsTrackable(slot.target))
        {
            Vector2 delta = (Vector2)slot.target.bounds.center - from;
            if (delta.sqrMagnitude > 0.000001f) return Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg;
        }
        return owner != null && owner.Facing < 0 ? 180f : 0f;
    }

    private void Abort()
    {
        foreach (var slot in slots)
            if (slot.spear != null && slot.spear.IsHeld) slot.spear.ForceReturnToPool();
        miniSerial++;   // 남은 미니 별창은 RailEchoShot이 Player 상태를 보고 스스로 버린다
        pendingMinis = 0;
        running = false;
        ReturnToPool();
    }

    private void Update()
    {
        if (sigil == null) return;
        if (!running)
        {
            sigil.localScale = Vector3.zero;
            return;
        }
        // 나타날 때 조금 넘쳤다 제자리, 창이 소환될 때마다 맥동, 마지막 창을 쏜 뒤 줄어들며 사라짐
        float t = Mathf.Clamp01(age / sigilAppear);
        float s = t < 0.7f ? Mathf.Lerp(0f, 1.2f, 1f - Mathf.Pow(1f - t / 0.7f, 3f)) : Mathf.Lerp(1.2f, 1f, (t - 0.7f) / 0.3f);
        if (finishedAt >= 0f) s *= 1f - Smooth01((age - finishedAt) / sigilFade);
        float pulse = 1f + sigilPulse * Mathf.Exp(-(age - lastSummonAt) / 0.08f);
        sigil.localScale = sigilScale * (s * pulse);
        if (sigilStar != null) sigilStar.localRotation = Quaternion.Euler(0f, 0f, age * sigilSpin);
    }

    private static Vector2 Direction(float angleDegrees)
    {
        float radians = angleDegrees * Mathf.Deg2Rad;
        return new Vector2(Mathf.Cos(radians), Mathf.Sin(radians));
    }

    private static float Smooth01(float t)
    {
        t = Mathf.Clamp01(t);
        return t * t * (3f - 2f * t);
    }
}
