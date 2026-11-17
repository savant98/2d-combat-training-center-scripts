using DG.Tweening;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// 화면 위쪽 보스 체력바(2026-11-15 리메이크, 사용자 요청 "바뀐 디자인에 맞게, playerHUD의 체력을 참고하되 아주 복잡할 필요는 없다").
// PlayerHUD 체력과 같은 비스듬한 칸 셰이더(UI/PlayerHudHpFill)를 보스용 재질로 쓴다.
//   체력: 붉은 칸. 깎이면 노랑~주황 칸이 밝게 남았다가 따라 줄어든다(_Trail). 갓 깎인 조각은 밝은 노랑으로 번쩍였다가 가라앉고(_Fresh·_TrailFlash),
//     깎인 덩어리의 끝은 선으로 닫힌다(_TrailEdge). 피격 번쩍임은 밝은 붉은 주황이라 깎인 양을 덮지 않는다
//     (2026-11-15 사용자 요청 "깎일 때 얼마나 깎이는지 잘 안 보인다" → 흰색, 2026-11-17 "하얀색이라 잘 안 보여, 노란색-주황색 계열, 발광도 높여" → 앰버).
//     브레이크 중에는 깎인 양을 브레이크 시작 직전 체력에 붙잡아 두어 '이번 브레이크에 깎은 덩어리'가 커지며 보이고, 끝나면 잠시 뒤 줄어든다.
//   쉴드: 체력 위의 얇은 청록 칸.
//   쉴드가 0이 되면 쉴드 줄 오른쪽 끝에 노란 '브레이크 가능' 아이콘과 남은 시간 막대가, 브레이크 중에는 '브레이크' 표시와 남은 시간이 뜬다.
// 브레이크 누적 피해(같은 요청 "브레이크 중에 넣은 합산 데미지를 보스 옆에"): 브레이크 동안 지금 보스가 실제로 잃은 체력(EnemyHealth.DamageApplied)을 더해
//   보스 몸 옆(Player 반대쪽)의 World Space 표시 하나에 보여 주고, 끝나면 '브레이크 총 피해'로 잠시 강조한 뒤 사라진다.
//   표시는 StageHUD가 Awake에서 Scene에 하나 만들어 소유한다(보스가 처치되어 풀로 돌아가도 합계를 보여 줄 수 있게).
// 보스의 쉴드 상태는 EnemyShieldSystem의 읽기 전용 값을 매 프레임 읽기만 한다. 움직임은 실제 시간 기준이라 히트스톱 중에도 멈추지 않는다.
public class StageHUD : MonoBehaviour
{
    [Header("BossHUD")]
    [SerializeField] private CanvasGroup hpGroup;
    [Tooltip("보스 체력 칸(UI/PlayerHudHpFill 보스 재질). fillAmount가 값이고 셰이더 _Fill로 넘긴다")]
    [SerializeField] private Image hpBar;
    [Tooltip("보스 쉴드 칸(같은 셰이더의 얇은 청록 재질)")]
    [SerializeField] private Image shieldBar;
    [Tooltip("쉴드 줄 전체. 쉴드가 없는 보스면 숨긴다")]
    [SerializeField] private CanvasGroup shieldRowGroup;
    [SerializeField] private TMP_Text bossNameText;
    [Tooltip("값이 바뀔 때 위아래로 함께 튀는 대상")]
    [SerializeField] private RectTransform[] punchTargets;

    [Header("Break State")]
    [SerializeField] private CanvasGroup breakableGroup;
    [Tooltip("브레이크 가능 남은 시간 막대(Filled, 가로)")]
    [SerializeField] private Image breakableTimer;
    [SerializeField] private RectTransform breakableIcon;
    [SerializeField] private CanvasGroup breakGroup;
    [Tooltip("브레이크 남은 시간 막대(Filled, 가로)")]
    [SerializeField] private Image breakTimer;

