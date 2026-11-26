using System;
using UnityEngine;

// 고정 크레인(DESIGN_V4 §2.4 — 4차 새 Prefab DungeonPropCraneAnchor · 5차 DESIGN_V5 §1 외형 '막대 끝 O' · 7차 DESIGN_V7 V11 고리 한가운데 매달림).
// 천장 기둥 끝의 그래플 고리에 붙기(↑)로 매달린다. 탑승해도 움직이지 않는다.
// 점프·대시로 내리거나 다른 크레인으로 연쇄 붙기를 해 공중을 건넌다(고정 크레인 사이 간격 ≤ 붙기 반경 × 0.9 — 템플릿 규칙). 매달린 채 무장을 쓸 수 있다.
// - 배치: footprint 2×4(차지 칸·기둥 기준 = 집 발 (x + 1, y) = 루트 (0, −2)). 배치 설정 zipRadius(4~14, 기본 8), craneCableLength(천장 쪽 기둥 길이 0~12, 기본 3 — 표시만:
//   footprint 위 끝의 회전 관절에서 위로 그만큼 망원 기둥을 그리고 그 끝에 천장 앵커판을 둔다. 천장 위치는 물리 질의 없이 설정으로 정한다).
// - 7차 V11(사용자 결정 — 안 A): 매달린 발 = 루트 + HangLocal (0, −0.655). Player 몸 중심(발 + 0.755)이 그래플 고리 중심(루트 (0, RingCenterY))에 온다
//   (집 발보다 1.345칸 위). 붙기 반경·격자 Tool 범위 원의 중심도 고리 중심이다. 편집기 검사·격자 Tool은 DungeonCranePaths.HangFeet(prefab, root)로 같은 값을 쓴다.
// - 외형(5차 · 7차 V11): 천장 앵커판(경첩 너클) + 굵은 망원 기둥(바깥 관 0.22 + 안쪽 관 0.14 + 1칸마다 칼라, 길이대로 정점을 새로 채운다 — 늘이기 없음) +
//   둥근 회전 관절(허브·잠금 링·파랑 잠금등) + 짧은 팔 + 큰 그래플 고리(반지름 RingRadius 0.92·굵기 RingWidth 0.10 — 안쪽 끝 0.87이 공 모양 몸(반지름 0.75)보다 커서
//   몸이 고리 안에 든다, 흰 외곽선) + 고리 안쪽 아래 잠금 걸쇠 2개(225°·315° — 체결 때 회전으로 몸을 묾) + 고리 둘레 파랑 잠금등 3개. 7차에 손잡이 바는 없앴다(몸 뒤에 가려짐).
//   운반 케이지는 없다(매달리는 고리). 청록은 고리 위 비컨(붙을 수 있음)과 강조 테두리에만. 크레인 부품은 Player 뒤에 그려진다.
// - 표시: 비컨(대기 점멸·선택되면 빠르게). 붙으면 잠금등 3개가 0.05초 간격으로 차례로 켜진다(체결 표시).
//   6차 T4: 붙기 직선·표적 괄호는 그래플 고리 중심(= 키 안내 자리, 흔들림을 따라감)을 가리킨다 — 7차 V11부터 이 점이 도착 몸 중심·시야 판정선 끝과 같다.
//   괄호 상자 ±1.2 × ±1.2(강조 원 1.08 + 여유).
//   흔들림은 관절 아래 묶음(Swing)의 회전만 — 대기 중 작은 진자, 탑승 중 멈춤, 도착 때 감쇠 진동 4°(ZipCraneBase.KickAngle)를 더한다(관절 ~ 고리 중심 1.68칸 → 고리는 최대 ±0.12칸 옆으로).
[DisallowMultipleComponent]
public sealed class PropCraneAnchor : ZipCraneBase
{
    public const string CableLengthKey = "craneCableLength";
    /// <summary>회전 관절 윗면(루트 로컬 y) — footprint 위 끝. 기둥은 여기서 위로 그린다.</summary>
    public const float MountTop = 2f;
    /// <summary>그래플 고리 중심(머리 로컬 y — 키 안내 자리).</summary>
    public const float RingCenterY = .1f;
    /// <summary>
    /// 7차(DESIGN_V7 V11 안 A): 매달린 발(루트 로컬). 몸 중심(발 + BodyHalfHeight 0.755)이 그래플 고리 중심(RingCenterY)에 온다 — 집 발 HangFeetLocal (0, −2)보다 1.345칸 위.
    /// 실행 쪽(ZipFeet 재정의·범위 원)과 생성 도구는 crane7이 이 값을 쓰고, 편집기 검사·격자 Tool은 DungeonCranePaths.HangFeet(prefab, root)로 같은 값을 읽는다.
    /// </summary>
    public static readonly Vector2 HangLocal = new Vector2(0f, RingCenterY - BodyHalfHeight);
    /// <summary>
    /// 7차 V11: 그래플 고리 반지름(고리 몸체 가운데 선)·굵기(칸). 안쪽 끝 0.87 — Player 공 모양 몸(반지름 0.75)과 틈 0.12, 바깥 끝 0.97 < footprint 반폭 1.
    /// 생성 도구(DungeonFacilitySetup.AnchorHead)·Verify·회귀가 같은 값을 쓴다.
    /// </summary>
    public const float RingRadius = .92f, RingWidth = .1f;
    /// <summary>잠금등이 차례로 켜지는 간격(초).</summary>
    public const float LockStep = .05f;
    /// <summary>6차 T4 · 7차 V11: 표적 괄호 상자 반크기(고리 강조 원 반지름 1.08 + 0.12 — 5·6차 ±0.8). 생성 도구가 같은 값으로 괄호 Mesh를 굽는다.</summary>
    public static readonly Vector2 FrameHalf = new Vector2(1.2f, 1.2f);

