using System;
using UnityEngine;

// 적 생성 연출 앵커(IDungeonSpawnAnchor) 공통 부품 — 제작 부스(N02 '조립 → 셔터 → 배출'), 수리 거치대(N07 '깨어남'), 중계 비컨(N14 '전송').
// - DungeonManager가 적 구성 항목을 생성할 때 그 위치가 WorldArea(footprint + 여유) 안이면 PlaySpawn을 부른다(데이터 연결 없음, concepts §7-7).
//   적 자신의 등장 연출(EnemySpawnController — 포털 효과 → 아래에서 위로 드러남, 약 3초)과 같은 시간에 앵커가 뒤에서 함께 움직인다.
// - 한 시간표(0 → openAt → ejectAt → closeAt → duration)를 세 형태가 함께 쓴다. 연결하지 않은 부품은 건너뛴다.
//   · 관절(arms): 평소 작게 흔들리다(idleSwing) 작업 자세(workAngle)로 돌아가 떨리고(workJitter), ejectAt 뒤 제자리로.
//   · 미끄럼 묶음(slides): openAt부터 열린 자리로 옮겨지고(셔터 조각이 지붕 뒤로 겹쳐 말려 들어감 등), closeAt부터 돌아온다(slidesReturn).
//   · 회전체(spinners): 평소 속도 → 작업 속도. · 작업 조명(workLights): 평소 밝기 → 깜박이는 작업 밝기. · 섬광(flashLights): ejectAt에 번쩍.
//   · 숨김(hideOnSpawn): hideAt에 숨는다(수리 중이던 모형이 실제 적으로 '깨어남'). 풀에서 다시 꺼내면 돌아온다.
//   · 입자: 작업 불꽃(workSparks)은 작업 동안, 배출 입자(ejectBurst)는 ejectAt에 한 번, 평소 불꽃(idleSparks)은 idleSoundInterval마다.
// - 움직임은 회전·이동·켜고 끄기·밝기만(크기 변형 없음). idle은 차분하게(장식이 상호작용·위험처럼 보이지 않게, concepts §6-6), 생성 때만 밝힌다.
// - 소리(임시 기존 효과음): startSound/startFeedback(0초), openSound(openAt), ejectSound/ejectFeedback(ejectAt), idleSound(간헐). 모두 거리 감쇠.
[DisallowMultipleComponent]
public sealed class DungeonSpawnAnchor : MonoBehaviour, IDungeonSpawnAnchor, IDungeonSpentAnchor, IDungeonPropPart
{
    public enum Style { Fabricate = 0, Wake = 1, Transmit = 2 }

    [Serializable]
    public struct Joint
    {
        public Transform target;
        [Tooltip("평소 흔들림 폭(도). 0이면 가만히 있다.")] public float idleSwing;
        [Tooltip("평소 흔들림 주기(초).")] public float idlePeriod;
        [Tooltip("작업 자세 = 처음 자세 + 이 각도(도).")] public float workAngle;
        [Tooltip("작업 중 떨림 폭(도).")] public float workJitter;
        [Tooltip("켜면 생성 뒤에도 작업 자세로 남는다(풀어진 집게 등). 풀에서 다시 꺼내면 처음 자세.")] public bool stayOpen;
    }

    [Serializable]
    public struct Slide
    {
        public Transform target;
        [Tooltip("열린 자리 = 처음 자리 + 이 거리(부품 부모 좌표).")] public Vector2 travel;
        [Tooltip("열리기 시작하는 지연(초, openAt 기준). 셔터 조각을 차례로 말아 올릴 때 쓴다.")] public float delay;
    }

    [SerializeField] private Style style = Style.Fabricate;

    [Header("생성 위치 판정")]
    [SerializeField, Tooltip("판정 사각형 = footprint(WaveEnvironmentTool) + 둘레 여유(칸).")] private Vector2 areaMargin = new Vector2(.25f, .25f);

    [Header("시간표(초)")]
    [SerializeField, Min(.5f)] private float duration = 3.3f;
    [SerializeField, Min(0f)] private float armTime = .3f;
    [SerializeField, Min(0f)] private float openAt = .7f;
    [SerializeField, Min(.05f)] private float openTime = .55f;
    [SerializeField, Min(0f)] private float hideAt = 1.4f;
    [SerializeField, Min(0f)] private float ejectAt = 2.6f;
    [SerializeField, Min(0f)] private float closeAt = 2.9f;
    [SerializeField, Min(.05f)] private float closeTime = .4f;