    [Header("Colors")]
    [SerializeField] private Color hpIdleEmission = new Color(1f, 0.26f, 0.12f, 1f);
    [Tooltip("피격 때 체력 칸 전체의 번쩍임. 깎인 양(노랑~주황)을 덮지 않게 밝은 붉은 주황")]
    [SerializeField] private Color hpDamageEmission = new Color(1f, 0.42f, 0.28f, 1f);
    [SerializeField] private Color shieldIdleEmission = new Color(0.25f, 0.9f, 1f, 1f);
    [SerializeField] private float flashStrength = 1.7f;
    [SerializeField] private float flashDuration = 0.25f;

    [Header("Motion")]
    [SerializeField] private float fillDuration = 0.2f;
    [Tooltip("평소 깎인 양: 마지막 타격 뒤 남아 있는 시간과 따라 줄어드는 시간(InQuad — 머물다 빠르게 빠진다)")]
    [SerializeField] private float trailDelay = 0.7f;
    [SerializeField] private float trailDuration = 0.45f;
    [Tooltip("브레이크 중 붙잡아 둔 깎인 양: 브레이크가 끝난 뒤 기다리는 시간과 줄어드는 시간")]
    [SerializeField] private float breakTrailDelay = 0.35f;
    [SerializeField] private float breakTrailDuration = 0.6f;
    [SerializeField] private float scanSpeed = 1f;
    [SerializeField] private float scanBoost = 2.5f;
    [SerializeField] private float pulseDuration = 1.2f;
    [SerializeField] private float punchAmount = 0.12f;
    [SerializeField] private float stateFadeSpeed = 6f;

    [Header("Break Damage Counter")]
    [Tooltip("보스 옆 브레이크 누적 피해 표시(World Space Canvas + CanvasGroup, 자식 'Label'·'Value' TMP). Awake에서 Scene에 하나 만든다")]
    [SerializeField] private CanvasGroup breakDamageCounterPrefab;
    [Tooltip("보스 몸(Collider2D) 가장자리에서 표시 가장자리까지의 거리(칸)")]
    [SerializeField] private float counterGap = 1f;
    [Tooltip("보스 몸 중심보다 올리는 높이(칸)")]
    [SerializeField] private float counterRise = 0.6f;
    [Tooltip("숫자가 합계까지 따라 오르는 시간과 타격마다 숫자가 튀는 크기, 브레이크가 끝날 때 튀는 크기")]
    [SerializeField] private float counterCountDuration = 0.25f;
    [SerializeField] private float counterHitPunch = 0.18f;
    [SerializeField] private float counterTotalPunch = 0.35f;
    [Tooltip("브레이크가 끝난 뒤 '브레이크 총 피해'를 보여 주는 시간과 사라지는 시간")]
    [SerializeField] private float counterHoldDuration = 1.6f;
    [SerializeField] private float counterFadeDuration = 0.4f;

    [Header("Warning Effect")]
    [SerializeField] private GameObject warningEffect;
    private Coroutine warningRoutine;

    private static readonly int FillID = Shader.PropertyToID("_Fill");
    private static readonly int TrailID = Shader.PropertyToID("_Trail");
    private static readonly int ScanTimeID = Shader.PropertyToID("_ScanTime");
    private static readonly int PulseID = Shader.PropertyToID("_Pulse");
    private static readonly int PulseXID = Shader.PropertyToID("_PulseX");
    private static readonly int PulseAgeID = Shader.PropertyToID("_PulseAge");
    private static readonly int EmissionColorID = Shader.PropertyToID("_EmissionColor");
    private static readonly int FreshID = Shader.PropertyToID("_Fresh");

    private const string CounterLabel = "브레이크 피해";
    private const string CounterTotalLabel = "브레이크 총 피해";
    private const float CounterFadeIn = 0.12f;         // 나타나는 시간(초)
    private const float CounterSlide = 0.12f;          // Player가 보스를 넘어가 반대쪽으로 옮길 때 미끄러지는 시간(초)
    private const float CounterSideDeadZone = 0.3f;    // Player가 보스 중심에서 이만큼(칸) 넘어가야 쪽을 바꾼다
    private const float CounterViewMargin = 0.25f;     // 화면 가장자리 여유(칸). 반대쪽이 화면 밖이면 Player 쪽에 둔다