    [Header("고정 크레인(배치 기본값)")]
    [SerializeField, Range(0f, 12f), Tooltip("회전 관절에서 천장 앵커판까지 기둥 길이(칸, 표시만).")] private float cableLength = 3f;

    [Header("고정 크레인 구성(생성 도구가 연결)")]
    [SerializeField, Tooltip("관절 아래에 매단 팔·고리 묶음(회전 원점 = 관절 중심). 흔들림은 이 회전만.")] private Transform swing;
    [SerializeField, Tooltip("천장 앵커판(이동만 — 기둥 길이 위치).")] private Transform ceiling;
    [SerializeField, Tooltip("관절 ~ 천장 앵커판 망원 기둥(실행 중 다시 그림).")] private MeshFilter ceilingCable;
    [SerializeField, Tooltip("바깥 관 색(몸체 정점색).")] private Color cableColor = new Color(.6f, .62f, .66f, 1f);
    [SerializeField, Tooltip("안쪽 관 색(몸체 정점색).")] private Color innerColor = new Color(.5f, .52f, .56f, 1f);
    [SerializeField, Tooltip("칼라 색(몸체 정점색).")] private Color collarColor = new Color(.8f, .82f, .86f, 1f);
    [SerializeField] private float cableWidth = .22f;
    [SerializeField] private float innerWidth = .14f;
    [SerializeField, Range(0f, 1f), Tooltip("바깥 관이 차지하는 아래쪽 비율.")] private float poleSplit = .55f;
    [SerializeField] private float collarEvery = 1f;
    [SerializeField] private float collarWidth = .3f;
    [SerializeField] private float collarThickness = .06f;
    [SerializeField, Tooltip("고리 둘레 잠금등 3개(체결 때 차례로 켜짐, 파랑).")] private Renderer[] lockLights = Array.Empty<Renderer>();

