using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class PlayerGearUI : MonoBehaviour
{
    private static readonly int EmissionColorID = Shader.PropertyToID("_EmissionColor");

    [Header("Root")]
    [SerializeField] private GameObject root;

    [Header("Current Stage Weapon")]
    [SerializeField] private Image stageWeaponIcon;
    [SerializeField] private Image stageWeaponCircle;
    [SerializeField] private Image stageWeaponCooldownFill;
    [SerializeField] private CanvasGroup stageWeaponGroup;
    [SerializeField] private TextMeshProUGUI stageWeaponCooldownText;

    [Header("Current Stage Weapon Ready Visual")]
    [SerializeField] private Material weaponReadyMaterial;
    [SerializeField] private Material weaponPreparingMaterial;
    [SerializeField] private float readyPulseScale = 1.25f;
    [SerializeField] private float readyPulseDuration = 0.22f;
    [SerializeField] private float readyIconAlpha = 1f;
    [SerializeField] private float preparingIconAlpha = 0.35f;
    [SerializeField] private float stageWeaponCircleRotateSpeed = 25f;

    [Header("Gear Layout")]
    [Tooltip("큰 기어. 왼쪽 시작 지점 역할.")]
    [SerializeField] private RectTransform bigGear;

    [Tooltip("작은 기어. fill 값에 따라 큰 기어와 오른쪽 끝 사이를 이동.")]
    [SerializeField] private RectTransform smallGear;

    [Tooltip("작은 기어가 fill=0일 때 위치.")]
    [SerializeField] private RectTransform smallGearStartPoint;

    [Tooltip("작은 기어가 fill=1일 때 위치.")]
    [SerializeField] private RectTransform smallGearEndPoint;

    [Header("Line")]
    [Tooltip("큰 기어와 작은 기어 사이 직선 Fill 이미지.")]
    [SerializeField] private Image lineFillImage;

    [Header("Texts")]
    [SerializeField] private TextMeshProUGUI stageText;

    [Header("Emission Targets")]
    [Tooltip("큰 기어에 달린 Image. 없으면 bigGear의 Graphic을 자동 탐색.")]
    [SerializeField] private Graphic bigGearGraphic;

    [Tooltip("라인 Graphic. 없으면 lineFillImage 사용.")]
    [SerializeField] private Graphic lineGraphic;

    [Header("Emission Colors")]
    [SerializeField] private Color inactiveEmission = new Color(0.05f, 0.05f, 0.05f, 1f);
    [SerializeField] private Color stage1Emission = new Color(0.25f, 0.85f, 1f, 1f);
    [SerializeField] private Color stage2Emission = new Color(1f, 0.8f, 0.2f, 1f);
    [SerializeField] private Color stage3Emission = new Color(1f, 0.25f, 0.1f, 1f);
    [SerializeField] private Color flashEmission = Color.white;

    [Header("Emission Intensity")]
    [SerializeField] private float inactiveEmissionIntensity = 0.25f;
    [SerializeField] private float baseEmissionIntensity = 1.2f;
    [SerializeField] private float waitingEmissionIntensity = 1.6f;
    [SerializeField] private float flashEmissionIntensity = 3.5f;

    [Header("Motion")]
    [SerializeField] private float bigGearRotationSpeed = 240f;
    [SerializeField] private float moveLerpSpeed = 35f;
    [SerializeField] private bool instantMoveOnStageUp = true;

    [Header("Motion - Shift")]
    [Tooltip("준비를 시작할 때 / 다 찼을 때의 톱니 회전 배율(모터가 올라가듯 빨라진다)")]
    [SerializeField] private float chargeSpinStart = 0.4f;
    [SerializeField] private float chargeSpinEnd = 1.7f;

    [Tooltip("대기 시간을 나누는 칸 수. 칸마다 톱니가 시계처럼 한 칸씩 끊어 돈다")]
    [SerializeField] private int waitTickCount = 8;
    [SerializeField] private float waitTickDegrees = 30f;
    [SerializeField] private float waitTickSharpness = 45f;

    [Tooltip("준비 완료 순간 단계 숫자와 무기 아이콘이 찍히는 동작")]
    [SerializeField] private float chargingTextAlpha = 0.6f;
    [SerializeField] private float stageStampScale = 1.7f;
    [SerializeField] private float stageStampDuration = 0.16f;
    [SerializeField] private float weaponIconPopScale = 0.6f;
    [SerializeField] private float weaponIconPopDuration = 0.14f;

    [Header("Gear Rack")]
    [Tooltip("작은 톱니의 피치 반지름. 게이지의 톱니 레일을 따라 구르는 각도를 정한다")]
    [SerializeField] private float pinionRollRadius = 19.9f;

    [Tooltip("대기 중 게이지가 한 칸 줄어드는 속도(초당 전체 길이)")]
    [SerializeField] private float waitFillSpeed = 2.5f;

    [Header("Stage Pips")]
    [Tooltip("지금까지 물린 단계를 보여 주는 표시 3개")]
    [SerializeField] private Image[] stagePips;
    [SerializeField] private Color stagePipOffColor = new Color(0.16f, 0.2f, 0.26f, 1f);

    [Header("Appear / Warning")]
    [SerializeField] private float appearScaleFrom = 0.86f;
    [SerializeField] private float appearDuration = 0.07f;

    [Tooltip("남은 시간이 이 비율보다 적으면 게이지가 깜빡인다")]
    [SerializeField, Range(0f, 1f)] private float warningFill = 0.3f;
    [SerializeField] private float warningBlinkInterval = 0.1f;
    [SerializeField, Range(0f, 1f)] private float warningBlinkLevel = 0.45f;

    [Header("Stage Punch")]
    [SerializeField] private float punchScalePower = 0.18f;
    [SerializeField] private float punchDuration = 0.18f;
    [SerializeField] private int punchVibrato = 8;
    [SerializeField] private float flashDuration = 0.12f;

    [Header("Feedback")]
    [Tooltip("단계가 확정되어 Waiting으로 들어갈 때 재생.")]
    [SerializeField] private FeedbackDataSO stageUpFeedback;

    [Tooltip("기어가 돌아가는 동안 주기적으로 발생시키는 Spark Feedback.")]
    [SerializeField] private FeedbackDataSO sparkFeedback;

    [SerializeField] private Transform bigGearSparkPoint;
    [SerializeField] private Transform smallGearSparkPoint;
    [SerializeField] private float sparkInterval = 0.08f;
    [SerializeField] private bool playSparkOnlyWhenActive = true;

    [Tooltip("다 찼을 때 불꽃 간격이 이 배율만큼 짧아진다")]
    [SerializeField] private float chargeSparkRateMax = 1.6f;

    [Tooltip("대기 중 불꽃 간격 배율(맞물린 뒤에는 드문드문 튄다)")]
    [SerializeField] private float waitingSparkIntervalMultiplier = 3f;

    private bool isActive;
    private bool isWaiting;
    private int currentStage;
    private int lastStage;
    private float currentFill;
    private float targetFill;
    private bool stageWeaponReady;
    private bool hasStageWeaponReadyState;

    private Vector2 smallGearStartPos;
    private Vector2 smallGearEndPos;

    private float sparkTimer;
    private int waitTick;
    private float tickAngle;
    private float tickAngleTarget;
    private Graphic smallGearGraphic;
    private Vector3 rootBaseScale = Vector3.one;

    private Material bigGearMaterial;
    private Material smallGearMaterial;
    private Material lineMaterial;

    private Tween bigGearPunchTween;
    private Tween bigEmissionTween;
    private Tween smallEmissionTween;
    private Tween lineEmissionTween;
    private Tween stageWeaponCirclePulseTween;

    private void Awake()
    {
        CacheReferences();
        CachePositions();
        rootBaseScale = RootTransform.localScale;
        CreateMaterialInstances();

        SetVisible(false);
        hasStageWeaponReadyState = false;
        stageWeaponReady = false;
        SetStageWeaponCooldown(null, 0f, 0f, false);
    }

    private void OnDestroy()
    {
        KillTweens();
        DestroyMaterialInstances();
    }

    private void LateUpdate()
    {
        if (!isActive)
            return;

        UpdateSmoothFill();
        UpdateSmallGearPosition();
        RotateGears();
        UpdateSpark();
        RotateStageWeaponCircle();
        UpdateWarningBlink();
    }
    public void SetStageWeaponCooldown(Sprite icon, float remaining, float duration, bool ready)
    {
        bool hasIcon = icon != null;

        if (stageWeaponIcon != null)
        {
            stageWeaponIcon.gameObject.SetActive(hasIcon);
            stageWeaponIcon.sprite = icon;
        }

        if (stageWeaponCircle != null)
            stageWeaponCircle.gameObject.SetActive(hasIcon);

        if (!hasIcon)
        {
            SetStageWeaponReadyVisual(false, true);
            HideStageWeaponCooldown();

            // 다음에 다시 아이콘이 생겼을 때 Visual을 확실히 재적용
            hasStageWeaponReadyState = false;
            return;
        }

        SetStageWeaponReadyVisual(ready, false);

        float normalized = duration > 0f
            ? Mathf.Clamp01(remaining / duration)
            : 0f;

        bool coolingDown = remaining > 0.01f && duration > 0f;

        if (stageWeaponCooldownFill != null)
        {
            stageWeaponCooldownFill.gameObject.SetActive(coolingDown);
            stageWeaponCooldownFill.fillAmount = normalized;
        }

        if (stageWeaponCooldownText != null)
        {
            stageWeaponCooldownText.gameObject.SetActive(coolingDown);
            stageWeaponCooldownText.text = remaining > 1f
                ? Mathf.CeilToInt(remaining).ToString()
                : remaining.ToString("0.0");
        }
    }
    private void SetStageWeaponReadyVisual(bool ready, bool forceOff)
    {
        bool finalReady = ready && !forceOff;

        if (hasStageWeaponReadyState && stageWeaponReady == finalReady)
        {
            RefreshStageWeaponIconAlpha(finalReady);
            return;
        }

        hasStageWeaponReadyState = true;
        stageWeaponReady = finalReady;

        if (stageWeaponCircle != null)
        {
            stageWeaponCircle.material = finalReady ? weaponReadyMaterial : weaponPreparingMaterial;

            if (!finalReady)
            {
                KillStageWeaponCirclePulse();
            }
            else
            {
                if (stageWeaponCirclePulseTween == null || !stageWeaponCirclePulseTween.IsActive())
                {
                    stageWeaponCircle.rectTransform.localScale = Vector3.one;

                    stageWeaponCirclePulseTween = stageWeaponCircle.rectTransform
                        .DOScale(readyPulseScale, readyPulseDuration)
                        .SetLoops(2, LoopType.Yoyo)
                        .SetEase(Ease.OutQuad)
                        .SetUpdate(true);
                }
            }
        }

        RefreshStageWeaponIconAlpha(finalReady);
    }
    private void RefreshStageWeaponIconAlpha(bool ready)
    {
        if (stageWeaponIcon == null)
            return;

        Color color = stageWeaponIcon.color;
        color.a = ready ? readyIconAlpha : preparingIconAlpha;
        stageWeaponIcon.color = color;
    }
    private void HideStageWeaponCooldown()
    {
        if (stageWeaponCooldownFill != null)
        {
            stageWeaponCooldownFill.fillAmount = 0f;
            stageWeaponCooldownFill.gameObject.SetActive(false);
        }

        if (stageWeaponCooldownText != null)
        {
            stageWeaponCooldownText.text = "";
            stageWeaponCooldownText.gameObject.SetActive(false);
        }
    }
    private void RotateStageWeaponCircle()
    {
        if (stageWeaponCircle == null)
            return;

        stageWeaponCircle.transform.Rotate(
            new Vector3(0f, 0f, stageWeaponCircleRotateSpeed * Time.unscaledDeltaTime));
    }
    public void SetGearUI(bool active, int stage, float fill, bool isWaiting)
    {
        fill = Mathf.Clamp01(fill);

        if (!active)
        {
            SetVisible(false);
            hasStageWeaponReadyState = false;
            stageWeaponReady = false;
            SetStageWeaponCooldown(null, 0f, 0f, false);
            return;
        }

        bool appearing = !isActive;

        SetVisible(true);

        if (appearing)
            PlayAppear();

        bool wasWaiting = this.isWaiting;
        int previousStage = currentStage;

        bool enteredWaiting = !wasWaiting && isWaiting;
        bool stageChanged = stage != previousStage && stage > 0;

        stageWeaponGroup.alpha = isWaiting ? 1f : 0f;

        // 핵심:
        // Charging 중에도 이미 targetStage를 표시하기 때문에
        // stageChanged만 보면 Waiting 진입을 못 잡는다.
        bool shouldPlayStageEffect = enteredWaiting && stage > 0;

        this.isWaiting = isWaiting;
        currentStage = stage;

        // 대기 중에는 남은 시간을 칸 단위로 끊어 보여 준다(톱니가 한 칸씩 돌아오는 시각과 같다).
        targetFill = isWaiting ? QuantizeWaitFill(fill) : fill;

        if (shouldPlayStageEffect && instantMoveOnStageUp)
        {
            currentFill = 1f;
            targetFill = 1f;
            ApplyLineFill(1f);
            UpdateSmallGearPositionImmediate(1f);
        }

        UpdateTexts();
        UpdateStagePips(fill);

        // 매 프레임 덮어쓰면 체결 순간의 번쩍임과 경쟁하므로 상태가 바뀔 때만 적용한다.
        if (stage != previousStage || isWaiting != wasWaiting)
            ApplyBaseEmission();

        if (shouldPlayStageEffect)
            PlayStageUpEffect();

        lastStage = currentStage;
    }
    public void SetVisible(bool visible)
    {
        isActive = visible;

        if (root != null)
            root.SetActive(visible);
        else
            gameObject.SetActive(visible);

        if (!visible)
        {
            currentStage = 0;
            lastStage = 0;
            currentFill = 0f;
            targetFill = 0f;
            isWaiting = false;
            sparkTimer = 0f;

            KillTweens();
            KillStageWeaponCirclePulse();

            ApplyInactiveEmission();
            ApplyLineFill(0f);
            UpdateSmallGearPositionImmediate(0f);
            SetStageWeaponCooldown(null, 0f, 0f, false);
        }
    }

    private void CacheReferences()
    {
        if (bigGearGraphic == null && bigGear != null)
            bigGearGraphic = bigGear.GetComponent<Graphic>();

        if (smallGear != null)
            smallGearGraphic = smallGear.GetComponent<Graphic>();

        if (lineGraphic == null && lineFillImage != null)
            lineGraphic = lineFillImage;
    }

    private void CachePositions()
    {
        if (smallGearStartPoint != null)
            smallGearStartPos = smallGearStartPoint.anchoredPosition;
        else if (smallGear != null)
            smallGearStartPos = smallGear.anchoredPosition;

        if (smallGearEndPoint != null)
            smallGearEndPos = smallGearEndPoint.anchoredPosition;
        else if (smallGear != null)
            smallGearEndPos = smallGear.anchoredPosition;
    }

    private void CreateMaterialInstances()
    {
        bigGearMaterial = CreateGraphicMaterialInstance(bigGearGraphic);
        smallGearMaterial = CreateGraphicMaterialInstance(smallGearGraphic);
        lineMaterial = CreateGraphicMaterialInstance(lineGraphic);

        ApplyInactiveEmission();
    }

    private Material CreateGraphicMaterialInstance(Graphic graphic)
    {
        if (graphic == null)
            return null;

        Material source = graphic.material;

        if (source == null)
            return null;

        Material instance = Instantiate(source);
        graphic.material = instance;

        return instance;
    }

    private void DestroyMaterialInstances()
    {
        DestroyMaterialInstance(bigGearMaterial);
        DestroyMaterialInstance(smallGearMaterial);
        DestroyMaterialInstance(lineMaterial);

        bigGearMaterial = null;
        smallGearMaterial = null;
        lineMaterial = null;
    }

    private void DestroyMaterialInstance(Material mat)
    {
        if (mat == null)
            return;

        if (Application.isPlaying)
            Destroy(mat);
        else
            DestroyImmediate(mat);
    }

    private void UpdateSmoothFill()
    {
        currentFill = Mathf.MoveTowards(
            currentFill,
            targetFill,
            (isWaiting ? waitFillSpeed : moveLerpSpeed) * Time.unscaledDeltaTime);

        ApplyLineFill(currentFill);
    }

    private void ApplyLineFill(float fill)
    {
        if (lineFillImage != null)
            lineFillImage.fillAmount = Mathf.Clamp01(fill);
    }

    private void UpdateSmallGearPosition()
    {
        UpdateSmallGearPositionImmediate(currentFill);
    }

    private void UpdateSmallGearPositionImmediate(float fill)
    {
        if (smallGear == null)
            return;

        Vector2 position = Vector2.Lerp(
            smallGearStartPos,
            smallGearEndPos,
            Mathf.Clamp01(fill));

        smallGear.anchoredPosition = position;

        // 톱니 레일 아래를 미끄러지지 않고 구른다(오른쪽으로 갈수록 반시계 방향).
        float rolled = (position.x - smallGearStartPos.x) / Mathf.Max(1f, pinionRollRadius) * Mathf.Rad2Deg;
        smallGear.localRotation = Quaternion.Euler(0f, 0f, rolled);
    }

    private float QuantizeWaitFill(float fill)
    {
        int ticks = Mathf.Max(1, waitTickCount);
        return Mathf.Ceil(fill * ticks - 0.0001f) / ticks;
    }

    private Transform RootTransform => root != null ? root.transform : transform;

    private void PlayAppear()
    {
        Transform rootTransform = RootTransform;
        rootTransform.DOKill();
        rootTransform.localScale = rootBaseScale * appearScaleFrom;
        rootTransform
            .DOScale(rootBaseScale, appearDuration)
            .SetEase(Ease.OutQuad)
            .SetUpdate(true);
    }

    private void UpdateStagePips(float fill)
    {
        if (stagePips == null)
            return;

        Color stageColor = GetStageEmission(currentStage);
        stageColor.a = 1f;

        for (int i = 0; i < stagePips.Length; i++)
        {
            if (stagePips[i] == null)
                continue;

            bool locked = i < (isWaiting ? currentStage : currentStage - 1);
            bool charging = !isWaiting && i == currentStage - 1;

            stagePips[i].color = locked
                ? stageColor
                : charging
                    ? Color.Lerp(stagePipOffColor, stageColor, fill)
                    : stagePipOffColor;
        }
    }

    private void UpdateWarningBlink()
    {
        if (!isWaiting || lineMaterial == null)
            return;

        if (lineEmissionTween != null && lineEmissionTween.IsActive())
            return;

        bool dim = targetFill <= warningFill
            && Mathf.Repeat(Time.unscaledTime, warningBlinkInterval * 2f) >= warningBlinkInterval;

        SetEmission(
            lineMaterial,
            GetStageEmission(currentStage) * waitingEmissionIntensity * (dim ? warningBlinkLevel : 1f));
    }

    private void RotateGears()
    {
        float dt = Time.unscaledDeltaTime;

        if (isWaiting)
        {
            // 대기: 남은 시간이 한 칸 줄 때마다 시계처럼 한 칸씩 끊어 돈다(준비 때와 반대 방향).
            int tick = Mathf.FloorToInt((1f - targetFill) * Mathf.Max(1, waitTickCount));

            if (tick > waitTick)
            {
                tickAngleTarget += waitTickDegrees * (tick - waitTick);
                waitTick = tick;
            }

            float next = Mathf.Lerp(tickAngle, tickAngleTarget, 1f - Mathf.Exp(-waitTickSharpness * dt));
            float step = next - tickAngle;
            tickAngle = next;

            if (bigGear != null)
                bigGear.Rotate(0f, 0f, step);

            return;
        }

        // 준비: 채울수록 모터가 올라가듯 빨라진다.
        float spinUp = Mathf.Lerp(chargeSpinStart, chargeSpinEnd, currentFill);

        if (bigGear != null)
            bigGear.Rotate(0f, 0f, -bigGearRotationSpeed * spinUp * dt);
    }

    private void UpdateSpark()
    {
        if (playSparkOnlyWhenActive && (!isActive || currentStage <= 0))
            return;

        if (sparkFeedback == null)
            return;

        sparkTimer -= Time.unscaledDeltaTime;

        if (sparkTimer > 0f)
            return;

        // 준비 중에는 작은 톱니가 게이지를 긁으며 나아가 찰수록 불꽃이 잦아지고,
        // 대기 중에는 맞물린 큰 톱니에서 드문드문 튄다.
        sparkTimer = isWaiting
            ? sparkInterval * waitingSparkIntervalMultiplier
            : sparkInterval / Mathf.Lerp(1f, chargeSparkRateMax, currentFill);

        FeedbackPlayer feedbackPlayer = GameManager.Instance != null
            ? FeedbackPlayer.Instance
            : null;

        if (feedbackPlayer == null)
            return;

        Transform point = isWaiting ? bigGearSparkPoint : smallGearSparkPoint;

        if (point != null)
            feedbackPlayer.Play(sparkFeedback, point);
    }

    private void UpdateTexts()
    {
        if (stageText != null)
        {
            stageText.text = currentStage <= 0 ? "0" : $"{currentStage}";

            // 아직 물리지 않은 단계(준비 중)는 흐리게, 준비 완료면 또렷하게 찍는다.
            Color textColor = GetTextColor();
            textColor.a = isWaiting ? 1f : chargingTextAlpha;
            stageText.color = textColor;
        }
    }
    public Color GetStageColor(int stage)
    {
        return GetStageEmission(stage);
    }
    private Color GetTextColor()
    {
        return GetStageEmission(currentStage);
    }
    private void ApplyInactiveEmission()
    {
        Color emission = inactiveEmission * inactiveEmissionIntensity;

        SetEmission(bigGearMaterial, emission);
        SetEmission(smallGearMaterial, emission);
        SetEmission(lineMaterial, emission);
    }
    private void ApplyBaseEmission()
    {
        Color baseColor = GetStageEmission(currentStage);
        float intensity = isWaiting
            ? waitingEmissionIntensity
            : baseEmissionIntensity;

        Color emission = baseColor * intensity;

        SetEmission(bigGearMaterial, emission);
        SetEmission(smallGearMaterial, emission);
        SetEmission(lineMaterial, emission);
    }

    private Color GetStageEmission(int stage)
    {
        return stage switch
        {
            1 => stage1Emission,
            2 => stage2Emission,
            3 => stage3Emission,
            _ => inactiveEmission
        };
    }

    private void SetEmission(Material mat, Color emissionColor)
    {
        if (mat == null)
            return;

        if (!mat.HasProperty(EmissionColorID))
            return;

        mat.SetColor(EmissionColorID, emissionColor);
    }
    private void PlayStageUpEffect()
    {
        KillTweens();

        waitTick = 0;
        tickAngle = 0f;
        tickAngleTarget = 0f;

        if (bigGear != null)
        {
            bigGear.DOKill();
            bigGear.localScale = Vector3.one;
            bigGearPunchTween = bigGear
                .DOPunchScale(
                    Vector3.one * punchScalePower,
                    punchDuration,
                    punchVibrato,
                    0.8f)
                .SetUpdate(true);
        }

        if (smallGear != null)
        {
            smallGear.DOKill();
            smallGear.localScale = Vector3.one;
            smallGear
                .DOPunchScale(
                    Vector3.one * punchScalePower,
                    punchDuration,
                    punchVibrato,
                    0.8f)
                .SetUpdate(true);
        }

        if (stageText != null)
        {
            stageText.transform.localScale = Vector3.one * stageStampScale;
            stageText.transform
                .DOScale(1f, stageStampDuration)
                .SetEase(Ease.OutBack)
                .SetUpdate(true);
        }

        if (stageWeaponIcon != null)
        {
            RectTransform icon = stageWeaponIcon.rectTransform;
            icon.DOKill();
            icon.localScale = Vector3.one * weaponIconPopScale;
            icon
                .DOScale(1f, weaponIconPopDuration)
                .SetEase(Ease.OutBack)
                .SetUpdate(true);
        }

        FlashMaterialEmission(bigGearMaterial, ref bigEmissionTween);
        FlashMaterialEmission(smallGearMaterial, ref smallEmissionTween);
        FlashMaterialEmission(lineMaterial, ref lineEmissionTween);

        FeedbackPlayer feedbackPlayer = GameManager.Instance != null
            ? FeedbackPlayer.Instance
            : null;

        // 체결 효과와 소리는 PlayerGearVisual이 몸에서 재생하고, 여기서는 게이지의 큰 톱니에서 튀는 불꽃만 낸다.
        if (feedbackPlayer != null && stageUpFeedback != null)
            feedbackPlayer.Play(stageUpFeedback, bigGear != null ? bigGear : transform);
    }
    private void FlashMaterialEmission(Material mat, ref Tween tween)
    {
        if (mat == null)
            return;

        if (!mat.HasProperty(EmissionColorID))
            return;

        tween?.Kill();

        Color baseColor = GetStageEmission(currentStage);
        float baseIntensity = isWaiting
            ? waitingEmissionIntensity
            : baseEmissionIntensity;

        Color baseEmission = baseColor * baseIntensity;
        Color flashEmissionColor = flashEmission * flashEmissionIntensity;

        Sequence seq = DOTween.Sequence().SetUpdate(true);

        seq.Append(
            mat.DOColor(
                flashEmissionColor,
                EmissionColorID,
                flashDuration * 0.35f)
            .SetEase(Ease.OutQuad));

        seq.Append(
            mat.DOColor(
                baseEmission,
                EmissionColorID,
                flashDuration * 0.65f)
            .SetEase(Ease.OutQuad));

        tween = seq;
    }
    private void KillStageWeaponCirclePulse()
    {
        stageWeaponCirclePulseTween?.Kill();
        stageWeaponCirclePulseTween = null;

        if (stageWeaponCircle != null)
        {
            stageWeaponCircle.rectTransform.DOKill();
            stageWeaponCircle.rectTransform.localScale = Vector3.one;
        }
    }
    private void KillTweens()
    {
        bigGearPunchTween?.Kill();
        bigEmissionTween?.Kill();
        smallEmissionTween?.Kill();
        lineEmissionTween?.Kill();

        bigGearPunchTween = null;
        bigEmissionTween = null;
        smallEmissionTween = null;
        lineEmissionTween = null;

        if (stageText != null)
        {
            stageText.transform.DOKill();
            stageText.transform.localScale = Vector3.one;
        }

        if (smallGear != null)
        {
            smallGear.DOKill();
            smallGear.localScale = Vector3.one;
        }

        if (stageWeaponIcon != null)
        {
            stageWeaponIcon.rectTransform.DOKill();
            stageWeaponIcon.rectTransform.localScale = Vector3.one;
        }
    }
}