    private Material hpMaterial;
    private Material shieldMaterial;
    private float hpTrail = 1f;
    private Tween hpTrailTween;
    private float hpPrevious = 1f;
    private float hpPulse, hpPulseX = 1f, hpPulseAge = 9f, hpScanTime;
    private float shieldPulse, shieldPulseX = 1f, shieldPulseAge = 9f, shieldScanTime;
    private EnemyController boss;
    private EnemyShieldSystem bossShield;
    private float hpFresh;              // 이번 타격 직전 체력(갓 깎인 조각의 시작, 셰이더 _Fresh). 회복이면 0
    private bool breakActive;           // 이 HUD가 처리 중인 브레이크(시작·끝을 한 번씩만 처리한다)
    private bool holdTrail;             // 브레이크 중: 깎인 양을 브레이크 시작 직전 체력에 붙잡아 둔다

    private enum CounterState { Hidden, Counting, Total }
    private CanvasGroup counterGroup;
    private Canvas counterCanvas;
    private Transform counterRoot;
    private TMP_Text counterLabel, counterValue;
    private RectTransform counterLabelRect, counterValueRect;
    private CounterState counterState;
    private float breakDamageTotal, counterShown, counterHideTime, counterHalfWidth;
    private int counterShownInt = -1;
    private Tween counterTween;
    private EnemyController bodyOwner;
    private Collider2D bodyCollider;
    private Vector2 bodyOffset, bodyExtents;
    private float counterFarSide = 1f, counterSide = 1f, counterSideVelocity;
    private Vector3 counterPosition;

    // 보스 HP 바 자리(훈련 코너 상황판이 겹치지 않게 피할 때 쓴다).
    // 피격 펀치(punchTargets의 Y 스케일)가 바의 월드 사각형을 흔들면 상황판이 매 타격마다 다시 피하며 떨리므로,
    // 펀치를 받지 않는 같은 자리·크기의 빈 사각형(BossBarArea)을 돌려준다.
    public RectTransform BossBarRect => BarArea();
    private RectTransform barArea, barAreaInner, barAreaSource;
    // 지금 체력바가 보여 주는 보스(검증·연출이 읽기만 한다).
    public EnemyController CurrentBoss => boss;

    private void Awake()
    {
        hpMaterial = CopyMaterial(hpBar);
        shieldMaterial = CopyMaterial(shieldBar);
        if (hpMaterial != null) hpMaterial.SetColor(EmissionColorID, hpIdleEmission);
        if (shieldMaterial != null) shieldMaterial.SetColor(EmissionColorID, shieldIdleEmission);
        if (breakableGroup != null) breakableGroup.alpha = 0f;
        if (breakGroup != null) breakGroup.alpha = 0f;
        CreateCounter();
        HideWarningLine();
    }

    private void OnEnable() => EnemyHealth.DamageApplied += OnEnemyDamageApplied;

    private void OnDisable()
    {
        EnemyHealth.DamageApplied -= OnEnemyDamageApplied;
        HideWarningLine();
        ResetBreakTracking();
    }

    private void OnDestroy()
    {
        if (hpMaterial != null) Destroy(hpMaterial);
        if (shieldMaterial != null) Destroy(shieldMaterial);
        counterTween?.Kill();
        if (counterGroup != null) Destroy(counterGroup.gameObject);
    }

    private static Material CopyMaterial(Image image)
    {
        if (image == null || image.material == null) return null;
        var copy = Instantiate(image.material);
        image.material = copy;
        return copy;
    }

    public void ShowWarningLine()
    {
        HideWarningLine();
        if (warningEffect == null || !isActiveAndEnabled) return;
        warningEffect.SetActive(true);
        warningRoutine = StartCoroutine(HideWarningAfterAnimation());
    }

    // 화살표 LineRenderer까지 함께 끈다. CanvasGroup의 투명도만으로는 화살표가 남는다.
    public void HideWarningLine()
    {
        if (warningRoutine != null) StopCoroutine(warningRoutine);
        warningRoutine = null;
        if (warningEffect != null) warningEffect.SetActive(false);
    }

