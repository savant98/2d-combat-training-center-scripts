using UnityEngine;

/// <summary>
/// 던전 방의 출입구(관 입구). 템플릿 맵 층에 Wave 격자 Tool로 놓고(루트 WaveEnvironmentTool, 점유 2×3칸), 연결된 출입구만 실행 중에 세운다.
/// 회전 0 = 오른쪽 벽(관이 +x로 나감), 회전 2(180°) = 왼쪽 벽. 기준점 = 점유 2×3칸의 가운데. 벽 3칸 중 안쪽 2칸이 파인 자리에 선다.
/// 계약(DESIGN.md §2.3): 아래 공개 API·상수는 바꾸지 않는다. 표시·셔터 동작은 이 클래스(또는 파생)에서 채운다.
/// </summary>
/// <remarks>
/// 표시(영역 passage): 표시 묶음(Visual)은 오른쪽 벽 기준으로 만들어 두고, 왼쪽 벽(180° 회전)에서는 Visual을 상하 반전해
/// 위아래가 뒤집히지 않고 좌우 대칭으로 보이게 한다. 셔터는 판 4장이 관 위쪽에 겹쳐 수납됐다가 미끄러져 내려와 입구를 막는
/// 기계 동작이다(크기 변경 없음). 잠김·봉인 동안에는 파인 칸 2×3(점유 칸 전체, 셀 경계 정렬)에 Platform 고체를 켜서 입구로 못 들어오게 한다.
/// 실내 쪽 면은 벽면(실내 쪽 파인 칸 1열의 바깥 면)과 같다. 점유 칸 전체로 둔 것은 기존 회귀(WavePlatformDeviceRegression: 환경 장치의
/// 고체는 점유 칸 양옆 경계에 맞춘다)를 지키기 위해서다.
/// 물리 질의는 하지 않는다(Player 몸통 사각형과 고체 사각형의 단순 비교만).
/// </remarks>
[DisallowMultipleComponent]
public class DungeonDoor : WorldEntityBase, IDungeonPassagePoint
{
    public enum DoorState { Open = 0, Locked = 1, Sealed = 2 }

    /// <summary>벽 안쪽으로 파는 깊이(칸).</summary>
    public const int Depth = 2;
    /// <summary>출입구 높이(칸).</summary>
    public const int Height = 3;

    public DoorState State { get; protected set; } = DoorState.Open;
    /// <summary>이 출입구가 속한 방과 템플릿 출입구 번호(DungeonManager가 세울 때 채운다).</summary>
    public int RoomId { get; set; } = -1;
    public int Socket { get; set; } = -1;

    /// <summary>붙은 벽(회전에서 읽는다).</summary>
    public DungeonDoorSide Side => Mathf.Abs(Mathf.DeltaAngle(transform.eulerAngles.z, 180f)) < 45f ? DungeonDoorSide.Left : DungeonDoorSide.Right;
    /// <summary>관이 나가는 방향(방 밖).</summary>
    public Vector2 Outward => Side == DungeonDoorSide.Right ? Vector2.right : Vector2.left;
    /// <summary>출입구 바닥 높이(월드 y). 이 높이의 칸 위에 선다.</summary>
    public float FloorY => transform.position.y - Height * .5f;

    /// <summary>Player 몸통 중심이 이 사각형에 들어오면 관 통과를 시작한다(월드).</summary>
    public Rect MouthRect
    {
        get
        {
            float x = transform.position.x, floor = FloorY;
            return Side == DungeonDoorSide.Right
                ? Rect.MinMaxRect(x - .5f, floor + .2f, x + 1.2f, floor + 1.6f)
                : Rect.MinMaxRect(x - 1.2f, floor + .2f, x + .5f, floor + 1.6f);
        }
    }
    /// <summary>관 안쪽 깊숙이 들어간 Player 몸통 중심(월드).</summary>
    public Vector2 RecessPoint => new(transform.position.x + Outward.x * .25f, FloorY + .755f);
    /// <summary>관에서 나와 방 안에 선 Player 몸통 중심(월드).</summary>
    public Vector2 StandPoint => new(transform.position.x - Outward.x * 2.2f, FloorY + .755f);

