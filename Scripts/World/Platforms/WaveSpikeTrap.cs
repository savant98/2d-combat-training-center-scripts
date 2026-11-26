using System.Collections.Generic;
using UnityEngine;

// 별도 동작이나 효과 없이 가시 영역에 닿은 플레이어에게 피해를 줍니다.
// 6차 T9(던전 방 클리어 정지, IDungeonRoomHazard): 정지 신호를 받은 호출에서 피해 판정을 끄고, 정전 물결 차례가 오면 날을 받침 쪽으로 접는다
// (등장 Clip 첫 자세의 날 각도·경첩을 읽어 회전·이동만으로 접음 — 크기 변형 없음). 다 접히면 날의 빨강(Damage 재질 Renderer)을 끄고 배선 흐름을 세운다.
// 조립 등장이 끝나기 전에 온 신호는 기억했다가 등장 완료 때 바로 접힌 자세로 들어간다. 신호가 없으면(던전 밖 일반 Wave) 늘 작동한다.
[DisallowMultipleComponent]
public sealed class WaveSpikeTrap : WorldEntityBase, IWaveDeviceSettings, IDungeonRoomHazard
{
    [SerializeField, Min(0f)] private float damage = 10f;
    private readonly WaveTrapDamage trapDamage = new WaveTrapDamage();
    public WaveDeviceParameter[] Parameters => new[] { WaveTrapDamage.DamageParameter(damage), WaveTrapDamage.ReturnParameter };
    public bool SettingsChangeShape => false;
    public void ApplySettings(WaveDeviceSetting[] overrides) => trapDamage.ApplySettings(overrides, damage);
    public bool TryGetRange(WaveDeviceSetting[] overrides, out WaveDeviceRange range) { range = default; return false; }
    [SerializeField] private Vector2 damageAreaOffset = new Vector2(0f, -.15f);
    [SerializeField] private Vector2 damageAreaSize = new Vector2(.9f, .7f);
    [SerializeField] private LayerMask targetMask;
    [SerializeField, Min(.01f)] private float interval = .1f;
    // 6차 T9: 방 클리어 정지 때 날을 받침 쪽으로 접는 시간(초, 판단값). 직렬화하지 않는다(Wave 장치 Prefab 파일을 바꾸지 않게).
    private const float FoldTime = .3f;

    private RaycastMotor2D motor;
    private WaveEnvironmentLifecycle lifecycle;
    private Collider2D[] hits = new Collider2D[8];
    private readonly HashSet<PlayerHealth> damagedPlayers = new();
    private float nextDamageTime;

    // 접을 날 부품 하나: 기준 자세와, 등장 Clip 첫 자세에서 읽은 경첩(부모 좌표)·접는 각도(도).
    private struct FoldPart
    {
        public Transform target;
        public Vector3 restPosition;
        public Quaternion restRotation;
        public Vector2 hinge;
        public float angle;
    }
    private static readonly string[] FoldPrefixes = { "Blade", "ActuatorRod", "RackTeeth" };
    private readonly HazardHalt halt = new HazardHalt();
    private HazardRendererSwitch red;
    private WaveMachineMotion machine;
    private FoldPart[] folds;
    private float appliedFold = -1f;

    /// <summary>방 클리어 정지 신호를 받아 판정이 꺼져 있다(6차 T9, 검사용).</summary>
    public bool IsHalted => halt.Halted;
    /// <summary>날이 다 접히고 빨강이 꺼진 정지 자세(검사용).</summary>
    public bool InHaltedPose => halt.Halted && halt.Blend >= 1f && red != null && !red.IsOn;
    /// <summary>날의 빨강이 켜져 있는지(검사용).</summary>
    public bool DangerLit => red == null || red.IsOn;
    /// <summary>접히는 날 부품 수(검사용 — 등장 Clip에서 읽은 것).</summary>
    public int FoldPartCount => folds != null ? folds.Length : 0;
    /// <summary>날이 접힌 정도(0 = 선 자세 ~ 1 = 접힘, 검사용).</summary>
    public float FoldAmount => halt.Blend;

    protected override void Awake()
    {
        base.Awake();
        ApplySettings(WaveDeviceSettings.None);
        lifecycle = GetComponent<WaveEnvironmentLifecycle>();
        machine = GetComponent<WaveMachineMotion>();
        motor = GetComponent<RaycastMotor2D>();
        if (motor == null) motor = gameObject.AddComponent<RaycastMotor2D>();
        if (targetMask.value == 0) targetMask = LayerMask.GetMask("Player");
    }

    private void OnEnable() => nextDamageTime = 0f;

    private void OnDisable()
    {
        trapDamage.Cancel();
        ResetHalt();
    }