    private IEnumerator HideWarningAfterAnimation()
    {
        float duration = 3f;
        var animator = warningEffect.GetComponent<Animator>();
        if (animator != null && animator.runtimeAnimatorController != null)
        {
            duration = 0f;
            foreach (var clip in animator.runtimeAnimatorController.animationClips)
                duration = Mathf.Max(duration, clip.length);
            duration /= Mathf.Max(.01f, Mathf.Abs(animator.speed));
        }
        // 경고 Animator와 같은 게임 시간을 써서 일시정지 중에는 종료도 기다린다.
        yield return new WaitForSeconds(duration);
        warningRoutine = null;
        if (warningEffect != null) warningEffect.SetActive(false);
    }

    public void ShowBossHP() => ShowBossHP(null);

    // boss가 있으면 그 보스의 표준 이름과 지금 체력·쉴드에서 시작한다(없으면 가득 찬 상태).
    public void ShowBossHP(EnemyController target)
    {
        ResetBreakTracking();
        boss = target;
        bossShield = target != null && target.Health != null ? target.Health.enemyShieldSystem : null;
        if (bossNameText != null && target != null)
            bossNameText.text = target.DisplayName;

        float hp = 1f, shield = 1f;
        if (target != null && target.Health != null && target.Health.maxHP > 0f)
            hp = Mathf.Clamp01(target.Health.currentHP / target.Health.maxHP);
        if (bossShield != null && bossShield.MaximumShield > 0f)
            shield = Mathf.Clamp01(bossShield.CurrentShield / bossShield.InitialMaximumShield);

        SetImmediate(hpBar, hp);
        SetImmediate(shieldBar, shield);
        hpTrailTween?.Kill();
        hpTrail = hpPrevious = hp;
        hpFresh = 0f;
        if (shieldRowGroup != null)
            shieldRowGroup.alpha = target == null || bossShield != null ? 1f : 0f;

        hpGroup.DOKill();
        hpGroup.DOFade(1f, 0.5f).SetUpdate(true);
    }

    public void HideBossHP()
    {
        hpGroup.DOKill();
        hpGroup.DOFade(0f, 0.5f).SetUpdate(true);
        boss = null;
        bossShield = null;
        ResetBreakTracking();
    }

    public void UpdateBossHP(float normalized)
    {
        normalized = Mathf.Clamp01(normalized);
        float previous = hpPrevious;
        float change = normalized - previous;
        hpPrevious = normalized;
        Animate(hpBar, normalized);

        // 브레이크 중에는 깎인 양을 붙잡아 두고(holdTrail), 브레이크가 끝나면 EndBreak가 줄인다.
        hpTrailTween?.Kill();
        if (normalized >= hpTrail)
            hpTrail = normalized;
        else if (!holdTrail)
            hpTrailTween = ShrinkTrail(normalized, trailDelay, trailDuration);

        if (Mathf.Approximately(change, 0f)) return;
        hpFresh = change < 0f ? previous : 0f;
        hpPulse = 1f;
        hpPulseX = normalized;
        hpPulseAge = 0f;
        Flash(hpMaterial, change < 0f ? hpDamageEmission : hpIdleEmission, hpIdleEmission);
        Punch();
    }

    private Tween ShrinkTrail(float target, float delay, float duration) =>
        DOTween.To(() => hpTrail, v => hpTrail = v, target, duration).SetDelay(delay).SetEase(Ease.InQuad).SetUpdate(true);

    public void UpdateBossShield(float normalized)
    {
        normalized = Mathf.Clamp01(normalized) * (bossShield != null ? bossShield.CapacityRatio : 1f);
        if (shieldBar != null && !Mathf.Approximately(normalized, shieldBar.fillAmount))
        {
            shieldPulse = 1f;
            shieldPulseX = normalized;
            shieldPulseAge = 0f;
        }
        Animate(shieldBar, normalized);
    }

    private void SetImmediate(Image bar, float value)
    {
        if (bar == null) return;
        bar.DOKill();
        bar.fillAmount = value;
    }

    private void Animate(Image bar, float value)
    {
        if (bar == null) return;
        bar.DOKill();
        bar.DOFillAmount(value, fillDuration).SetEase(Ease.OutQuad).SetUpdate(true);
    }

