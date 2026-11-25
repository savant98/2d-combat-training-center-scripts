using System;
using System.Collections.Generic;
using UnityEngine;

// 컨베이어 벨트(N01, 이동 시설 + 고체 발판). 위에 선 Player와 지상 일반 적을 진행 방향으로 운반한다.
// - 길이(4~46칸)·방향·작동 조건·속도는 배치별 설정(DungeonProp가 모아 격자 편집기에 보인다). 왼쪽 끝은 footprint 왼쪽 변에 고정되고 오른쪽으로 늘어난다.
//   표시는 생성 도구가 만든 '한 칸 조각' Mesh를 이어 붙여 실행 중에 만든다(FacilityMesh.Tile — 크기 늘이기 없음). 고체 상자도 길이에 맞춰 셀 경계에 맞춘다.
// - 운반: 자기 RaycastMotor2D.OverlapAttackBox로 벨트 윗면 위를 묻고(Physics2D 직접 질의 없음), 접지해 있고 발이 벨트 윗면 높이인 대상의
//   externalForce 진행 방향 성분만 목표 속도까지 가속 한도로 채운다(AdminBlackHole 방식 — 덮어쓰지 않으므로 넉백·점프대 옆 발사는 그대로).
//   캐릭터보다 늦게 돌아(DefaultExecutionOrder 100, 점프대와 같음) 다음 틱 이동에 목표 속도가 온전히 들어간다.
// - 제외: 보스·넉백 불가 적(훈련 인형·실드 등)·비행 적(중력 없음)·등장 중(Collider 꺼짐)·대시 중(IgnoreExternalForces)·구속 중·입력 차단 중 Player.
// - 작동 조건: 항상 / 방 클리어 뒤 / 방 클리어 전까지(IDungeonRoomPowered). 던전 밖이면 '클리어 전까지'도 작동한다(방 신호가 없으므로).
// - 움직임: 벨트 돌기(cleat)·파란 화살표가 진행 방향으로 미끄러지고(이동), 끝 드럼·받침 롤러가 돈다(회전), 하류 끝에서 부스러기가 떨어진다(입자).
//   켜지고 꺼질 때 0.45초 동안 가속·감속한다. 구동음은 거리 감쇠 루프(임시 기존 효과음).
[DisallowMultipleComponent]
[DefaultExecutionOrder(100)]
public sealed class PropConveyor : MonoBehaviour, IDungeonPropPart, IDungeonPropSettings, IDungeonRoomPowered
{
    public const string LengthKey = "conveyorLength";
    public const string DirectionKey = "conveyorDirection";
    public const string PowerKey = "conveyorPower";
    public const string SpeedKey = "conveyorSpeed";
    public const int MinLength = 4, MaxLength = 46;
    private static readonly string[] DirectionOptions = { "오른쪽", "왼쪽" };
    private static readonly string[] PowerOptions = { "항상 작동", "방 클리어 뒤 작동", "방 클리어 전까지 작동" };

    public enum PowerMode { Always = 0, AfterClear = 1, UntilClear = 2 }

    [Header("배치 기본값")]
    [SerializeField, Range(MinLength, MaxLength)] private int length = 4;
    [SerializeField, Tooltip("0 = 오른쪽, 1 = 왼쪽")] private int direction;
    [SerializeField] private PowerMode powerMode = PowerMode.Always;
    [SerializeField, Min(.1f), Tooltip("운반 속도(칸/초).")] private float speed = 1.5f;

    [Header("운반")]
    [SerializeField, Min(1f), Tooltip("목표 속도까지 채우는 가속 한도(칸/초²).")] private float acceleration = 40f;
    [SerializeField, Min(.05f), Tooltip("켜지고 꺼질 때 가속·감속 시간(초).")] private float spinUp = .45f;
    [SerializeField] private LayerMask targetMask;
    [SerializeField, Min(.01f), Tooltip("발과 벨트 윗면의 높이 차 허용(칸).")] private float standTolerance = .1f;

