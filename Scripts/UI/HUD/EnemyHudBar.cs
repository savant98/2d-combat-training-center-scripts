using UnityEngine;
using UnityEngine.UI;

// 적 머리 위 막대 한 줄(체력 또는 쉴드).
// 2026-11-17 사용자 지시: "Enemy의 체력바(보스 체력바 말고 머리 위의 기본 체력바)는 아직 구버전이니까, 보스 HUD를 참고해서 체력바도 업그레이드해 줘."
// 보스 체력바(StageHUD)와 같은 셰이더(UI/PlayerHudHpFill)로 비스듬한 발광 칸을 그린다. 칸의 수는 막대 폭에 맞춰 정하므로, 적마다 막대 폭이 달라도 칸의 크기는 같다.
// - 값: Show(값)이 목표를 정하고 fillTime 동안 따라간다.
// - 깎인 양(keepTrail): 값이 줄면 줄기 전 값까지 밝은 칸으로 남았다가, trailDelay 뒤 trailTime 동안 줄어든다. 이어서 맞으면 다시 기다린다.
//   HoldTrail(true) 동안에는 줄지 않는다(브레이크 동안 깎은 덩어리를 그대로 보여 준다).
// - 갓 깎인 조각: 이번 타격으로 깎인 만큼이 밝은 노랑으로 번쩍였다가 가라앉는다(셰이더 _Fresh · _PulseAge). 값이 바뀐 자리의 칸도 잠깐 번쩍인다(_Pulse).
//   깎인 양의 색(노랑~주황)·세기는 보스 체력 재질에서 복사한 재질 값이다(tools/Unity/BossHudSetup.cs "trail").
// - 재질은 처음 그릴 때 이 막대 몫으로 복사한다(풀에서 기다리는 적은 복사하지 않는다). 움직임이 끝나면 스스로 꺼져 갱신 비용이 없다.
// 점검 빛(보스·Player 막대의 훑는 빛)은 쓰지 않는다. 여러 적의 머리 위에서 저마다 훑으면 산만하다.
public class EnemyHudBar : MonoBehaviour
{
    [Tooltip("셰이더 UI/PlayerHudHpFill 재질을 쓰는 Image. 이 사각형 안에 칸을 그린다.")]
    [SerializeField] private Image fill;

    [Header("칸(Canvas 단위)")]
    [Tooltip("칸 하나의 폭(틈 포함). 막대 폭을 이 값으로 나눠 칸의 수를 정한다.")]
    [SerializeField] private float cellPitch = 22f;
    [SerializeField] private float cellGap = 4f;
    [SerializeField] private float cellHalfHeight = 8f;
    [Tooltip("막대의 양 끝에서 칸까지 띄우는 거리. 칸이 기울어 있어 그만큼은 비워야 사각형 밖으로 나가지 않는다.")]
    [SerializeField] private float sideMargin = 4f;

    [Header("움직임(초)")]
    [SerializeField] private float fillTime = 0.15f;
    [Tooltip("깎인 양을 남긴다(체력). 끄면 값만 따라간다(쉴드).")]
    [SerializeField] private bool keepTrail = true;
    [SerializeField] private float trailDelay = 0.35f;
    [SerializeField] private float trailTime = 0.3f;
    [SerializeField] private float pulseTime = 0.6f;

    [Header("번쩍임")]
    [ColorUsage(false, true)] [SerializeField] private Color idleEmission = new Color(1f, 0.26f, 0.12f, 1f);
    [ColorUsage(false, true)] [SerializeField] private Color hitEmission = new Color(1f, 0.42f, 0.28f, 1f);
    [SerializeField] private float flashStrength = 1.7f;
    [SerializeField] private float flashTime = 0.25f;

    private static readonly int FillID = Shader.PropertyToID("_Fill");
    private static readonly int TrailID = Shader.PropertyToID("_Trail");
    private static readonly int FreshID = Shader.PropertyToID("_Fresh");
    private static readonly int PulseID = Shader.PropertyToID("_Pulse");
    private static readonly int PulseXID = Shader.PropertyToID("_PulseX");
    private static readonly int PulseAgeID = Shader.PropertyToID("_PulseAge");
    private static readonly int ScanTimeID = Shader.PropertyToID("_ScanTime");
    private static readonly int EmissionColorID = Shader.PropertyToID("_EmissionColor");
    private static readonly int RectSizeID = Shader.PropertyToID("_RectSize");
    private static readonly int CellsID = Shader.PropertyToID("_Cells");
    private static readonly int CellSpanID = Shader.PropertyToID("_CellSpan");
    private static readonly int CellGapID = Shader.PropertyToID("_CellGap");
    private static readonly int CellHalfHeightID = Shader.PropertyToID("_CellHalfHeight");
    // 점검 빛이 쉬는 구간의 시각. 이 값에 두면 칸이 27개 이하인 막대에서는 훑는 빛이 켜지지 않는다.
    private const float RestingScanTime = 1.2f;
    private const float Settled = 0.0005f;

    private Material material;
    private float target = 1f, shown = 1f, trail = 1f, trailFrom = 1f;
    private float trailWait, trailProgress = 1f;
    private float fresh, pulse, pulseX = 1f, pulseAge = 9f, flash;
    private bool holdTrail;