    private void Flash(Material material, Color flash, Color idle)
    {
        if (material == null) return;
        material.DOKill();
        material.SetColor(EmissionColorID, flash * flashStrength);
        material.DOColor(idle, EmissionColorID, flashDuration).SetEase(Ease.OutQuad).SetUpdate(true);
    }

    // hpBar의 조상 중 펀치를 받는 가장 바깥 사각형을 찾아, 그 형제로 스케일 1인 사본을 만든다.
    // hpBar가 그 사각형의 바로 아래 자식이면 사본 아래에 hpBar와 같은 배치의 자식을 하나 더 둔다.
    private RectTransform BarArea()
    {
        if (hpBar == null) return null;
        var bar = hpBar.rectTransform;
        RectTransform source = null;
        if (punchTargets != null)
            for (var t = bar; t != null && t != transform; t = t.parent as RectTransform)
                if (System.Array.IndexOf(punchTargets, t) >= 0) source = t;
        if (source == null || source.parent == null) return bar;
        if (barArea == null || barAreaSource != source)
        {
            if (barArea != null) Destroy(barArea.gameObject);
            barAreaSource = source;
            barArea = new GameObject("BossBarArea", typeof(RectTransform), typeof(LayoutElement)).GetComponent<RectTransform>();
            barArea.GetComponent<LayoutElement>().ignoreLayout = true;
            barArea.SetParent(source.parent, false);
            barAreaInner = null;
            if (bar != source && bar.parent == source)
            {
                barAreaInner = new GameObject("Bar", typeof(RectTransform)).GetComponent<RectTransform>();
                barAreaInner.SetParent(barArea, false);
            }
        }
        SyncBarArea();
        return barAreaInner != null ? barAreaInner : barArea;
    }

    private void SyncBarArea()
    {
        if (barArea == null || barAreaSource == null) return;
        Copy(barAreaSource, barArea);
        barArea.localScale = Vector3.one;
        if (barAreaInner != null && hpBar != null) Copy(hpBar.rectTransform, barAreaInner);
    }

    private static void Copy(RectTransform from, RectTransform to)
    {
        to.anchorMin = from.anchorMin; to.anchorMax = from.anchorMax; to.pivot = from.pivot;
        to.anchoredPosition = from.anchoredPosition; to.sizeDelta = from.sizeDelta;
        to.localRotation = from.localRotation; to.localScale = Vector3.one;
    }

    private void LateUpdate()
    {
        SyncBarArea();
        UpdateCounter(Time.unscaledDeltaTime);
    }

    private void Punch()
    {
        if (punchTargets == null) return;
        foreach (var target in punchTargets)
        {
            if (target == null) continue;
            target.DOKill(true);
            target.localScale = Vector3.one;
            target.DOPunchScale(new Vector3(0f, punchAmount, 0f), 0.26f, 6, 0.6f).SetUpdate(true);
        }
    }

    private void Update()
    {
        float dt = Time.unscaledDeltaTime;

        hpPulse = Mathf.MoveTowards(hpPulse, 0f, dt / Mathf.Max(pulseDuration, 0.01f));
        hpPulseAge += dt;
        hpScanTime += dt * (scanSpeed + hpPulse * scanBoost);
        if (hpMaterial != null && hpBar != null)
        {
            hpMaterial.SetFloat(FillID, hpBar.fillAmount);
            hpMaterial.SetFloat(TrailID, Mathf.Max(hpTrail, hpBar.fillAmount));
            hpMaterial.SetFloat(ScanTimeID, hpScanTime);
            hpMaterial.SetFloat(PulseID, hpPulse);
            hpMaterial.SetFloat(PulseXID, hpPulseX);
            hpMaterial.SetFloat(PulseAgeID, hpPulseAge);
            hpMaterial.SetFloat(FreshID, hpFresh);
        }

        shieldPulse = Mathf.MoveTowards(shieldPulse, 0f, dt / Mathf.Max(pulseDuration, 0.01f));
        shieldPulseAge += dt;
        shieldScanTime += dt * (scanSpeed + shieldPulse * scanBoost);
        if (shieldMaterial != null && shieldBar != null)
        {
            shieldMaterial.SetFloat("_Capacity", bossShield != null ? bossShield.CapacityRatio : 1f);
            shieldMaterial.SetFloat(FillID, shieldBar.fillAmount);
            shieldMaterial.SetFloat(TrailID, shieldBar.fillAmount);
            shieldMaterial.SetFloat(ScanTimeID, shieldScanTime);
            shieldMaterial.SetFloat(PulseID, shieldPulse);
            shieldMaterial.SetFloat(PulseXID, shieldPulseX);
            shieldMaterial.SetFloat(PulseAgeID, shieldPulseAge);
        }

        UpdateBreakState(dt);
    }