    // ───────────── 표시 부품(tools/Unity/DungeonPassageSetup.cs가 연결한다) ─────────────
    [Header("표시")]
    [Tooltip("오른쪽 벽 기준으로 만든 표시 묶음. 왼쪽 벽(180° 회전)에서는 상하 반전해 좌우 대칭으로 보이게 한다.")]
    [SerializeField] private Transform visual;
    [Tooltip("셔터 판(맨 아래 판부터). 열림 자세에서 닫힘 자세로 미끄러져 내려온다.")]
    [SerializeField] private Transform[] shutterPlates = System.Array.Empty<Transform>();
    [Tooltip("셔터 판의 열림 자세(Visual 기준 위치). 판 순서와 같다.")]
    [SerializeField] private Vector2[] shutterOpen = System.Array.Empty<Vector2>();
    [Tooltip("셔터 판의 닫힘 자세(Visual 기준 위치). 판 순서와 같다.")]
    [SerializeField] private Vector2[] shutterClosed = System.Array.Empty<Vector2>();
    [Tooltip("셔터를 움직이는 구동 기어. 셔터가 움직인 만큼 돈다.")]
    [SerializeField] private Transform shutterGear;
    [Tooltip("셔터가 다 닫힐 때까지 구동 기어가 도는 각도(도).")]
    [SerializeField] private float shutterGearTurn = -240f;
    [Tooltip("잠금 빗장(빨간 발광). 셔터가 거의 닫힌 뒤 관 안쪽에서 미끄러져 나와 셔터를 가로지른다.")]
    [SerializeField] private Transform[] lockBars = System.Array.Empty<Transform>();
    [Tooltip("빗장이 걸린 자세(Visual 기준 위치). 빗장 순서와 같다.")]
    [SerializeField] private Vector2[] lockBarClosed = System.Array.Empty<Vector2>();
    [Tooltip("빗장이 빠져 있을 때 걸린 자세에서 옮겨 두는 양(Visual 기준, +x = 관 안쪽).")]
    [SerializeField] private Vector2 lockBarRetract = new Vector2(.42f, 0f);
    [Tooltip("봉인 표식(빨간 발광). 봉인 상태에서 셔터가 닫혀 있는 동안 맥동한다.")]
    [SerializeField] private Renderer[] sealMarks = System.Array.Empty<Renderer>();
    [Tooltip("중립 기능 발광(파랑). 열려 있으면 원래 세기, 잠기면 어둡게 한다.")]
    [SerializeField] private Renderer[] functionGlows = System.Array.Empty<Renderer>();
    [Tooltip("입구 테두리 발광. 빨아들이거나 내보낼 때 잠깐 밝아진다.")]
    [SerializeField] private Renderer[] mouthGlows = System.Array.Empty<Renderer>();
    [Tooltip("흡입·배출 입자(Local 공간, 방출은 코드가 직접 한다).")]
    [SerializeField] private ParticleSystem streams;

    [Header("고체")]
    [Tooltip("잠김·봉인 동안 켜는 파인 칸 2×3의 Platform 고체(셀 경계 정렬, 실내 쪽 면 = 벽면). 열리면 끈다.")]
    [SerializeField] private BoxCollider2D blocker;

    [Header("동작")]
    [Tooltip("셔터가 열림↔닫힘을 한 번 오가는 시간(초).")]
    [SerializeField, Range(.3f, .45f)] private float shutterDuration = .4f;
    [Tooltip("봉인 표식의 맥동 횟수(초당).")]
    [SerializeField, Min(.1f)] private float sealPulseRate = 1.4f;
    [Tooltip("잠긴 동안의 중립 기능 발광 세기(원래 세기 대비). 알파 블렌드 재질이라 0으로 두지 않는다.")]
    [SerializeField, Range(.1f, 1f)] private float lockedFunctionLevel = .3f;
    [Tooltip("흡입·배출 때 입구 테두리가 밝아지는 시간(초).")]
    [SerializeField, Min(.05f)] private float mouthFlashDuration = .45f;
    [Tooltip("흡입·배출 때 입구 테두리 발광의 최대 배율.")]
    [SerializeField, Min(1f)] private float mouthFlashGain = 2.6f;
    [Tooltip("흡입·배출 입자 수.")]
    [SerializeField, Range(0, 40)] private int streamCount = 14;

    [Header("거리에 따른 효과 세기")]
    [Tooltip("카메라에서 이 거리(칸) 안이면 소리를 원래 세기로 낸다.")]
    [SerializeField, Min(0f)] private float soundNearDistance = 7f;
    [Tooltip("카메라에서 이 거리(칸)부터는 소리를 내지 않는다.")]
    [SerializeField, Min(0f)] private float soundFarDistance = 24f;

    private static readonly int IntensityId = Shader.PropertyToID("_Intensity");
    private MaterialPropertyBlock block;
    private float[] functionBase, mouthBase, sealBase;
    private float closed;          // 셔터 닫힘 정도(0 = 열림, 1 = 닫힘)
    private float flash;           // 입구 테두리 발광이 남은 비율(1 → 0)
    private bool blockWanted;      // 고체를 켜야 하는데 Player가 그 칸에 겹쳐 있어 미뤄 두었는지
    private bool appliedLeft;
    private bool orientationApplied;
    private float glowFunctionLevel = -1f, glowMouthLevel = -1f;