    [Header("구성(생성 도구가 연결)")]
    [SerializeField, Min(1)] private int footprintCells = 4;
    [SerializeField] private BoxCollider2D solid;
    [SerializeField, Tooltip("뒤판·아래쪽 벨트(롤러 뒤).")] private MeshFilter body;
    [SerializeField] private Mesh bodyTile;
    [SerializeField, Tooltip("위쪽 벨트·앞 난간·기둥(롤러 앞).")] private MeshFilter front;
    [SerializeField] private Mesh frontTile;
    [SerializeField] private MeshFilter trim;
    [SerializeField] private Mesh trimTile;
    [SerializeField] private MeshFilter cleats;
    [SerializeField] private Mesh cleatTile;
    [SerializeField, Min(.1f)] private float cleatPeriod = .5f;
    [SerializeField] private MeshFilter chevrons;
    [SerializeField] private Mesh chevronRightTile;
    [SerializeField] private Mesh chevronLeftTile;
    [SerializeField] private Transform capRight;
    [SerializeField] private Transform[] drums = Array.Empty<Transform>();
    [SerializeField, Min(.05f)] private float drumRadius = .3f;
    [SerializeField] private Transform rollerTemplate;
    [SerializeField, Min(.05f)] private float rollerRadius = .16f;
    [SerializeField] private ParticleSystem debris;
    [SerializeField, Tooltip("작동 표시(하류 표시등 등) — 작동 중 밝게, 멈추면 어둡게.")] private Renderer[] runLights = Array.Empty<Renderer>();
    [SerializeField, Tooltip("파란 화살표 — 멈추면 흐리게.")] private Renderer[] chevronLights = Array.Empty<Renderer>();

    [Header("소리(임시: 기존 효과음)")]
    [SerializeField] private SoundOptions runLoop;

    private int activeLength;
    private int activeDirection = 1;
    private PowerMode activeMode;
    private float activeSpeed;
    private bool cached;
    private bool live;
    private bool roomSignal;
    private bool roomCleared = true;
    private float ramp;
    private float phase;
    private float rollerSpin;
    private Vector3 cleatHome, chevronHome, capRightHome, debrisHome;
    private Quaternion[] drumHome = Array.Empty<Quaternion>();
    private Mesh bodyMesh, frontMesh, trimMesh, cleatMesh, chevronMesh;
    private readonly List<Transform> rollers = new List<Transform>();
    private readonly List<float> rollerHomeZ = new List<float>();
    private Renderer[] visibilityProbes = Array.Empty<Renderer>();
    private FacilityLights runLamp, chevronLamp;
    private readonly FacilityLoopSound loop = new FacilityLoopSound();
    private RaycastMotor2D motor;
    private Collider2D[] hits = new Collider2D[16];
    private readonly Dictionary<CharacterController2D, float> riders = new Dictionary<CharacterController2D, float>();
    private readonly List<CharacterController2D> riderOrder = new List<CharacterController2D>();

    public int Length => cached ? activeLength : length;
    public int Direction => activeDirection;
    public float Speed => activeSpeed;
    public PowerMode Mode => activeMode;
    /// <summary>0~1 가동 정도(가속·감속 포함).</summary>
    public float Ramp => ramp;
    public bool IsRunning => live && Running;
    /// <summary>운반한 대상 수(검사용, 대여마다 0).</summary>
    public int PushCount { get; private set; }
    /// <summary>벨트 윗면 높이(월드).</summary>
    public float TopY => transform.TransformPoint(new Vector3(0f, .5f, 0f)).y;
    /// <summary>벨트 왼쪽·오른쪽 끝(월드 x).</summary>
    public float LeftX => transform.TransformPoint(new Vector3(LeftLocal, 0f, 0f)).x;
    public float RightX => transform.TransformPoint(new Vector3(LeftLocal + Length, 0f, 0f)).x;
    private float LeftLocal => -footprintCells * .5f;

    private bool Running
    {
        get
        {
            switch (activeMode)
            {
                case PowerMode.AfterClear: return roomCleared;
                case PowerMode.UntilClear: return !roomSignal || !roomCleared;
                default: return true;
            }
        }
    }

    private void Awake()
    {
        Cache();
        motor = GetComponent<RaycastMotor2D>();
        if (motor == null) motor = gameObject.AddComponent<RaycastMotor2D>();   // 정적 공격 질의만 쓴다
        if (targetMask.value == 0) targetMask = LayerMask.GetMask("Player", "Enemy");
    }

    private void Cache()
    {
        if (cached) return;
        cached = true;
        if (cleats != null) cleatHome = cleats.transform.localPosition;
        if (chevrons != null) chevronHome = chevrons.transform.localPosition;
        if (capRight != null) capRightHome = capRight.localPosition;
        if (debris != null) debrisHome = debris.transform.localPosition;
        drumHome = new Quaternion[drums.Length];
        for (int i = 0; i < drums.Length; i++) drumHome[i] = drums[i] != null ? drums[i].localRotation : Quaternion.identity;
        if (rollerTemplate != null) { rollers.Add(rollerTemplate); rollerHomeZ.Add(rollerTemplate.localPosition.z); }
        runLamp = new FacilityLights(runLights);
        chevronLamp = new FacilityLights(chevronLights);
        var prop = GetComponent<DungeonProp>();
        var root = prop != null && prop.VisualRoot != null ? prop.VisualRoot : transform;
        visibilityProbes = root.GetComponentsInChildren<Renderer>(true);
        ReadSettings(WaveDeviceSettings.None);
    }