    [Header("부품(생성 도구가 연결)")]
    [SerializeField] private Joint[] arms = Array.Empty<Joint>();
    [SerializeField] private Slide[] slides = Array.Empty<Slide>();
    [SerializeField, Tooltip("끝나면 미끄럼 묶음이 처음 자리로 돌아오는지(부스 셔터 = 돌아옴, 거치대 집게 = 열린 채).")] private bool slidesReturn = true;
    [SerializeField] private Transform[] spinners = Array.Empty<Transform>();
    [SerializeField] private float idleSpin;
    [SerializeField] private float workSpin = 240f;
    [SerializeField] private Renderer[] workLights = Array.Empty<Renderer>();
    [SerializeField, Min(0f), Tooltip("평소 작업 조명 밝기(0이면 꺼짐).")] private float idleWorkLevel = .25f;
    [SerializeField, Min(0f)] private float workLevel = 1.8f;
    [SerializeField] private Renderer[] flashLights = Array.Empty<Renderer>();
    [SerializeField, Min(0f)] private float flashBoost = 2.2f;
    [SerializeField, Min(.05f)] private float flashTime = .45f;
    [SerializeField] private GameObject hideOnSpawn;
    [SerializeField] private ParticleSystem[] workSparks = Array.Empty<ParticleSystem>();
    [SerializeField] private ParticleSystem[] ejectBurst = Array.Empty<ParticleSystem>();
    [SerializeField] private ParticleSystem idleSparks;

    [Header("효과(임시: 기존 효과음)")]
    [SerializeField] private SoundOptions startSound;
    [SerializeField] private FeedbackDataSO startFeedback;
    [SerializeField] private SoundOptions openSound;
    [SerializeField] private SoundOptions ejectSound;
    [SerializeField] private FeedbackDataSO ejectFeedback;
    [SerializeField] private SoundOptions idleSound;
    [SerializeField, Tooltip("평소 간헐음·불꽃 간격(초, 최소~최대). x ≤ 0이면 없음.")] private Vector2 idleSoundInterval = new Vector2(0f, 0f);

    private Quaternion[] armHome = Array.Empty<Quaternion>();
    private Vector3[] slideHome = Array.Empty<Vector3>();
    private HazardLights work, flash;
    private WaveEnvironmentTool tool;
    private Renderer[] probes = Array.Empty<Renderer>();
    private float clock;           // 평소 움직임 시계
    private float sequence = -1f;  // 생성 연출 시간(음수 = 쉬는 중)
    private float spinSpeed;
    private float nextIdleSound;
    private bool openedSound, ejected, hidden;
    private bool cached;
    private bool live;

    public Style Kind => style;
    public bool IsPlaying => sequence >= 0f;
    public float SequenceTime => sequence;
    /// <summary>이번 대여에서 PlaySpawn이 불린 수(검사용).</summary>
    public int SpawnCount { get; private set; }
    public EnemyController LastEnemy { get; private set; }
    public bool ModelHidden => hidden;
    public float Duration => duration;

    /// <summary>적 생성 위치 판정 사각형(월드). 회전(90° 단위)을 따라 가로·세로가 바뀐다.</summary>
    public Rect WorldArea
    {
        get
        {
            if (tool == null) tool = GetComponent<WaveEnvironmentTool>();
            Vector2 size = tool != null ? (Vector2)tool.Footprint : Vector2.one;
            int steps = Mathf.RoundToInt(transform.eulerAngles.z / 90f) & 3;
            if (steps % 2 == 1) size = new Vector2(size.y, size.x);
            size += areaMargin * 2f;
            Vector2 center = transform.position;
            return new Rect(center - size * .5f, size);
        }
    }

    private void Awake() => Cache();

    private void Cache()
    {
        if (cached) return;
        cached = true;
        tool = GetComponent<WaveEnvironmentTool>();
        armHome = new Quaternion[arms.Length];
        for (int i = 0; i < arms.Length; i++) armHome[i] = arms[i].target != null ? arms[i].target.localRotation : Quaternion.identity;
        slideHome = new Vector3[slides.Length];
        for (int i = 0; i < slides.Length; i++) slideHome[i] = slides[i].target != null ? slides[i].target.localPosition : Vector3.zero;
        work = new HazardLights(workLights);
        flash = new HazardLights(flashLights);
    }