    /// <summary>셔터가 지금 상태의 자세에 도착했는지.</summary>
    public bool ShutterSettled => Mathf.Approximately(closed, ShutterTarget);

    /// <summary>
    /// 셔터가 향하는 닫힘 정도. 닫힌 상태여도 Player가 파인 칸에 있어 고체를 미뤄 둔 동안에는 열린 자세를 유지한다
    /// (닫힌 셔터가 Player 몸 위로 내려와 표시와 고체가 어긋나지 않게 한다). Player가 빠져 고체가 켜지면 그때 닫힌다.
    /// </summary>
    private float ShutterTarget => State == DoorState.Open || BlockerDeferred ? 0f : 1f;
    private bool BlockerDeferred => blockWanted && blocker != null && !blocker.enabled;

    /// <summary>Player 몸통이 파인 칸 2×3(잠그면 고체가 켜질 자리)과 겹치는지. 물리 질의 없이 Collider 경계만 비교한다.</summary>
    public bool IsPlayerInRecess(PlayerController player) => player != null && player.isActiveAndEnabled && BodyRect(player).Overlaps(BlockerRect());

    /// <summary>
    /// 상태를 바꾼다. instant면 셔터를 바로 그 자세로 둔다(방을 세우는 순간 등).
    /// silent면 열림↔닫힘 효과음을 내지 않는다(방 전체를 한꺼번에 잠그거나 풀 때 DungeonManager가 Player 위치에서 한 번만 낸다).
    /// </summary>
    public virtual void SetState(DoorState state, bool instant = false, bool silent = false)
    {
        var previous = State;
        State = state;
        ApplyOrientation();
        bool shut = state != DoorState.Open;
        // 고체는 여는 즉시 끄고, 잠글 때는 Player가 그 칸에 없을 때 켠다(겹친 채 켜면 몸이 고체 속에 묻힌다).
        // 셔터 자세가 고체를 따르므로(ShutterTarget) 고체부터 정한다.
        blockWanted = shut;
        if (!shut) SetBlocker(false);
        else TryEnableBlocker();
        if (instant)
        {
            closed = ShutterTarget;
            ApplyPose();
        }
        else if (!silent && previous != state && (previous == DoorState.Open) != (state == DoorState.Open))
        {
            var rules = DungeonRulesSO.Default;
            if (rules != null) PlayFeedback(shut ? rules.doorLockFeedback : rules.doorUnlockFeedback);
        }
    }
    /// <summary>Player를 빨아들이는 연출(관 통과 시작).</summary>
    public virtual void PlayIntake() => PlayStream(true);
    /// <summary>Player를 내보내는 연출(관 통과 끝).</summary>
    public virtual void PlayEject() => PlayStream(false);

    public override void OnSpawnedFromPool()
    {
        base.OnSpawnedFromPool();
        RoomId = -1; Socket = -1;
        State = DoorState.Open;
        orientationApplied = false;
        ApplyOrientation();
        closed = 0f; flash = 0f; blockWanted = false;
        SetBlocker(false);
        if (streams != null) streams.Clear(true);
        ApplyPose();
    }

    protected override void Awake()
    {
        base.Awake();
        block = new MaterialPropertyBlock();
        functionBase = BaseIntensities(functionGlows);
        mouthBase = BaseIntensities(mouthGlows);
        sealBase = BaseIntensities(sealMarks);
    }

    private void OnEnable()
    {
        orientationApplied = false;
        ApplyOrientation();
        ApplyPose();
    }

    private void OnDisable()
    {
        // 풀로 돌아가면 다음에 꺼낼 때 열린 자세에서 시작한다. 고체가 켜진 채 남지 않게 한다.
        blockWanted = false;
        SetBlocker(false);
    }

    private void Update()
    {
        // 풀이 위치·회전을 바꾼 뒤에 켜거나, 다른 곳이 회전을 바꿔도 표시 방향을 맞춘다.
        ApplyOrientation();
        // 미뤄 둔 고체를 먼저 켜 본다. 켜지면 같은 프레임부터 셔터가 닫히기 시작한다.
        if (blockWanted && (blocker == null || !blocker.enabled)) TryEnableBlocker();
        float target = ShutterTarget;
        // 근사 비교로 멈추면 끝값에 조금 못 미친 채 선다(셔터 게이트에서 고체가 남던 원인). MoveTowards가 마지막에 목표값을 정확히 맞춘다.
        bool moving = closed != target;
        if (moving) closed = Mathf.MoveTowards(closed, target, Time.deltaTime / Mathf.Max(.05f, shutterDuration));
        if (flash > 0f) flash = Mathf.Max(0f, flash - Time.deltaTime / Mathf.Max(.05f, mouthFlashDuration));
        ApplyPose();
    }