    private float activeCable = 3f;
    private float swingAngle, pendulum;
    private Mesh cableBuilt;
    private float cableBuiltLength = -1f;
    private float lockTime = -1f;
    private FacilityLights lockLamp;
    private readonly float[] lockLevels = { -1f, -1f, -1f };

    /// <summary>지금 기둥 길이(칸, 검사용).</summary>
    public float CableLength => activeCable;
    /// <summary>천장 앵커판 자리(월드, 검사용).</summary>
    public Vector2 CeilingPoint => ceiling != null ? (Vector2)ceiling.position : (Vector2)transform.TransformPoint(new Vector3(0f, MountTop + activeCable, 0f));
    /// <summary>지금 흔들림 각도(검사용 — 진자 + 도착 감쇠 진동).</summary>
    public float SwingAngle => swingAngle;
    /// <summary>5차: 켜진 잠금등 수(검사용).</summary>
    public int LocksLit { get; private set; }

    private WaveDeviceParameter CableParameter => new WaveDeviceParameter(CableLengthKey, "천장 기둥 길이", "회전 관절에서 천장 앵커판까지 그리는 기둥 길이입니다(표시만, 판정 없음).", "칸", 0f, 12f, .5f, cableLength, true);

    public override WaveDeviceParameter[] PropParameters => new[] { RadiusParameter, CableParameter };

    protected override void ReadSettings(WaveDeviceSetting[] overrides)
    {
        base.ReadSettings(overrides);
        activeCable = WaveDeviceSettings.Value(overrides, CableParameter);
    }

    protected override void OnCache() => lockLamp = new FacilityLights(lockLights);

    protected override void OnSpawned()
    {
        swingAngle = pendulum = 0f;
        lockTime = -1f;
        LocksLit = 0;
        RebuildCable(true);
    }

    protected override void OnReturning()
    {
        lockLamp?.Restore();
        for (int i = 0; i < lockLevels.Length; i++) lockLevels[i] = -1f;
    }

    protected override void OnSettingsApplied() => RebuildCable(false);

    protected override void OnDestroy()
    {
        base.OnDestroy();
        FacilityMesh.Release(ref cableBuilt);
    }

    // 천장 앵커판을 기둥 길이만큼 위로 옮기고(이동만) 망원 기둥 Mesh를 그 길이로 다시 그린다(늘이기 없음).
    private void RebuildCable(bool force)
    {
        if (!force && Mathf.Abs(cableBuiltLength - activeCable) < 1e-3f) return;
        cableBuiltLength = activeCable;
        if (ceiling != null)
        {
            var parent = ceiling.parent;
            Vector3 world = transform.TransformPoint(new Vector3(0f, MountTop + activeCable, 0f));
            Vector3 local = parent != null ? parent.InverseTransformPoint(world) : world;
            ceiling.localPosition = new Vector3(local.x, local.y, ceiling.localPosition.z);
            ceiling.gameObject.SetActive(activeCable > .01f);
        }
        if (ceilingCable != null)
        {
            var space = ceilingCable.transform;
            Vector2 from = space.InverseTransformPoint(transform.TransformPoint(new Vector3(0f, MountTop - .02f, 0f)));
            Vector2 to = space.InverseTransformPoint(transform.TransformPoint(new Vector3(0f, MountTop + activeCable - .08f, 0f)));
            ceilingCable.sharedMesh = cableBuilt = FacilityMesh.Pole(cableBuilt, from, to, cableWidth, innerWidth, poleSplit, collarEvery, collarWidth, collarThickness,
                cableColor, innerColor, collarColor, name + "_CeilingPole");
        }
    }

    protected override void OnRideStarted()
    {
        // 움직이지 않는다 — 바로 매달림 대기.
        phase = Phase.Waiting;
        phaseTime = 0f;
    }

    protected override void OnRideEnded(bool interrupted)
    {
        phase = Phase.Idle;
        phaseTime = 0f;
    }

    protected override void OnGrabbed()
    {
        lockTime = 0f;
        LocksLit = 0;
    }

    protected override bool HeadLeansItself => false;