    private void UpdateBreakState(float dt)
    {
        bool breakable = bossShield != null && bossShield.isActiveAndEnabled && bossShield.IsBreakable;
        bool breaking = IsBossBreaking();
        SyncBreak(breaking);

        if (breakableGroup != null)
            breakableGroup.alpha = Mathf.MoveTowards(breakableGroup.alpha, breakable ? 1f : 0f, dt * stateFadeSpeed);
        if (breakGroup != null)
            breakGroup.alpha = Mathf.MoveTowards(breakGroup.alpha, breaking ? 1f : 0f, dt * stateFadeSpeed);

        if (breakable && breakableTimer != null)
            breakableTimer.fillAmount = bossShield.BreakableRemaining01;
        if (breaking && breakTimer != null)
            breakTimer.fillAmount = bossShield.BreakRemaining01;

        // 브레이크 가능 아이콘은 숨 쉬듯 커졌다 작아져 눈길을 끈다.
        if (breakableIcon != null)
            breakableIcon.localScale = Vector3.one * (breakable ? 1f + 0.08f * Mathf.Sin(Time.unscaledTime * 7f) : 1f);
    }

    private bool IsBossBreaking() => bossShield != null && bossShield.isActiveAndEnabled && bossShield.IsBreaking;

    // 브레이크의 시작(false → true)과 끝(true → false, 보스 비활성 포함)을 한 번씩만 처리한다.
    private void SyncBreak(bool breaking)
    {
        if (breaking == breakActive) return;
        breakActive = breaking;
        if (breaking) BeginBreak();
        else EndBreak();
    }

    private void BeginBreak()
    {
        // 깎인 양을 브레이크 시작 직전 체력에 붙잡는다. 브레이크 동안 깎은 덩어리가 막대 위에서 커지며 보인다.
        hpTrailTween?.Kill();
        hpTrail = hpPrevious;
        holdTrail = true;

        breakDamageTotal = 0f;
        if (counterGroup == null) return;
        counterTween?.Kill();
        counterShown = 0f;
        SetCounterLabel(CounterLabel);
        SetCounterValue(0f);
        counterState = CounterState.Counting;
        if (counterCanvas != null) counterCanvas.enabled = true;
        PlaceCounter(0f, true);
    }

    private void EndBreak()
    {
        holdTrail = false;
        hpTrailTween?.Kill();
        if (hpTrail > hpPrevious)
            hpTrailTween = ShrinkTrail(hpPrevious, breakTrailDelay, breakTrailDuration);
        else
            hpTrail = hpPrevious;

        if (counterGroup == null || counterState != CounterState.Counting) return;
        counterTween?.Kill();
        counterShown = breakDamageTotal;
        SetCounterValue(counterShown);
        SetCounterLabel(CounterTotalLabel);
        PunchText(counterValueRect, counterTotalPunch, 0.32f);
        PunchText(counterLabelRect, counterTotalPunch * 0.5f, 0.32f);
        counterState = CounterState.Total;
        counterHideTime = Time.unscaledTime + counterHoldDuration;
    }

    // 브레이크 중 지금 보스가 실제로 잃은 체력만 더한다(읽기만 하고 게임 상태는 바꾸지 않는다).
    // 체력이 줄기 직전·보스 체력바 갱신 전에 불리므로, 브레이크에 들어간 그 프레임의 타격이면 여기서 브레이크 시작을 먼저 처리한다.
    private void OnEnemyDamageApplied(EnemyHealth health, WeaponBaseSO.DamageInfo info, float applied)
    {
        if (boss == null || health == null || health != boss.Health || !IsBossBreaking()) return;
        SyncBreak(true);
        breakDamageTotal += applied;
        if (counterState != CounterState.Counting) return;
        counterTween?.Kill();
        counterTween = DOTween.To(() => counterShown, SetCounterShown, breakDamageTotal, counterCountDuration)
            .SetEase(Ease.OutCubic).SetUpdate(true);
        PunchText(counterValueRect, counterHitPunch, 0.2f);
    }