    // 지금 막대가 향하는 값(0~1)과, 화면에 보이는 값·깎인 양의 끝.
    public float Value => target;
    public float Shown => shown;
    public float Trail => keepTrail ? Mathf.Max(trail, shown) : shown;
    public int Cells { get; private set; }
    public bool IsHoldingTrail => holdTrail;
    public Image Fill => fill;

    private void Awake() => enabled = false;

    private void OnDestroy()
    {
        if (material != null) Destroy(material);
    }

    // 값을 바로 맞춘다(풀에서 다시 나올 때). 깎인 양·번쩍임을 지운다.
    public void ResetTo(float value)
    {
        value = Mathf.Clamp01(value);
        target = shown = trail = trailFrom = value;
        trailWait = 0f;
        trailProgress = 1f;
        fresh = pulse = flash = 0f;
        pulseX = value;
        pulseAge = 9f;
        holdTrail = false;
        if (material != null) Apply();
        enabled = false;
    }

    // 값이 바뀌었다. 줄었으면 깎인 양을 남기고 번쩍인다. 늘었으면(회복·쉴드 재생) 값만 따라간다.
    public void Show(float value)
    {
        value = Mathf.Clamp01(value);
        float previous = target;
        target = value;
        if (value < previous - 0.00001f)
        {
            fresh = previous;
            pulse = flash = 1f;
            pulseX = value;
            pulseAge = 0f;
            if (trail < previous) trail = previous;
            if (!holdTrail) StartTrail(trailDelay);
        }
        else if (value >= Trail)
        {
            trail = value;
            trailProgress = 1f;
            fresh = 0f;
        }
        enabled = true;
    }

    // 브레이크 동안에는 깎인 양을 붙잡아 둔다. 풀면 잠깐 뒤 줄어든다.
    public void HoldTrail(bool hold)
    {
        if (holdTrail == hold) return;
        holdTrail = hold;
        if (hold)
        {
            if (trail < shown) trail = shown;
            trailProgress = 1f;
            trailWait = 0f;
        }
        else if (trail > target + Settled)
        {
            StartTrail(trailDelay);
            enabled = true;
        }
    }

    private void StartTrail(float delay)
    {
        trailWait = delay;
        trailFrom = trail;
        trailProgress = 0f;
    }

    private void Update()
    {
        float dt = Time.deltaTime;

        // 값: 남은 차이의 대부분을 fillTime 동안 따라간다
        float rate = 1f - Mathf.Exp(-dt * 4f / Mathf.Max(fillTime, 0.01f));
        shown = Mathf.Lerp(shown, target, rate);
        if (Mathf.Abs(shown - target) < Settled) shown = target;

        // 깎인 양: 기다렸다가 점점 빠르게 줄어든다
        if (!holdTrail && trailProgress < 1f)
        {
            if (trailWait > 0f) trailWait -= dt;
            else
            {
                trailProgress = Mathf.Min(1f, trailProgress + dt / Mathf.Max(trailTime, 0.01f));
                trail = Mathf.Lerp(trailFrom, target, trailProgress * trailProgress);
            }
        }

        pulse = Mathf.MoveTowards(pulse, 0f, dt / Mathf.Max(pulseTime, 0.01f));
        flash = Mathf.MoveTowards(flash, 0f, dt / Mathf.Max(flashTime, 0.01f));
        pulseAge += dt;
        Apply();

        bool trailDone = !keepTrail || holdTrail || trailProgress >= 1f;
        if (shown == target && trailDone && pulse <= 0f && flash <= 0f && pulseAge > 2f) enabled = false;
    }

    private void Apply()
    {
        if (fill == null) return;
        if (material == null) CreateMaterial();
        if (material == null) return;
        material.SetFloat(FillID, shown);
        material.SetFloat(TrailID, Trail);
        material.SetFloat(FreshID, keepTrail ? fresh : 0f);
        material.SetFloat(PulseID, pulse);
        material.SetFloat(PulseXID, pulseX);
        material.SetFloat(PulseAgeID, pulseAge);
        material.SetColor(EmissionColorID, Color.LerpUnclamped(idleEmission, hitEmission * flashStrength, flash));
    }

    // 이 막대 몫의 재질을 만들고, 사각형 크기에 맞춰 칸을 놓는다.
    private void CreateMaterial()
    {
        if (fill.material == null) return;
        material = Instantiate(fill.material);
        fill.material = material;
        Vector2 size = fill.rectTransform.rect.size;
        float span = Mathf.Max(1f, size.x - sideMargin * 2f);
        Cells = Mathf.Max(1, Mathf.RoundToInt(span / Mathf.Max(cellPitch, 1f)));
        material.SetVector(RectSizeID, new Vector4(size.x, size.y, 0f, 0f));
        material.SetFloat(CellsID, Cells);
        material.SetFloat(CellSpanID, span);
        material.SetFloat(CellGapID, cellGap);
        material.SetFloat(CellHalfHeightID, cellHalfHeight);
        material.SetFloat(ScanTimeID, RestingScanTime);
    }
}