    // ───────────── 방향 ─────────────
    private void ApplyOrientation()
    {
        if (visual == null) return;
        bool left = Side == DungeonDoorSide.Left;
        if (orientationApplied && left == appliedLeft) return;
        orientationApplied = true;
        appliedLeft = left;
        // 180° 회전은 위아래도 뒤집으므로 Visual만 상하로 다시 뒤집어 좌우 반전만 남긴다(고체는 상하 대칭이라 그대로 둔다).
        var scale = visual.localScale;
        scale.y = Mathf.Abs(scale.y) * (left ? -1f : 1f);
        visual.localScale = scale;
    }

    // ───────────── 자세 ─────────────
    private void ApplyPose()
    {
        float eased = closed * closed * (3f - 2f * closed);
        int count = shutterPlates != null ? shutterPlates.Length : 0;
        for (int i = 0; i < count; i++)
        {
            var plate = shutterPlates[i];
            if (plate == null || i >= shutterOpen.Length || i >= shutterClosed.Length) continue;
            // 가장 멀리 내려가는 맨 아래 판이 먼저 출발하고 위의 판이 조금씩 늦게 따라 내려온다(닫힐 때). 열 때는 반대 순서다.
            float lag = count > 1 ? .18f * (i / (float)(count - 1)) : 0f;
            float t = Mathf.Clamp01((closed - lag) / (1f - .18f));
            t = t * t * (3f - 2f * t);
            plate.localPosition = Vector2.Lerp(shutterOpen[i], shutterClosed[i], t);
        }
        if (shutterGear != null) shutterGear.localRotation = Quaternion.Euler(0f, 0f, shutterGearTurn * eased);

        // 빗장: 셔터가 60% 넘게 닫힌 뒤 관 안쪽에서 미끄러져 나와 걸린다. 빠져 있는 동안은 감춘다.
        float bar = Mathf.Clamp01((closed - .6f) / .4f);
        bar = bar * bar * (3f - 2f * bar);
        for (int i = 0; i < (lockBars != null ? lockBars.Length : 0); i++)
        {
            var node = lockBars[i];
            if (node == null || i >= lockBarClosed.Length) continue;
            node.localPosition = lockBarClosed[i] + lockBarRetract * (1f - bar);
            bool show = bar > .001f;
            if (node.gameObject.activeSelf != show) node.gameObject.SetActive(show);
        }

        // 봉인 표식: 봉인 상태에서 다 닫혀 있을 때만 보이고 맥동한다.
        bool sealShown = State == DoorState.Sealed && closed > .95f;
        float pulse = .45f + .55f * (.5f + .5f * Mathf.Sin(Time.time * Mathf.PI * 2f * sealPulseRate));
        for (int i = 0; i < (sealMarks != null ? sealMarks.Length : 0); i++)
        {
            var mark = sealMarks[i];
            if (mark == null) continue;
            if (mark.enabled != sealShown) mark.enabled = sealShown;
            if (sealShown) SetIntensity(mark, sealBase[i] * pulse);
        }

        // 중립 기능 발광은 잠길수록 어두워지고, 흡입·배출 때 입구 테두리가 잠깐 밝아진다.
        float function = Mathf.Lerp(1f, lockedFunctionLevel, eased);
        if (!Mathf.Approximately(function, glowFunctionLevel))
        {
            glowFunctionLevel = function;
            for (int i = 0; i < (functionGlows != null ? functionGlows.Length : 0); i++)
                if (functionGlows[i] != null) SetIntensity(functionGlows[i], functionBase[i] * function);
        }
        float mouth = function * (1f + (mouthFlashGain - 1f) * flash * flash);
        if (!Mathf.Approximately(mouth, glowMouthLevel))
        {
            glowMouthLevel = mouth;
            for (int i = 0; i < (mouthGlows != null ? mouthGlows.Length : 0); i++)
                if (mouthGlows[i] != null) SetIntensity(mouthGlows[i], mouthBase[i] * mouth);
        }
    }

    private void SetIntensity(Renderer target, float value)
    {
        block ??= new MaterialPropertyBlock();
        target.GetPropertyBlock(block);
        block.SetFloat(IntensityId, value);
        target.SetPropertyBlock(block);
    }