    // 보스 교체·숨김·비활성: 브레이크 추적과 누적 피해 표시를 즉시 처음 상태로 돌린다.
    private void ResetBreakTracking()
    {
        breakActive = false;
        holdTrail = false;
        breakDamageTotal = 0f;
        bodyOwner = null;
        bodyCollider = null;
        bodyOffset = new Vector2(0f, 1f);
        bodyExtents = new Vector2(1.5f, 1.5f);
        HideCounter();
    }

    private void CreateCounter()
    {
        if (breakDamageCounterPrefab == null) return;
        counterGroup = Instantiate(breakDamageCounterPrefab);
        counterGroup.name = breakDamageCounterPrefab.name;
        // 이 HUD와 같은 Scene에 둔다(Scene이 내려가면 함께 사라진다). 보스 자식이 아니라 처치 뒤에도 합계를 보여 줄 수 있다.
        var scene = gameObject.scene;
        if (scene.name == "DontDestroyOnLoad") DontDestroyOnLoad(counterGroup.gameObject);
        else if (scene.isLoaded && counterGroup.gameObject.scene != scene) SceneManager.MoveGameObjectToScene(counterGroup.gameObject, scene);
        counterGroup.interactable = false;
        counterGroup.blocksRaycasts = false;
        counterRoot = counterGroup.transform;
        counterCanvas = counterGroup.GetComponent<Canvas>();
        counterLabel = FindText(counterRoot, "Label");
        counterValue = FindText(counterRoot, "Value");
        counterLabelRect = counterLabel != null ? counterLabel.rectTransform : null;
        counterValueRect = counterValue != null ? counterValue.rectTransform : null;
        counterPosition = counterRoot.position;
        HideCounter();
    }

    private static TMP_Text FindText(Transform root, string name)
    {
        var child = root.Find(name);
        return child != null ? child.GetComponent<TMP_Text>() : null;
    }

    private void HideCounter()
    {
        counterTween?.Kill();
        counterState = CounterState.Hidden;
        counterShown = 0f;
        if (counterGroup == null) return;
        counterGroup.alpha = 0f;
        if (counterCanvas != null) counterCanvas.enabled = false;
        foreach (var rect in new[] { counterLabelRect, counterValueRect })
        {
            if (rect == null) continue;
            rect.DOKill(true);
            rect.localScale = Vector3.one;
        }
        SetCounterLabel(CounterLabel);
        SetCounterValue(0f);
    }

    private void SetCounterShown(float value)
    {
        counterShown = value;
        SetCounterValue(value);
    }

    private void SetCounterValue(float value)
    {
        int shown = Mathf.RoundToInt(value);
        if (counterValue == null || shown == counterShownInt) return;
        counterShownInt = shown;
        counterValue.text = shown.ToString("N0");
        UpdateCounterWidth();
    }

    private void SetCounterLabel(string label)
    {
        if (counterLabel == null || counterLabel.text == label) return;
        counterLabel.text = label;
        UpdateCounterWidth();
    }

    // 글자 폭(월드 칸)의 절반. 보스 몸에서 표시 가장자리까지 counterGap을 지키고 화면 안에 드는지 볼 때 쓴다.
    private void UpdateCounterWidth()
    {
        if (counterRoot == null) return;
        float width = 0f;
        if (counterLabel != null) width = Mathf.Max(width, counterLabel.preferredWidth);
        if (counterValue != null) width = Mathf.Max(width, counterValue.preferredWidth);
        counterHalfWidth = width * 0.5f * Mathf.Abs(counterRoot.lossyScale.x);
    }

    private static void PunchText(RectTransform target, float amount, float duration)
    {
        if (target == null) return;
        target.DOKill(true);
        target.localScale = Vector3.one;
        target.DOPunchScale(Vector3.one * amount, duration, 6, 0.6f).SetUpdate(true);
    }