    public override void OnSpawnedFromPool()
    {
        base.OnSpawnedFromPool();
        trapDamage.Cancel();
        ApplySettings(WaveDeviceSettings.None);
        nextDamageTime = 0f;
        ResetHalt();
        if (lifecycle != null) lifecycle.BeginSpawn(OnReady);
    }

    public override void ForceReturnToPool()
    {
        if (isReturning) return;
        trapDamage.Cancel();
        if (lifecycle != null) lifecycle.BeginDespawn(ReturnAfterDespawn);
        else base.ForceReturnToPool();
    }

    private void ReturnAfterDespawn() => base.ForceReturnToPool();

    // 풀 재사용·꺼짐: 정지 신호를 잊고(던전 밖 = 작동) 빨강·배선 흐름을 처음 상태로 둔다. 날 자세는 등장 Clip(WaveEnvironmentLifecycle)이 되돌린다.
    private void ResetHalt()
    {
        halt.Reset();
        appliedFold = -1f;
        red?.Restore();
        if (machine != null) machine.Frozen = false;
    }

    private bool Ready => lifecycle == null || lifecycle.IsReady;

    // 조립 등장이 끝난 순간(기준 자세로 돌아온 직후). 등장 중에 받은 정지 신호는 여기서 바로 접힌 자세로 든다.
    private void OnReady()
    {
        if (!halt.Halted) return;
        halt.Signal(true, true);   // 등장 중에 받은 정지는 바로 끝 자세(Blend 1·지연 0)로 든다 — LateUpdate가 이 값으로 매 프레임 접는다
        CacheFolds();
        ApplyFold(1f);
        red?.Set(false);
        if (machine != null) machine.Frozen = true;
    }

    // ── 6차 T9: 방 클리어 정지 ─────────────────────────────

    public void SetHazardHalted(bool halted, bool instant)
    {
        bool changed = halt.Signal(halted, instant);
        if (halted)
        {
            trapDamage.Cancel();   // 판정은 이 호출에서 바로 끈다(FixedUpdate가 묻지 않는다)
            if (instant && Ready && isActiveAndEnabled)
            {
                CacheFolds();
                ApplyFold(1f);
                red?.Set(false);
                if (machine != null) machine.Frozen = true;
            }
            return;
        }
        if (!changed) return;
        // 다시 작동(재도전 등): 빨강·배선 흐름을 되돌리고 날은 Update가 다시 세운다(instant면 바로).
        red?.Restore();
        if (machine != null) machine.Frozen = false;
        if (instant && Ready && isActiveAndEnabled) { CacheFolds(); ApplyFold(0f); }
    }

    private void Update()
    {
        if (!halt.HasSignal || !Ready || (!halt.Halted && halt.Blend <= 0f && appliedFold <= 0f)) return;
        halt.Step(Time.deltaTime, FoldTime, out bool started, out bool settled);
        if (halt.Halted && !halt.VisualHalting) return;   // 정전 물결 차례 전
        if (started && machine != null) machine.Frozen = true;
        CacheFolds();
        if (settled && halt.Halted) red?.Set(false);
    }

    // 접기는 Animation 단계 뒤에 쓴다: 등장이 끝나면 Animator(Ready 상태 — Motion 없음·WriteDefaultValues 켬)가 Update 뒤 Animation 단계에서
    // 등장 Clip에 바인딩된 날 부품(Blade·BladeRim·ActuatorRod·RackTeeth)에 기준 자세를 매 프레임 다시 쓴다. 그래서 정지 중에는 매 프레임 다시 접는다.
    private void LateUpdate()
    {
        if (folds == null || !Ready) return;                  // 등장·퇴장 중에는 Lifecycle이 자세를 맡는다
        if (halt.Blend <= 0f && appliedFold <= 0f) return;    // 선 자세로 다 돌아왔으면 Animator 기본값에 맡긴다
        ApplyFold(halt.Blend);
    }