    private void OnDestroy()
    {
        loop.Stop();
        FacilityMesh.Release(ref bodyMesh);
        FacilityMesh.Release(ref frontMesh);
        FacilityMesh.Release(ref trimMesh);
        FacilityMesh.Release(ref cleatMesh);
        FacilityMesh.Release(ref chevronMesh);
    }

    private void OnDisable()
    {
        loop.Stop();
        if (debris != null) debris.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
    }

    // ── 수명 ─────────────────────────────

    public void OnPropSpawned(DungeonProp prop)
    {
        Cache();
        live = true;
        roomSignal = false;
        roomCleared = true;
        phase = 0f;
        rollerSpin = 0f;
        PushCount = 0;
        ramp = Running ? 1f : 0f;   // 방에 들어오면 이미 돌고 있다
        for (int i = 0; i < drums.Length; i++) if (drums[i] != null) drums[i].localRotation = drumHome[i];
        if (debris != null) debris.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        ApplyMotion(0f);
        ApplyLights();
    }

    public void OnPropReturning(DungeonProp prop)
    {
        live = false;
        ramp = 0f;
        loop.Stop();
        riders.Clear();
        riderOrder.Clear();
        if (debris != null) debris.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        runLamp?.Restore();
        chevronLamp?.Restore();
    }

    public void SetRoomPowered(bool powered, bool instant)
    {
        Cache();
        roomSignal = true;
        roomCleared = powered;
        if (instant) ramp = Running ? 1f : 0f;
        ApplyLights();
    }

    // ── 표시 ─────────────────────────────

    private bool Visible()
    {
        foreach (var probe in visibilityProbes)
            if (probe != null && probe.enabled && probe.isVisible) return true;
        return false;
    }

    private void Update()
    {
        if (!live) return;
        float target = Running ? 1f : 0f;
        ramp = Mathf.MoveTowards(ramp, target, Time.deltaTime / Mathf.Max(.05f, spinUp));
        ApplyLights();
        bool visible = Visible();
        loop.Update(runLoop, transform.TransformPoint(new Vector3(LeftLocal + activeLength * .5f, 0f, 0f)), ramp > .01f, ramp);
        if (debris != null) FacilityFeedback.Emit(debris, visible && ramp > .5f);
        if (!visible) return;
        ApplyMotion(Time.deltaTime);
    }

    private void ApplyMotion(float dt)
    {
        float travel = activeDirection * activeSpeed * ramp * dt;
        phase += travel;
        if (cleats != null) cleats.transform.localPosition = cleatHome + new Vector3(Mathf.Repeat(phase, cleatPeriod), 0f, 0f);
        if (chevrons != null) chevrons.transform.localPosition = chevronHome + new Vector3(Mathf.Repeat(phase, 1f), 0f, 0f);
        // 벨트가 오른쪽으로 가면 위를 지나는 드럼·롤러는 시계 방향(음수)으로 돈다.
        float drumTurn = -travel / drumRadius * Mathf.Rad2Deg;
        for (int i = 0; i < drums.Length; i++) if (drums[i] != null) drums[i].localRotation *= Quaternion.Euler(0f, 0f, drumTurn);
        rollerSpin += -travel / rollerRadius * Mathf.Rad2Deg;
        var spin = Quaternion.Euler(0f, 0f, rollerSpin);
        for (int i = 0; i < rollers.Count; i++) if (rollers[i] != null && rollers[i].gameObject.activeSelf) rollers[i].localRotation = spin;
    }

    private void ApplyLights()
    {
        runLamp?.Set(Mathf.Lerp(.15f, 1f, ramp));
        chevronLamp?.Set(Mathf.Lerp(.25f, 1f, ramp));
    }

    // ── 운반 ─────────────────────────────