    private void UpdateCounter(float dt)
    {
        if (counterGroup == null || counterState == CounterState.Hidden) return;
        PlaceCounter(dt, false);
        if (counterState == CounterState.Counting || Time.unscaledTime < counterHideTime)
            counterGroup.alpha = Mathf.MoveTowards(counterGroup.alpha, 1f, dt / CounterFadeIn);
        else
        {
            counterGroup.alpha = Mathf.MoveTowards(counterGroup.alpha, 0f, dt / Mathf.Max(counterFadeDuration, 0.01f));
            if (counterGroup.alpha <= 0f) HideCounter();
        }
    }

    // 보스 몸 옆, Player 반대쪽에 둔다(그쪽이 화면 밖이고 Player 쪽은 화면 안이면 Player 쪽). 보스가 비활성이 되면 마지막 자리에 둔다.
    private void PlaceCounter(float dt, bool snap)
    {
        var cam = Camera.main;
        if (counterCanvas != null && cam != null && counterCanvas.worldCamera != cam) counterCanvas.worldCamera = cam;
        if (boss != null && boss.isActiveAndEnabled)
        {
            ReadBody();
            Vector2 center = (Vector2)boss.transform.position + bodyOffset;
            var player = GameManager.Instance != null ? GameManager.Instance.Player : null;
            if (player != null)
            {
                float dx = center.x - player.transform.position.x;
                if (snap || Mathf.Abs(dx) > CounterSideDeadZone) counterFarSide = dx >= 0f ? 1f : -1f;
            }
            float reach = bodyExtents.x + counterGap + counterHalfWidth;
            float side = counterFarSide;
            if (cam != null && cam.orthographic && !FitsView(cam, center.x + side * reach) && FitsView(cam, center.x - side * reach))
                side = -side;
            if (snap)
            {
                counterSide = side;
                counterSideVelocity = 0f;
            }
            else
                counterSide = Mathf.SmoothDamp(counterSide, side, ref counterSideVelocity, CounterSlide, Mathf.Infinity, Mathf.Max(dt, 1e-5f));
            counterPosition = new Vector3(center.x + counterSide * reach, center.y + counterRise, boss.transform.position.z);
        }
        if (counterRoot != null) counterRoot.position = counterPosition;
    }

    private bool FitsView(Camera cam, float x)
    {
        float half = cam.orthographicSize * cam.aspect - CounterViewMargin;
        float cx = cam.transform.position.x;
        return x - counterHalfWidth >= cx - half && x + counterHalfWidth <= cx + half;
    }

    // 몸 크기는 보스가 바뀔 때 한 번 고른 Collider2D에서 읽는다. 꺼져 있으면(얼림 등) 마지막 값을 쓴다.
    private void ReadBody()
    {
        if (bodyOwner != boss)
        {
            bodyOwner = boss;
            bodyCollider = FindBodyCollider(boss);
        }
        if (bodyCollider == null || !bodyCollider.enabled || !bodyCollider.gameObject.activeInHierarchy) return;
        var bounds = bodyCollider.bounds;
        bodyOffset = bounds.center - boss.transform.position;
        bodyExtents = bounds.extents;
    }

    // 루트 Collider2D(모터 몸통) → 피격 판정(EnemyHealth 객체) → 켜진 자식 Collider 중 가장 큰 것 순서로 고른다.
    // 보스 Prefab마다 몸통 Collider 위치가 달라(Stomper는 루트, Supervisor·Admin·Nightmare는 HitBox, Constructor는 PhysicsBox) 공개 API로 찾는다.
    private static Collider2D FindBodyCollider(EnemyController target)
    {
        var body = target.GetComponent<Collider2D>();
        if (body != null) return body;
        if (target.Health != null)
        {
            body = target.Health.GetComponent<Collider2D>();
            if (body != null) return body;
        }
        float largest = 0f;
        foreach (var each in target.GetComponentsInChildren<Collider2D>())
        {
            if (!each.enabled) continue;
            var size = each.bounds.size;
            if (size.x * size.y <= largest) continue;
            largest = size.x * size.y;
            body = each;
        }
        return body;
    }
}