    private static float[] BaseIntensities(Renderer[] renderers)
    {
        var values = new float[renderers != null ? renderers.Length : 0];
        for (int i = 0; i < values.Length; i++)
        {
            var material = renderers[i] != null ? renderers[i].sharedMaterial : null;
            values[i] = material != null && material.HasProperty(IntensityId) ? material.GetFloat(IntensityId) : 1f;
        }
        return values;
    }

    // ───────────── 고체 ─────────────
    // 파인 칸 2×3의 월드 사각형(벽면 ~ 남은 바깥 벽, 높이 3). Prefab의 BoxCollider2D와 같은 값이다(좌우 대칭이라 방향과 무관).
    private Rect BlockerRect()
    {
        float x = transform.position.x, floor = FloorY;
        return Rect.MinMaxRect(x - Depth * .5f, floor, x + Depth * .5f, floor + Height);
    }

    private void TryEnableBlocker()
    {
        if (blocker == null || blocker.enabled) return;
        var player = GameManager.Instance != null ? GameManager.Instance.Player : null;
        if (IsPlayerInRecess(player)) return;
        SetBlocker(true);
    }

    private void SetBlocker(bool enabled)
    {
        if (blocker != null && blocker.enabled != enabled) blocker.enabled = enabled;
    }

    // Player 몸통 사각형(물리 질의 없이 Collider 경계만 읽는다). 구속 중이면 모터가 쓰는 몸통을 쓴다.
    private static Rect BodyRect(PlayerController player)
    {
        var motor = player.Motor;
        if (motor != null && motor.IsConfined) return motor.ConfinedBody;
        if (player.TryGetComponent(out Collider2D body))
        {
            var bounds = body.bounds;
            return Rect.MinMaxRect(bounds.min.x, bounds.min.y, bounds.max.x, bounds.max.y);
        }
        Vector2 p = player.transform.position;
        return new Rect(p - new Vector2(.3f, 0f), new Vector2(.6f, 1.5f));
    }

    // ───────────── 흡입·배출 ─────────────
    private void PlayStream(bool intake)
    {
        flash = 1f;
        glowMouthLevel = -1f;
        if (streams == null || streamCount <= 0) return;
        if (!streams.isPlaying) streams.Play(true);
        // Visual 기준(+x = 관 안쪽, 입구 = x -1). 흡입은 방 쪽에서 관 안으로 빨려 들어가고, 배출은 관 안에서 방 쪽으로 뿜어 나온다.
        var emit = new ParticleSystem.EmitParams { applyShapeToPosition = false, rotation = 0f };
        for (int i = 0; i < streamCount; i++)
        {
            float y = Random.Range(-1.32f, .55f);
            float speed = Random.Range(6.5f, 11f);
            float x = intake ? Random.Range(-2.4f, -1.15f) : Random.Range(-.7f, .35f);
            emit.position = new Vector3(x, y, 0f);
            emit.velocity = new Vector3(intake ? speed : -speed, Random.Range(-.25f, .25f) - (intake ? y * .9f : -y * .2f), 0f);
            emit.startLifetime = Random.Range(.14f, .24f);
            emit.startSize = Random.Range(.22f, .42f);
            emit.startColor = Color.Lerp(new Color(.80f, .94f, 1f, .85f), new Color(.17f, .78f, .89f, .85f), Random.value);
            streams.Emit(emit, 1);
        }
    }

    // ───────────── 효과음(카메라 거리 감쇠, WaveEnvironmentDevice.PlayFeedback 방식) ─────────────
    private void PlayFeedback(FeedbackDataSO data)
    {
        if (data == null || FeedbackPlayer.Instance == null) return;
        Vector3 position = transform.position;
        float gain = DistanceGain(position);
        if (gain <= 0f) return;
        SoundOptions sound = null;
        if (data.sound != null && data.sound.clip != null)
        {
            sound = data.sound.WithPitchMultiplier(1f);
            sound.volume = data.sound.volume * gain;
        }
        FeedbackPlayer.Instance.PlayExact(data, position, transform.rotation, null, sound);
    }

    private float DistanceGain(Vector2 position)
    {
        var listener = Camera.main;
        if (listener == null) return 1f;
        float distance = Vector2.Distance(listener.transform.position, position);
        float near = Mathf.Max(0f, soundNearDistance), far = Mathf.Max(near, soundFarDistance);
        if (far <= near) return distance <= near ? 1f : 0f;
        float t = Mathf.InverseLerp(near, far, distance);
        return 1f - t * t * (3f - 2f * t);
    }
}
