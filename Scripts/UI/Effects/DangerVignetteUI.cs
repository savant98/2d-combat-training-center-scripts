using UnityEngine;
using UnityEngine.UI;

// 위험 알림: 화면 네 변을 두르는 붉은 빛(2026-11-11, 사용자 요청 "피격 조건이 미흡까지 얼마 안 남았을 때 나오는 효과는 좌·우가 빨간 Vignette인데
// 상하좌우 다 나오게 바꿔줘. player 체력이 30% 이하일 때도 똑같은 효과를 줘").
// - 켜지는 조건: 남은 피격이 1 이하(TrainingLimitWarningUI가 알린다. 2026-11-11 사용자 요청으로 5 이하에서 1 이하로 좁혔다)
//   또는 플레이어 체력이 최대의 30% 이하(여기서 직접 읽는다).
//   둘이 같은 빛 한 장을 함께 쓰고, 급한 쪽의 빠르기로 숨 쉰다. 그 상태에서 맞으면(남은 피격·체력이 줄면) 한 번 번쩍인다.
// - 훈련 기록이 없는 방에서도 체력 조건으로 떠야 하므로 상황판 표시(루트 CanvasGroup)를 따르지 않는다(ignoreParentGroups).
// - HUD의 체력 칸·보스 바·상황판 글자를 붉게 물들이지 않게, 자기 Canvas(overrideSorting)로 UI Canvas 바로 아래에 그린다.
// - 히트스톱과 사망 정지에도 멈추지 않게 실제 시간으로 진행한다. 사망하면 꺼진다(패배 화면은 자기 빛을 따로 켠다).
public class DangerVignetteUI : MonoBehaviour
{
    [SerializeField] private Canvas canvas;
    [SerializeField] private Image frame;

    [Header("Health")]
    [SerializeField, Range(0f, 1f)] private float healthThreshold = 0.3f;

    [Header("Look (실제 시간, 초)")]
    [SerializeField] private float minAlpha = 0.2f;
    [SerializeField] private float maxAlpha = 0.5f;
    [SerializeField] private float pulseAlpha = 0.4f;
    [SerializeField] private float pulseDuration = 0.4f;
    [SerializeField] private float fadeInDuration = 0.25f;
    [SerializeField] private float fadeOutDuration = 0.45f;
    // 숨쉬기 빠르기(rad/s): 여유 있음 / 급함 / 아주 급함. 체력은 30~20% / 20~10% / 10% 이하, 피격 한도는 남은 피격 1이 급함·0이 아주 급함.
    [SerializeField] private float slowRate = 3f;
    [SerializeField] private float midRate = 4.5f;
    [SerializeField] private float fastRate = 7f;

    private bool limitOn;
    private int limitLevel;
    private bool healthLow;
    private int healthLevel;
    private bool dead;
    private float lastHP = -1f;
    private int level;
    private float weight;
    private float phase;
    private float pulse = -1f;

    public bool IsHealthLow => healthLow;
    public bool IsLimitActive => limitOn;
    public bool IsOn => !dead && (healthLow || limitOn);
    public float Weight => weight;
    public int Level => level;
    public bool IsPulsing => pulse >= 0f;
    public float CurrentAlpha => frame != null && frame.enabled ? frame.color.a : 0f;

    private void Awake()
    {
        // 중첩 Canvas는 Prefab을 따로 열면 최상위 Canvas로 취급될 수 있다. 어떤 값으로 저장됐든 부모(상황판 루트 = Canvas 전체)에 맞춘다.
        var rt = (RectTransform)transform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition3D = Vector3.zero;
        rt.sizeDelta = Vector2.zero;
        rt.localScale = Vector3.one;
        rt.localRotation = Quaternion.identity;
        Draw();
    }

    private void Start()
    {
        ApplySorting();
    }

    // UI Canvas 바로 아래 순서로 그린다(같은 Sorting Layer, 순서 -1). 정점 색 처리 방식은 UI Canvas와 같게 둔다.
    public void ApplySorting()
    {
        if (canvas == null) return;
        var root = canvas.rootCanvas;
        if (root == null || root == canvas) return;
        canvas.overrideSorting = true;
        canvas.sortingLayerID = root.sortingLayerID;
        canvas.sortingOrder = root.sortingOrder - 1;
        canvas.vertexColorAlwaysGammaSpace = root.vertexColorAlwaysGammaSpace;
    }

    // 피격 한도 경고(TrainingLimitWarningUI)가 켜짐·꺼짐과 남은 피격을 알린다. 남은 피격 0(다음 피격에 실패)이 가장 빠르다.
    public void SetLimitWarning(bool on, int hitsLeft)
    {
        limitOn = on;
        limitLevel = hitsLeft <= 0 ? 2 : 1;
    }

    // 한 번 번쩍인다(경고 중 피격).
    public void Pulse()
    {
        pulse = 0f;
    }

    private void Update()
    {
        Step(Time.unscaledDeltaTime);
    }

    // dt만큼 진행하고 그린다(검사에서도 직접 부른다).
    public void Step(float dt)
    {
        ReadHealth();
        bool on = IsOn;
        float duration = on ? fadeInDuration : fadeOutDuration;
        weight = Mathf.MoveTowards(weight, on ? 1f : 0f, duration > 0f ? dt / duration : 1f);
        if (on) level = Mathf.Max(limitOn ? limitLevel : 0, healthLow ? healthLevel : 0);      // 꺼지는 동안에는 마지막 빠르기를 유지한다
        if (pulse >= 0f)
        {
            pulse += dt;
            if (pulse >= pulseDuration) pulse = -1f;
        }
        phase = Mathf.Repeat(phase + (level >= 2 ? fastRate : level == 1 ? midRate : slowRate) * dt, Mathf.PI * 2f);
        Draw();
    }

    // 체력은 UI가 읽기만 한다. 사망(IsDead)과 체력 0은 위험 표시가 아니라 패배 화면의 몫이다.
    private void ReadHealth()
    {
        var game = GameManager.Instance;
        var health = game != null && game.Player != null ? game.Player.Health : null;
        if (health == null)
        {
            healthLow = dead = false;
            lastHP = -1f;
            return;
        }

        dead = health.IsDead;
        float ratio = health.maxHP > 0f ? health.currentHP / health.maxHP : 1f;
        bool low = !dead && health.currentHP > 0f && ratio <= healthThreshold;
        if (low)
        {
            healthLevel = ratio <= healthThreshold / 3f + 0.0001f ? 2 : ratio <= healthThreshold * 2f / 3f + 0.0001f ? 1 : 0;
            if (healthLow && lastHP >= 0f && health.currentHP < lastHP - 0.001f) pulse = 0f;
        }
        healthLow = low;
        lastHP = health.currentHP;
    }

    private void Draw()
    {
        if (frame == null) return;
        bool visible = weight > 0f;
        if (frame.enabled != visible) frame.enabled = visible;
        if (!visible) return;
        float breath = 0.5f + 0.5f * Mathf.Sin(phase);
        float flash = pulse >= 0f ? 1f - TrainingBoardMotion.Seg(pulse, 0f, pulseDuration) : 0f;
        Color c = TrainingBoardStyle.Warning;
        c.a = Mathf.Clamp01(weight * (Mathf.Lerp(minAlpha, maxAlpha, breath) + pulseAlpha * flash));
        frame.color = c;
    }
}