    // 등장 Clip 첫 자세(날이 접힌 자세)에서 날 부품마다 접는 각도와 경첩을 한 번 읽는다(크기는 읽지 않는다 — 회전·이동만으로 접는다).
    // Clip을 샘플링한 뒤 모든 시각 부품의 자세를 되돌린다(기준 자세일 때만 부른다 — 등장 완료 뒤).
    private void CacheFolds()
    {
        if (folds != null) return;
        var root = lifecycle != null && lifecycle.visualRoot != null ? lifecycle.visualRoot : transform.Find("DesignV3");
        if (red == null) red = HazardRendererSwitch.ByMaterial(root != null ? root : transform, "Damage");
        var clip = lifecycle != null ? lifecycle.spawnAnimation : null;
        if (root == null || clip == null) { folds = System.Array.Empty<FoldPart>(); return; }
        var targets = new List<Transform>();
        foreach (Transform child in root)
        {
            string n = child.name;
            if (n.StartsWith("BladeSocket", System.StringComparison.Ordinal)) continue;
            foreach (string prefix in FoldPrefixes)
                if (n.StartsWith(prefix, System.StringComparison.Ordinal)) { targets.Add(child); break; }
        }
        var all = root.GetComponentsInChildren<Transform>(true);
        var saved = new (Vector3 p, Quaternion r, Vector3 s)[all.Length];
        for (int i = 0; i < all.Length; i++) saved[i] = (all[i].localPosition, all[i].localRotation, all[i].localScale);
        var rest = new (Vector3 p, Quaternion r)[targets.Count];
        for (int i = 0; i < targets.Count; i++) rest[i] = (targets[i].localPosition, targets[i].localRotation);
        var folded = new (Vector3 p, Quaternion r)[targets.Count];
        try
        {
            clip.SampleAnimation(gameObject, 0f);
            for (int i = 0; i < targets.Count; i++) folded[i] = (targets[i].localPosition, targets[i].localRotation);
        }
        finally
        {
            for (int i = 0; i < all.Length; i++)
                if (all[i] != null) { all[i].localPosition = saved[i].p; all[i].localRotation = saved[i].r; all[i].localScale = saved[i].s; }
        }
        var result = new List<FoldPart>();
        for (int i = 0; i < targets.Count; i++)
        {
            float angle = Mathf.DeltaAngle(0f, (Quaternion.Inverse(rest[i].r) * folded[i].r).eulerAngles.z);
            if (Mathf.Abs(angle) < 1f) continue;
            // 접힌 자세 = 경첩 h를 중심으로 angle만큼 돈 자세: folded = R(rest - h) + h → (I - R) h = folded - R·rest.
            float rad = angle * Mathf.Deg2Rad, c = Mathf.Cos(rad), s = Mathf.Sin(rad);
            Vector2 restP = rest[i].p, foldP = folded[i].p;
            Vector2 d = foldP - new Vector2(c * restP.x - s * restP.y, s * restP.x + c * restP.y);
            float a = 1f - c, b = s, det = a * a + b * b;   // I - R = [[1-c, s], [-s, 1-c]]
            if (det < 1e-6f) continue;
            var hinge = new Vector2((a * d.x - b * d.y) / det, (b * d.x + a * d.y) / det);
            result.Add(new FoldPart { target = targets[i], restPosition = rest[i].p, restRotation = rest[i].r, hinge = hinge, angle = angle });
        }
        folds = result.ToArray();
    }

    // amount(0~1)만큼 경첩을 중심으로 돌려 접는다(회전·이동만).
    private void ApplyFold(float amount)
    {
        if (folds == null) return;
        float k = amount * amount * (3f - 2f * amount);
        appliedFold = k;   // 같은 값이어도 다시 쓴다(Animator가 매 프레임 기준 자세로 덮는다 — LateUpdate)
        foreach (var part in folds)
        {
            if (part.target == null) continue;
            float rad = part.angle * k * Mathf.Deg2Rad, c = Mathf.Cos(rad), s = Mathf.Sin(rad);
            Vector2 r = (Vector2)part.restPosition - part.hinge;
            Vector2 p = part.hinge + new Vector2(c * r.x - s * r.y, s * r.x + c * r.y);
            part.target.localPosition = new Vector3(p.x, p.y, part.restPosition.z);
            part.target.localRotation = Quaternion.Euler(0f, 0f, part.angle * k) * part.restRotation;
        }
    }

    private void FixedUpdate()
    {
        if (lifecycle != null && !lifecycle.IsReady) return;
        if (halt.Halted) return;
        if (Time.time < nextDamageTime || (trapDamage.Damage <= 0f && !trapDamage.ReturnToStart)) return;
        nextDamageTime = Time.time + Mathf.Max(.01f, interval);
        var scale = transform.lossyScale;
        Vector2 size = new Vector2(Mathf.Abs(damageAreaSize.x * scale.x), Mathf.Abs(damageAreaSize.y * scale.y));
        Vector2 center = transform.TransformPoint(damageAreaOffset);
        int count;
        do
        {
            count = motor.OverlapAttackBox(center, size, transform.eulerAngles.z, hits, targetMask);
            if (count < hits.Length) break;
            System.Array.Resize(ref hits, hits.Length * 2);
        } while (true);

        damagedPlayers.Clear();
        var info = new WeaponBaseSO.DamageInfo { damage = damage, damageType = DamageType.Normal };
        for (int i = 0; i < count; i++)
        {
            if (hits[i] == null) continue;
            var player = hits[i].GetComponentInParent<PlayerHealth>();
            if (player == null || !damagedPlayers.Add(player)) continue;
            trapDamage.TryDamage(this, player, info, hits[i].ClosestPoint(center));
        }
    }
}