    private void FixedUpdate()
    {
        if (!live || ramp <= 0f || motor == null) return;
        float left = LeftX, right = RightX, top = TopY;
        Vector2 center = new Vector2((left + right) * .5f, top + .35f);
        Vector2 size = new Vector2(Mathf.Abs(right - left), .7f);
        int count;
        do
        {
            count = motor.OverlapAttackBox(center, size, 0f, hits, targetMask);
            if (count < hits.Length) break;
            Array.Resize(ref hits, hits.Length * 2);
        } while (true);

        // 대상마다 가장 낮은 Collider 바닥(= 발)을 모은다.
        riders.Clear();
        riderOrder.Clear();
        for (int i = 0; i < count; i++)
        {
            var hit = hits[i];
            hits[i] = null;
            if (hit == null) continue;
            var character = hit.GetComponentInParent<CharacterController2D>();
            if (character == null) continue;
            // 발 높이는 캐릭터 루트의 몸통 Collider(모터가 쓰는 것) 바닥으로 잰다. 질의에 걸린 Collider는 피격 판정(Player의 HitModel 등)이라
            // 발보다 0.15칸쯤 위에서 시작해 허용 오차를 넘는다(FacilityAccess.BodyRect와 같은 기준).
            float bottom = character.TryGetComponent(out Collider2D body) && body.enabled ? body.bounds.min.y : hit.bounds.min.y;
            if (riders.TryGetValue(character, out float lowest)) { if (bottom < lowest) riders[character] = bottom; }
            else { riders.Add(character, bottom); riderOrder.Add(character); }
        }

        float dt = Time.fixedDeltaTime;
        float targetSpeed = activeSpeed * ramp;
        foreach (var character in riderOrder)
        {
            if (!CanCarry(character)) continue;
            if (Mathf.Abs(riders[character] - top) > standTolerance) continue;
            float x = character.transform.position.x;
            if (x < Mathf.Min(left, right) || x > Mathf.Max(left, right)) continue;
            float along = character.externalForce.x * activeDirection;
            if (along >= targetSpeed) continue;
            character.externalForce.x += activeDirection * Mathf.Min(targetSpeed - along, acceleration * dt);
            PushCount++;
        }
    }

    private static bool CanCarry(CharacterController2D character)
    {
        if (character == null || !character.isActiveAndEnabled || character.IgnoreExternalForces || !character.IsGrounded) return false;
        if (character.Motor != null && character.Motor.IsConfined) return false;
        if (character is PlayerController player)
            return player.CanReceiveInput && (player.Health == null || !player.Health.IsDead);
        if (character is EnemyController enemy)
        {
            var health = enemy.Health;
            if (health == null || health.currentHP <= 0f || health.IsBossEnemy) return false;
            return enemy.CanReceiveWeaponKnockback && enemy.GravityOn;
        }
        return false;
    }

    // ── 배치별 설정 ─────────────────────────────

    private WaveDeviceParameter LengthParameter => new WaveDeviceParameter(LengthKey, "길이", "벨트 길이(칸)입니다. 왼쪽 끝은 그대로 두고 오른쪽으로 늘어납니다. 늘어난 칸에는 다른 것을 놓지 마세요.", "칸", MinLength, MaxLength, 1f, length, true);
    private WaveDeviceParameter DirectionParameter => new WaveDeviceParameter(DirectionKey, "방향", "위에 선 대상을 옮기는 방향입니다.", DirectionOptions, direction, true);
    private WaveDeviceParameter PowerParameter => new WaveDeviceParameter(PowerKey, "작동 조건", "항상 / 방을 클리어한 뒤 / 방을 클리어하기 전까지(전투 중 위험 요소) 중 언제 도는지 정합니다. 던전 밖에서는 늘 돕니다.", PowerOptions, (int)powerMode);
    private WaveDeviceParameter SpeedParameter => new WaveDeviceParameter(SpeedKey, "속도", "운반 속도입니다.", "칸/초", .5f, 4f, .1f, speed);

    public WaveDeviceParameter[] PropParameters => new[] { LengthParameter, DirectionParameter, PowerParameter, SpeedParameter };

    public void ApplyPropSettings(WaveDeviceSetting[] overrides)
    {
        ReadSettings(overrides);
        // 표시·고체 크기는 실행 중 개체에만 바꾼다(편집기 미리보기·Prefab 에셋은 건드리지 않는다).
        if (Application.isPlaying && cached) Rebuild();
    }

    private void ReadSettings(WaveDeviceSetting[] overrides)
    {
        activeLength = Mathf.RoundToInt(WaveDeviceSettings.Value(overrides, LengthParameter));
        activeDirection = Mathf.RoundToInt(WaveDeviceSettings.Value(overrides, DirectionParameter)) == 1 ? -1 : 1;
        activeMode = (PowerMode)Mathf.Clamp(Mathf.RoundToInt(WaveDeviceSettings.Value(overrides, PowerParameter)), 0, 2);
        activeSpeed = WaveDeviceSettings.Value(overrides, SpeedParameter);
    }

