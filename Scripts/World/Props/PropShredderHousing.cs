using System;
using UnityEngine;

// 분쇄기 하우징(N04, 벽 장식). 기능은 없다 — 폐기장 압착장의 '분쇄 설비'로 읽히게 하는 움직이는 장식이다.
// - 크러셔 기어 회전·표시등은 같은 루트의 DungeonPropMotion이 맡고, 이 부품은 압착 피스톤의 왕복(이동만, 크기 변형 없음)과
//   내려찍는 순간의 쿵 소리(거리 감쇠, 임시 기존 효과음 HeavyMechanismClunk)·가끔 떨어지는 폐자재 조각을 맡는다.
// - 날·붉은 위험 발광은 보이지 않는다(장식이 위험처럼 읽히지 않게, concepts §6-6). 화면 밖이면 멈춘다.
[DisallowMultipleComponent]
public sealed class PropShredderHousing : MonoBehaviour, IDungeonPropPart
{
    [SerializeField, Tooltip("왕복하는 압착 램(이동만).")] private Transform piston;
    [SerializeField, Tooltip("램이 내려가는 거리·방향(부품 좌표).")] private Vector2 stroke = new Vector2(0f, -.26f);
    [SerializeField, Min(.3f), Tooltip("한 번 왕복하는 시간(초).")] private float period = 2.2f;
    [SerializeField, Range(.05f, .5f), Tooltip("주기 중 내려찍는 구간 비율(빠르게).")] private float strikeRatio = .1f;
    [SerializeField, Range(0f, .5f), Tooltip("내려찍은 뒤 멈춰 있는 구간 비율.")] private float holdRatio = .14f;
    [SerializeField, Range(0f, 1f), Tooltip("내려찍을 때 폐자재 조각이 떨어질 확률.")] private float debrisChance = .45f;
    [SerializeField] private ParticleSystem debris;
    [SerializeField] private SoundOptions strikeSound;
    [SerializeField, Range(0f, 1f)] private float strikeVolume = .55f;

    private Vector3 pistonHome;
    private Renderer[] probes = Array.Empty<Renderer>();
    private float clock;
    private float lastPhase;
    private bool cached;

    /// <summary>이번 대여에서 내려찍은 횟수(검사용).</summary>
    public int Strikes { get; private set; }
    public Vector3 PistonOffset => piston != null ? piston.localPosition - pistonHome : Vector3.zero;

    private void Awake() => Cache();

    private void Cache()
    {
        if (cached) return;
        cached = true;
        if (piston != null) pistonHome = piston.localPosition;
    }

    public void OnPropSpawned(DungeonProp prop)
    {
        Cache();
        if (probes.Length == 0) probes = PropHazardKit.VisualRenderers(this);
        // 같은 방의 하우징이 한꺼번에 찍지 않게 시작 위상을 흩는다(위치로 정해 매번 같다).
        Vector3 p = transform.position;
        clock = Mathf.Repeat(p.x * .37f + p.y * .21f, 1f) * period;
        lastPhase = clock / period;
        Strikes = 0;
        if (piston != null) piston.localPosition = pistonHome + (Vector3)(stroke * Offset(lastPhase));
        PropHazardKit.Stop(debris, true);
    }

    public void OnPropReturning(DungeonProp prop)
    {
        if (piston != null) piston.localPosition = pistonHome;
    }

    // 0 = 위, 1 = 아래. 천천히 올라가 → 위에서 잠깐 → 빠르게 내려찍고 → 아래에서 잠깐.
    private float Offset(float phase)
    {
        float rise = 1f - strikeRatio - holdRatio * 2f;
        if (phase < rise) return 1f - PropHazardKit.Smooth(phase / rise);
        phase -= rise;
        if (phase < holdRatio) return 0f;
        phase -= holdRatio;
        if (phase < strikeRatio) { float t = phase / strikeRatio; return t * t; }
        return 1f;
    }

    private void Update()
    {
        if (!PropHazardKit.AnyVisible(probes)) return;
        clock += Time.deltaTime;
        float phase = Mathf.Repeat(clock / period, 1f);
        float strikeEnd = 1f - holdRatio;
        bool struck = lastPhase < strikeEnd && phase >= strikeEnd || phase < lastPhase && lastPhase < strikeEnd;
        lastPhase = phase;
        if (piston != null) piston.localPosition = pistonHome + (Vector3)(stroke * Offset(phase));
        if (!struck) return;
        Strikes++;
        Vector3 at = piston != null ? piston.position : transform.position;
        PropHazardKit.PlayClip(strikeSound, at, strikeVolume);
        if (debris != null && UnityEngine.Random.value < debrisChance) debris.Emit(UnityEngine.Random.Range(2, 5));
    }
}