    /// <summary>
    /// 7차 V11: 매달린 발 = 루트 + HangLocal(몸 중심 = 그래플 고리 중심 — 집 발 HomeFeet보다 1.345칸 위). 흔들림은 따르지 않는다(루트 기준 —
    /// 도착 진동 4°에 고리가 최대 ±0.12칸 옆으로 움직여도 고리 안쪽과 몸 사이 틈 0.12 안). 붙기 반경·점수·이동 목표·시야 판정선 끝이 모두 이 점을 쓴다.
    /// </summary>
    public override Vector2 ZipFeet => transform.TransformPoint(HangLocal);

    /// <summary>키 안내 자리: 그래플 고리 안(흔들려도 고리를 따라간다).</summary>
    protected override Vector2 PromptPoint => head != null ? (Vector2)head.TransformPoint(new Vector3(0f, RingCenterY, 0f)) : base.PromptPoint;

    /// <summary>6차 T4: 표시 기준점 G = 그래플 고리 중심(키 안내와 같은 점, 흔들림을 따라감). 머리가 없으면 매달림 몸 중심(7차 V11부터 고리 중심과 같은 점).</summary>
    protected override Vector2 AimPoint => head != null ? (Vector2)head.TransformPoint(new Vector3(0f, RingCenterY, 0f)) : ZipFeet + Vector2.up * BodyHalfHeight;

    /// <summary>격자 Tool 범위 미리보기: 붙기 반경 원(7차 V11: 중심 = 매달림 몸 중심 = 그래플 고리 중심 (0, RingCenterY) — 기반 클래스는 집 발 기준).</summary>
    public override bool TryGetPropRange(WaveDeviceSetting[] overrides, out WaveDeviceRange range)
    {
        float radius = WaveDeviceSettings.Value(overrides, RadiusParameter);
        range = new WaveDeviceRange(WaveEnvironmentTool.RangeShape.Circle, new Vector2(0f, RingCenterY), new Vector2(radius, radius), new Color(.25f, .95f, 1f, .9f));
        return true;
    }

    protected override Vector2 AimFrameHalf => FrameHalf;

    protected override void TickMotion(float dt)
    {
        // 잠금등: 탑승 중이면 체결 순간부터 0.05초 간격으로 하나씩 켜지고, 내리면 흐리게 돌아간다.
        if (IsRiding && lockTime >= 0f) lockTime += dt;
        else if (!IsRiding) lockTime = -1f;
        int lit = IsRiding ? (lockTime < 0f ? lockLights.Length : Mathf.Min(lockLights.Length, Mathf.FloorToInt(lockTime / LockStep) + 1)) : 0;
        LocksLit = lit;
        float idle = ZipAvailable ? .35f : .15f;
        for (int i = 0; i < lockLights.Length && i < lockLevels.Length; i++)
        {
            float level = (i < lit ? 1.6f : idle) * Mathf.Max(.25f, power);
            if (Mathf.Abs(lockLevels[i] - level) <= .002f) continue;   // 값이 바뀔 때만(MaterialPropertyBlock을 매 프레임 쓰지 않게)
            lockLevels[i] = level;
            lockLamp?.SetOne(i, level);
        }
    }

    protected override void ApplyPose(float dt)
    {
        // 매단 팔·고리 전체가 관절 아래에서 흔들린다(대기 중 작은 진자, 탑승 중·쓸 수 없으면 멈춤) + 도착 감쇠 진동(회전만).
        float want = IsRiding ? 0f : swayDegrees * power * Mathf.Sin(clock * 1.5f);
        pendulum = dt > 0f ? Mathf.Lerp(pendulum, want, 1f - Mathf.Exp(-dt * 6f)) : want;
        swingAngle = pendulum + KickAngle;
        if (swing != null) swing.localRotation = Quaternion.Euler(0f, 0f, swingAngle);
        base.ApplyPose(dt);
    }
}