    // ── IDungeonPropPart ─────────────────────────────

    public void OnPropSpawned(DungeonProp prop)
    {
        Cache();
        live = true;
        if (probes.Length == 0) probes = PropHazardKit.VisualRenderers(this);
        SpawnCount = 0;
        LastEnemy = null;
        sequence = -1f;
        Vector3 p = transform.position;
        clock = Mathf.Repeat(p.x * .53f + p.y * .29f, 10f);   // 같은 방의 앵커가 한 박자로 흔들리지 않게
        spinSpeed = idleSpin;
        ResetPose();
        ScheduleIdleSound();
    }

    public void OnPropReturning(DungeonProp prop)
    {
        live = false;
        sequence = -1f;
        ResetPose();
        work.Restore();
        flash.Restore();
    }

    private void ResetPose()
    {
        for (int i = 0; i < arms.Length; i++) if (arms[i].target != null) arms[i].target.localRotation = armHome[i];
        for (int i = 0; i < slides.Length; i++) if (slides[i].target != null) slides[i].target.localPosition = slideHome[i];
        if (hideOnSpawn != null) hideOnSpawn.SetActive(true);
        hidden = false;
        openedSound = ejected = false;
        work?.Set(idleWorkLevel);
        flash?.Restore();
        PropHazardKit.Stop(workSparks, true);
        PropHazardKit.Stop(ejectBurst, true);
        PropHazardKit.Stop(idleSparks, true);
    }

    private void ScheduleIdleSound()
    {
        nextIdleSound = idleSoundInterval.x > 0f
            ? Time.time + UnityEngine.Random.Range(idleSoundInterval.x, Mathf.Max(idleSoundInterval.x, idleSoundInterval.y))
            : float.PositiveInfinity;
    }

    // ── IDungeonSpawnAnchor ─────────────────────────────

    public void PlaySpawn(EnemyController enemy)
    {
        if (!isActiveAndEnabled) return;
        Cache();
        if (!live) { live = true; if (probes.Length == 0) probes = PropHazardKit.VisualRenderers(this); }
        SpawnCount++;
        LastEnemy = enemy;
        // 연출 도중에 다시 불리면(같은 앵커에서 시간차로 또 나옴) 처음부터 다시 한다. 열린 묶음은 그 자리에서 이어서 움직인다.
        sequence = 0f;
        openedSound = ejected = false;
        Vector3 at = transform.position;
        PropHazardKit.PlayClip(startSound, at);
        PropHazardKit.PlayFeedback(startFeedback, at);
        PropHazardKit.Play(workSparks);
    }

    /// <summary>
    /// 클리어한 방에 다시 세울 때 '내보낸 뒤' 모습으로 둔다(IDungeonSpentAnchor): 숨기는 모형(잠든 로봇 등)은 숨기고,
    /// 남는 관절·열린 채 남는 묶음(거치대 집게)은 열린 자세로 고정한다. 소리·효과는 내지 않는다.
    /// </summary>
    public void MarkSpent()
    {
        Cache();
        SpawnCount = Mathf.Max(1, SpawnCount);
        sequence = -1f;
        openedSound = ejected = true;
        if (hideOnSpawn != null) hideOnSpawn.SetActive(false);
        hidden = true;
        Pose(0f);
    }

    // ── 움직임 ─────────────────────────────

    private void Update()
    {
        if (!live) return;
        float dt = Time.deltaTime;
        bool playing = sequence >= 0f;
        if (playing) Advance(dt);
        else if (Time.time >= nextIdleSound)
        {
            ScheduleIdleSound();
            if (PropHazardKit.AnyVisible(probes) || DungeonPropFeedback.DistanceGain(transform.position) > 0f)
            {
                PropHazardKit.PlayClip(idleSound, transform.position, .55f);
                if (idleSparks != null) idleSparks.Emit(UnityEngine.Random.Range(4, 9));
            }
        }
        if (!playing && !PropHazardKit.AnyVisible(probes)) return;
        clock += dt;
        Pose(dt);
    }