    public bool TryGetPropRange(WaveDeviceSetting[] overrides, out WaveDeviceRange range)
    {
        int cells = Mathf.RoundToInt(WaveDeviceSettings.Value(overrides, LengthParameter));
        range = new WaveDeviceRange(WaveEnvironmentTool.RangeShape.Box, new Vector2(LeftLocal + cells * .5f, 0f), new Vector2(cells, 1f), new Color(.2f, .55f, 1f, .9f));
        return true;
    }

    // 길이·방향에 맞춰 고체 상자·표시 Mesh·끝 덮개·롤러·부스러기 자리를 다시 놓는다.
    private void Rebuild()
    {
        int cells = Mathf.Clamp(activeLength, MinLength, MaxLength);
        float left = LeftLocal;
        if (solid != null)
        {
            solid.offset = new Vector2(left + cells * .5f, 0f);
            solid.size = new Vector2(cells, 1f);
        }
        if (body != null && bodyTile != null) body.sharedMesh = bodyMesh = FacilityMesh.Tile(bodyMesh, bodyTile, cells, new Vector3(left, 0f, 0f), Vector3.right, name + "_Body");
        if (front != null && frontTile != null) front.sharedMesh = frontMesh = FacilityMesh.Tile(frontMesh, frontTile, cells, new Vector3(left, 0f, 0f), Vector3.right, name + "_Front");
        if (trim != null && trimTile != null) trim.sharedMesh = trimMesh = FacilityMesh.Tile(trimMesh, trimTile, cells, new Vector3(left, 0f, 0f), Vector3.right, name + "_Trim");
        if (cleats != null && cleatTile != null)
        {
            int count = Mathf.Max(1, Mathf.RoundToInt((cells - cleatPeriod) / cleatPeriod));
            cleats.sharedMesh = cleatMesh = FacilityMesh.Tile(cleatMesh, cleatTile, count, new Vector3(left - cleatHome.x, 0f, 0f), new Vector3(cleatPeriod, 0f, 0f), name + "_Cleats");
        }
        var chevronTile = activeDirection > 0 ? chevronRightTile : chevronLeftTile;
        if (chevrons != null && chevronTile != null)
            chevrons.sharedMesh = chevronMesh = FacilityMesh.Tile(chevronMesh, chevronTile, cells - 1, new Vector3(left - chevronHome.x, 0f, 0f), Vector3.right, name + "_Chevrons");
        if (capRight != null) capRight.localPosition = capRightHome + new Vector3(cells - footprintCells, 0f, 0f);
        LayoutRollers(cells, left);
        if (debris != null)
        {
            float x = activeDirection > 0 ? left + cells + .1f : left - .1f;
            debris.transform.localPosition = new Vector3(x, debrisHome.y, debrisHome.z);
            var velocity = debris.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.Local;
            velocity.x = new ParticleSystem.MinMaxCurve(activeDirection * .5f, activeDirection * 1.1f);
            velocity.y = new ParticleSystem.MinMaxCurve(0f, 0f);
            velocity.z = new ParticleSystem.MinMaxCurve(0f, 0f);
        }
        var prop = GetComponent<DungeonProp>();
        var root = prop != null && prop.VisualRoot != null ? prop.VisualRoot : transform;
        visibilityProbes = root.GetComponentsInChildren<Renderer>(true);
        RaycastMotor2D.InvalidateAttackQuerySync();
    }

    // 받침 롤러: 칸 경계 사이(가운데)마다 하나, 길면 두 칸마다. 템플릿을 복제해 쓰고 남는 것은 끈다.
    private void LayoutRollers(int cells, float left)
    {
        if (rollerTemplate == null) return;
        int step = cells > 20 ? 2 : 1;
        int needed = 0;
        for (int c = 1; c < cells - 1; c += step) needed++;
        while (rollers.Count < needed)
        {
            var clone = Instantiate(rollerTemplate, rollerTemplate.parent, false);
            clone.name = rollerTemplate.name + "_" + rollers.Count;
            rollers.Add(clone);
            rollerHomeZ.Add(rollerTemplate.localPosition.z);
        }
        int index = 0;
        for (int c = 1; c < cells - 1; c += step, index++)
        {
            var roller = rollers[index];
            roller.gameObject.SetActive(true);
            roller.localPosition = new Vector3(left + c + .5f, rollerTemplate.localPosition.y, rollerHomeZ[index]);
        }
        for (; index < rollers.Count; index++) if (rollers[index] != null) rollers[index].gameObject.SetActive(false);
    }
}