    private void Advance(float dt)
    {
        sequence += dt;
        float t = sequence;
        if (!openedSound && t >= openAt)
        {
            openedSound = true;
            PropHazardKit.PlayClip(openSound, transform.position);
        }
        if (!hidden && hideOnSpawn != null && t >= hideAt)
        {
            hidden = true;
            hideOnSpawn.SetActive(false);
        }
        if (!ejected && t >= ejectAt)
        {
            ejected = true;
            PropHazardKit.Stop(workSparks);
            PropHazardKit.Play(ejectBurst);
            Vector3 at = LastEnemy != null && LastEnemy.isActiveAndEnabled ? LastEnemy.transform.position : transform.position;
            PropHazardKit.PlayClip(ejectSound, at);
            PropHazardKit.PlayFeedback(ejectFeedback, at);
        }
        if (t >= duration)
        {
            sequence = -1f;
            PropHazardKit.Stop(workSparks);
            flash.Restore();
        }
    }

    private void Pose(float dt)
    {
        float t = sequence;
        bool playing = t >= 0f;
        // 작업 정도(0 평소 ~ 1 작업): armTime 동안 들어가고 ejectAt부터 0.4초 동안 빠진다.
        float workBlend = !playing ? 0f
            : t < ejectAt ? PropHazardKit.Smooth(armTime > 0f ? t / armTime : 1f)
            : 1f - PropHazardKit.Smooth((t - ejectAt) / .4f);
        for (int i = 0; i < arms.Length; i++)
        {
            var arm = arms[i];
            if (arm.target == null) continue;
            float idle = arm.idleSwing != 0f && arm.idlePeriod > 0f ? arm.idleSwing * Mathf.Sin((clock + i * .7f) / arm.idlePeriod * Mathf.PI * 2f) : 0f;
            float jitter = arm.workJitter != 0f ? arm.workJitter * (Mathf.PerlinNoise(clock * 9f + i * 3.1f, 0f) * 2f - 1f) : 0f;
            float blend = workBlend;
            if (arm.stayOpen && SpawnCount > 0)
            {
                // 남는 관절: 작업 동안 열리고 그 뒤로도 열린 채(떨림은 작업 동안만).
                blend = playing && SpawnCount <= 1 ? PropHazardKit.Smooth(armTime > 0f ? t / armTime : 1f) : 1f;
                if (!playing || t >= ejectAt) jitter = 0f;
            }
            float angle = Mathf.Lerp(idle, arm.workAngle + jitter, blend);
            arm.target.localRotation = armHome[i] * Quaternion.Euler(0f, 0f, angle);
        }
        for (int i = 0; i < slides.Length; i++)
        {
            var slide = slides[i];
            if (slide.target == null) continue;
            float open;
            if (!playing) open = !slidesReturn && SpawnCount > 0 ? 1f : 0f;   // 열린 채 남는 묶음(거치대 집게)은 한 번 생성한 뒤 열린 채다
            else
            {
                float o = PropHazardKit.Smooth((t - openAt - slide.delay) / openTime);
                float c = slidesReturn ? PropHazardKit.Smooth((t - closeAt - slide.delay * .5f) / closeTime) : 0f;
                open = Mathf.Max(0f, o - c);
            }
            slide.target.localPosition = slideHome[i] + (Vector3)(slide.travel * open);
        }
        float targetSpin = playing && t < ejectAt + .4f ? workSpin : idleSpin;
        spinSpeed = Mathf.MoveTowards(spinSpeed, targetSpin, Mathf.Abs(workSpin - idleSpin) * dt / .6f);
        if (spinSpeed != 0f)
            foreach (var spinner in spinners) if (spinner != null) spinner.localRotation *= Quaternion.Euler(0f, 0f, spinSpeed * dt);
        if (work.Count > 0)
        {
            if (playing && t < ejectAt)
            {
                // 용접·전송 조명: 빠르게 떨리는 밝기.
                float flicker = .7f + .3f * Mathf.PerlinNoise(clock * 21f, 1.7f);
                work.Set(workLevel * flicker * workBlend + idleWorkLevel * (1f - workBlend));
            }
            else work.Set(playing ? Mathf.Lerp(idleWorkLevel, workLevel, 1f - PropHazardKit.Smooth((t - ejectAt) / .5f)) : idleWorkLevel);
        }
        if (flash.Count > 0)
        {
            if (playing && t >= ejectAt && t < ejectAt + flashTime)
            {
                float k = 1f - (t - ejectAt) / flashTime;
                flash.Set(1f + flashBoost * k * k);
            }
            else if (!float.IsNaN(flash.Level)) flash.Restore();
        }
    }
}
